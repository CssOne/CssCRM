using System.Text.Json;
using CssVision.Web.Authorization;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Domain.Identity;
using CssVision.Web.Services.Crm;
using CssVision.Web.Services.Notion;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Cards do Notion ficam com o vendedor do card — nunca vão parar com outro consultor ativo.</summary>
public class NotionSyncVendedorTests
{
    private static JsonElement Pagina(string nome, string? vendedorEmail, string? whatsapp = null, string? id = null)
    {
        var props = new Dictionary<string, object>
        {
            ["Name"] = new { type = "title", title = new object[] { new { plain_text = nome } } },
            ["WhatsApp"] = new { type = "rich_text", rich_text = whatsapp is null ? Array.Empty<object>() : new object[] { new { plain_text = whatsapp } } },
            ["Status"] = new { type = "select", select = new { name = "EM ATENDIMENTO" } },
        };
        if (vendedorEmail is not null)
        {
            props["Vendedor"] = new { type = "people", people = new object[] { new { name = "Vendedor", person = new { email = vendedorEmail } } } };
        }
        var json = JsonSerializer.Serialize(new { id = id ?? Guid.NewGuid().ToString(), properties = props });
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    private sealed record Cenario(ApplicationDbContext Db, NotionSyncService Service, Guid Regional, Guid Ganho, Guid Placeholder,
        ApplicationUser Ana, ApplicationUser Bob, Dictionary<string, Guid> Etapas);

    /// <summary>Ana é consultora ativa; Bob existe no CRM mas está inativo.</summary>
    private static async Task<Cenario> PrepararAsync(TestDbContextFactory factory)
    {
        var db = factory.CreateContext();
        var regional = new CrmRegional { Nome = "MG132" };
        db.CrmRegionais.Add(regional);
        var ganho = await factory.CriarEtapaAsync(db, "Ganho", 1, TipoEtapaPipeline.Ganho);
        var etapa = await factory.ObterOuCriarEtapaLeadAsync(db, "Em atendimento (Leads)", 1);
        var placeholder = await factory.CriarUsuarioAsync(db, "Placeholder");
        placeholder.Ativo = false;
        var ana = await factory.CriarUsuarioAsync(db, "Ana");
        var bob = await factory.CriarUsuarioAsync(db, "Bob");
        bob.Ativo = false;
        await factory.AtribuirPapelAsync(db, ana, Roles.Comercial); // cria o papel usado pelos vendedores novos
        await db.SaveChangesAsync();
        var service = new NotionSyncService(db, TestDbContextFactory.CreateUserManager(db), NullLogger<NotionSyncService>.Instance);
        return new Cenario(db, service, regional.Id, ganho.Id, placeholder.Id, ana, bob,
            new Dictionary<string, Guid> { ["Em atendimento (Leads)"] = etapa.Id });
    }

    private static Task Processar(Cenario c, JsonElement pagina) =>
        c.Service.ProcessarPaginaAsync(pagina, c.Regional, "MG132", c.Etapas, c.Ganho, c.Placeholder, false, CancellationToken.None);

    [Fact]
    public async Task CardNovo_FicaComOVendedorDoCard_MesmoInativo_OuComONaoIdentificado()
    {
        using var factory = new TestDbContextFactory();
        var c = await PrepararAsync(factory);
        await using var _ = c.Db;

        await Processar(c, Pagina("Do Bob", c.Bob.Email));
        await Processar(c, Pagina("Sem vendedor", null));
        await Processar(c, Pagina("Da Ana", c.Ana.Email));

        var leads = await c.Db.CrmLeads.ToDictionaryAsync(l => l.NomeOuRazaoSocial, l => l.ResponsavelId);
        Assert.Equal(c.Bob.Id, leads["Do Bob"]);
        Assert.Equal(c.Placeholder, leads["Sem vendedor"]);
        Assert.Equal(c.Ana.Id, leads["Da Ana"]);
    }

    [Fact]
    public async Task VendedorQueSoExisteNoNotion_EntraInativo()
    {
        using var factory = new TestDbContextFactory();
        var c = await PrepararAsync(factory);
        await using var _ = c.Db;

        await Processar(c, Pagina("Cliente", "novo.vendedor@x.com"));

        var novo = await c.Db.Users.SingleAsync(u => u.Email == "novo.vendedor@x.com");
        Assert.False(novo.Ativo);
        Assert.Equal(novo.Id, (await c.Db.CrmLeads.SingleAsync()).ResponsavelId);
    }

    [Fact]
    public async Task Reimportacao_DevolveAoVendedorDoCard_LeadDoNotionQueFicouComConsultoraAtiva()
    {
        using var factory = new TestDbContextFactory();
        var c = await PrepararAsync(factory);
        await using var _ = c.Db;
        var pagina = Pagina("Cliente do Bob", c.Bob.Email, whatsapp: "31911112222");
        c.Db.CrmLeads.Add(new CrmLead
        {
            NomeOuRazaoSocial = "Cliente do Bob", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = c.Ana.Id,
            TelefoneNormalizado = "31911112222", ConsentimentoOrigem = OrigemLead.MarcadorMigracaoNotion,
        });
        await c.Db.SaveChangesAsync();

        Assert.True(await c.Service.DevolverAoVendedorDoCardAsync(pagina, c.Regional, "MG132", c.Placeholder, CancellationToken.None));

        var lead = await c.Db.CrmLeads.SingleAsync();
        Assert.Equal(c.Bob.Id, lead.ResponsavelId);
        Assert.Equal(c.Bob.Email!.ToLowerInvariant(), lead.NotionVendedorEmail);
    }

    [Fact]
    public async Task Reimportacao_NaoMexeEmLeadDoTrafegoPago_NemEmLeadAtribuidoAMao()
    {
        using var factory = new TestDbContextFactory();
        var c = await PrepararAsync(factory);
        await using var _ = c.Db;
        var admin = await factory.CriarUsuarioAsync(c.Db, "Admin");
        var doSite = new CrmLead
        {
            NomeOuRazaoSocial = "Do site", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = c.Ana.Id,
            TelefoneNormalizado = "31900000001", ConsentimentoOrigem = OrigemLead.MarcadorFormularioSite,
        };
        var atribuidoAMao = new CrmLead
        {
            NomeOuRazaoSocial = "Atribuído à mão", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = c.Ana.Id,
            TelefoneNormalizado = "31900000002", ConsentimentoOrigem = OrigemLead.MarcadorSincronizacaoNotion,
        };
        c.Db.CrmLeads.AddRange(doSite, atribuidoAMao);
        c.Db.CrmLeadAssignmentHistories.Add(new CrmLeadAssignmentHistory { LeadId = atribuidoAMao.Id, ResponsavelNovoId = c.Ana.Id, AlteradoPorId = admin.Id });
        await c.Db.SaveChangesAsync();

        await c.Service.DevolverAoVendedorDoCardAsync(Pagina("Do site", c.Bob.Email, whatsapp: "31900000001"), c.Regional, "MG132", c.Placeholder, CancellationToken.None);
        await c.Service.DevolverAoVendedorDoCardAsync(Pagina("Atribuído à mão", c.Bob.Email, whatsapp: "31900000002"), c.Regional, "MG132", c.Placeholder, CancellationToken.None);

        Assert.All(await c.Db.CrmLeads.ToListAsync(), l => Assert.Equal(c.Ana.Id, l.ResponsavelId));
    }

    [Fact]
    public async Task Incremental_TrocaFeitaNoCrm_NaoEDesfeitaEnquantoOVendedorDoCardNaoMuda()
    {
        using var factory = new TestDbContextFactory();
        var c = await PrepararAsync(factory);
        await using var _ = c.Db;
        var carla = await factory.CriarUsuarioAsync(c.Db, "Carla");
        var id = Guid.NewGuid().ToString();

        await Processar(c, Pagina("Cliente", c.Ana.Email, id: id));
        var lead = await c.Db.CrmLeads.SingleAsync();
        lead.ResponsavelId = carla.Id; // trocado no CRM
        await c.Db.SaveChangesAsync();

        await Processar(c, Pagina("Cliente", c.Ana.Email, id: id));
        Assert.Equal(carla.Id, (await c.Db.CrmLeads.SingleAsync()).ResponsavelId);

        // Vendedor do card mudou para a Carla e depois de volta para a Ana: acompanha o Notion.
        await Processar(c, Pagina("Cliente", carla.Email, id: id));
        await Processar(c, Pagina("Cliente", c.Ana.Email, id: id));
        Assert.Equal(c.Ana.Id, (await c.Db.CrmLeads.SingleAsync()).ResponsavelId);
    }
}
