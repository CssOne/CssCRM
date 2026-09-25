using System.Text.Json;
using CssVision.Web.Authorization;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Domain.Identity;
using CssVision.Web.Services.Notion;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Importação das vendas do Notion: uma oportunidade por card de venda.</summary>
public class NotionSyncVendasTests
{
    private static JsonElement Venda(string cpf, string veiculo, string? vendedorEmail, string? id = null)
    {
        var props = new Dictionary<string, object>
        {
            ["Name"] = new { type = "title", title = new object[] { new { plain_text = "Cliente" } } },
            ["CPF"] = new { type = "rich_text", rich_text = new object[] { new { plain_text = cpf } } },
            ["Status"] = new { type = "select", select = new { name = "VENDA CONCLUIDA" } },
            ["Veiculo"] = new { type = "rich_text", rich_text = new object[] { new { plain_text = veiculo } } },
            ["Mensalidade"] = new { type = "number", number = 100 },
        };
        if (vendedorEmail is not null)
        {
            props["Vendedor"] = new { type = "people", people = new object[] { new { name = "V", person = new { email = vendedorEmail } } } };
        }
        var json = JsonSerializer.Serialize(new { id = id ?? Guid.NewGuid().ToString(), properties = props });
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    private sealed record Cenario(ApplicationDbContext Db, NotionSyncService Service, Guid Regional, Guid Ganho, Guid Placeholder,
        Dictionary<string, Guid> Etapas, ApplicationUser Ana, ApplicationUser Bob);

    private static async Task<Cenario> PrepararAsync(TestDbContextFactory factory)
    {
        var db = factory.CreateContext();
        var regional = new CrmRegional { Nome = "MG132" };
        db.CrmRegionais.Add(regional);
        var ganho = await factory.CriarEtapaAsync(db, "Ganho", 1, TipoEtapaPipeline.Ganho);
        var etapa = await factory.ObterOuCriarEtapaLeadAsync(db, "Venda concluída (Indicação)", 1);
        var placeholder = await factory.CriarUsuarioAsync(db, "Placeholder");
        var ana = await factory.CriarUsuarioAsync(db, "Ana");
        var bob = await factory.CriarUsuarioAsync(db, "Bob");
        bob.Ativo = false;
        await factory.AtribuirPapelAsync(db, ana, Roles.Comercial);
        var service = new NotionSyncService(db, TestDbContextFactory.CreateUserManager(db), NullLogger<NotionSyncService>.Instance);
        return new Cenario(db, service, regional.Id, ganho.Id, placeholder.Id,
            new Dictionary<string, Guid> { ["Venda concluída (Indicação)"] = etapa.Id }, ana, bob);
    }

    private static Task Importar(Cenario c, JsonElement pagina) =>
        c.Service.ProcessarPaginaAsync(pagina, c.Regional, "MG132", c.Etapas, c.Ganho, c.Placeholder, false, CancellationToken.None, manterResponsavel: true);

    [Fact]
    public async Task ClienteComDuasVendas_FicaComDuasOportunidades_CadaUmaComOVendedorDoCard()
    {
        using var factory = new TestDbContextFactory();
        var c = await PrepararAsync(factory);
        await using var _ = c.Db;

        await Importar(c, Venda("11048168654", "UNO", c.Ana.Email));
        await Importar(c, Venda("11048168654", "SAHARA", c.Bob.Email));

        var lead = await c.Db.CrmLeads.SingleAsync();
        Assert.Equal(c.Ana.Id, lead.ResponsavelId); // o lead não troca de dono no segundo card
        var vendas = await c.Db.CrmOpportunities.Include(o => o.Veiculo).OrderBy(o => o.Veiculo!.Descricao).ToListAsync();
        Assert.Equal(["SAHARA", "UNO"], vendas.Select(o => o.Veiculo!.Descricao));
        Assert.Equal([c.Bob.Id, c.Ana.Id], vendas.Select(o => o.ResponsavelId));
        Assert.All(vendas, o => Assert.Equal(c.Ganho, o.EtapaId));
    }

    [Fact]
    public async Task VendaAntigaSemCard_EAproveitada_EReprocessarNaoDuplica()
    {
        using var factory = new TestDbContextFactory();
        var c = await PrepararAsync(factory);
        await using var _ = c.Db;
        var lead = new CrmLead { NomeOuRazaoSocial = "Cliente", TipoPessoa = TipoPessoa.Fisica, DocumentoNormalizado = "11048168654", ResponsavelId = c.Ana.Id };
        c.Db.CrmLeads.Add(lead);
        c.Db.CrmOpportunities.Add(new CrmOpportunity { LeadId = lead.Id, Titulo = "Migrada", ResponsavelId = c.Ana.Id, EtapaId = c.Ganho });
        await c.Db.SaveChangesAsync();

        var pagina = Venda("11048168654", "UNO", c.Ana.Email);
        await Importar(c, pagina);
        c.Db.ChangeTracker.Clear();
        await Importar(c, pagina);

        var venda = await c.Db.CrmOpportunities.SingleAsync();
        Assert.Equal(pagina.GetProperty("id").GetString(), venda.NotionPageId);
    }

    [Fact]
    public async Task VendaExcluidaNoCrm_NaoVolta()
    {
        using var factory = new TestDbContextFactory();
        var c = await PrepararAsync(factory);
        await using var _ = c.Db;
        var pagina = Venda("11048168654", "UNO", c.Ana.Email);
        await Importar(c, pagina);
        var venda = await c.Db.CrmOpportunities.SingleAsync();
        venda.Arquivado = true;
        await c.Db.SaveChangesAsync();
        c.Db.ChangeTracker.Clear();

        await Importar(c, pagina);

        var todas = await c.Db.CrmOpportunities.ToListAsync();
        Assert.Single(todas);
        Assert.True(todas[0].Arquivado);
    }
}
