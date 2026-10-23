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

/// <summary>
/// A sincronização do Notion entrega o lead ao vendedor do card, mas um lead que chegou direto do tráfego pago (distribuído pelo rodízio do CRM,
/// que respeita dia e horário de recebimento) não pode ir parar em quem estava fora da agenda quando ele chegou.
/// </summary>
public class NotionRespeitaAgendaTests
{
    // Segunda-feira 12:00 em Brasília.
    private static readonly DateTimeOffset SegundaMeioDia = new(2026, 10, 5, 15, 0, 0, TimeSpan.Zero);
    private const string Telefone = "31911112222";

    private static JsonElement Pagina(string nome, string vendedorEmail)
    {
        var props = new Dictionary<string, object>
        {
            ["Name"] = new { type = "title", title = new object[] { new { plain_text = nome } } },
            ["WhatsApp"] = new { type = "rich_text", rich_text = new object[] { new { plain_text = Telefone } } },
            ["Status"] = new { type = "select", select = new { name = "EM ATENDIMENTO" } },
            ["Vendedor"] = new { type = "people", people = new object[] { new { name = "Vendedor", person = new { email = vendedorEmail } } } },
        };
        var json = JsonSerializer.Serialize(new { id = Guid.NewGuid().ToString(), properties = props });
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    private sealed record Cenario(ApplicationDbContext Db, NotionSyncService Service, Guid Regional, Guid Ganho, Guid Placeholder,
        ApplicationUser Ana, ApplicationUser Carla, Dictionary<string, Guid> Etapas);

    /// <summary>Ana recebeu o lead pelo rodízio; Carla é o vendedor do card do Notion e só recebe leads à tarde (13h–18h).</summary>
    private static async Task<Cenario> PrepararAsync(TestDbContextFactory factory, bool carlaTemAgenda = true)
    {
        var db = factory.CreateContext();
        var regional = new CrmRegional { Nome = "MG132" };
        db.CrmRegionais.Add(regional);
        var ganho = await factory.CriarEtapaAsync(db, "Ganho", 1, TipoEtapaPipeline.Ganho);
        var etapa = await factory.ObterOuCriarEtapaLeadAsync(db, "Em atendimento (Leads)", 1);
        var placeholder = await factory.CriarUsuarioAsync(db, "Placeholder");
        placeholder.Ativo = false;
        var ana = await factory.CriarUsuarioAsync(db, "Ana");
        var carla = await factory.CriarUsuarioAsync(db, "Carla");
        await factory.AtribuirPapelAsync(db, ana, Roles.Comercial);
        await factory.AtribuirPapelAsync(db, carla, Roles.Comercial);
        if (carlaTemAgenda)
        {
            carla.HorarioInicioLeads = new TimeOnly(13, 0);
            carla.HorarioFimLeads = new TimeOnly(18, 0);
        }
        await db.SaveChangesAsync();
        var service = new NotionSyncService(db, TestDbContextFactory.CreateUserManager(db), NullLogger<NotionSyncService>.Instance);
        return new Cenario(db, service, regional.Id, ganho.Id, placeholder.Id, ana, carla,
            new Dictionary<string, Guid> { ["Em atendimento (Leads)"] = etapa.Id });
    }

    private static Task Processar(Cenario c, JsonElement pagina) =>
        c.Service.ProcessarPaginaAsync(pagina, c.Regional, "MG132", c.Etapas, c.Ganho, c.Placeholder, false, CancellationToken.None);

    private static CrmLead LeadDoTrafego(Guid responsavelId, DateTimeOffset chegouEm) => new()
    {
        NomeOuRazaoSocial = "Cliente do anúncio", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = responsavelId,
        TelefoneNormalizado = Telefone, MetaLeadId = Guid.NewGuid().ToString(), CriadoManualmente = false, CriadoEm = chegouEm,
    };

    [Fact]
    public async Task LeadDoTrafego_ChegouForaDaAgendaDoVendedorDoCard_NaoVaiParaEle()
    {
        using var factory = new TestDbContextFactory();
        var c = await PrepararAsync(factory);
        await using var _ = c.Db;
        c.Db.CrmLeads.Add(LeadDoTrafego(c.Ana.Id, SegundaMeioDia)); // meio-dia: Carla só trabalha à tarde
        await c.Db.SaveChangesAsync();

        await Processar(c, Pagina("Cliente do anúncio", c.Carla.Email!));

        Assert.Equal(c.Ana.Id, (await c.Db.CrmLeads.SingleAsync()).ResponsavelId);
    }

    [Fact]
    public async Task LeadDoTrafego_ChegouDentroDaAgendaDoVendedorDoCard_AcompanhaOCard()
    {
        using var factory = new TestDbContextFactory();
        var c = await PrepararAsync(factory);
        await using var _ = c.Db;
        c.Db.CrmLeads.Add(LeadDoTrafego(c.Ana.Id, SegundaMeioDia.AddHours(3))); // 15h em Brasília: dentro de 13h–18h
        await c.Db.SaveChangesAsync();

        await Processar(c, Pagina("Cliente do anúncio", c.Carla.Email!));

        Assert.Equal(c.Carla.Id, (await c.Db.CrmLeads.SingleAsync()).ResponsavelId);
    }

    [Fact]
    public async Task LeadDoTrafego_VendedorDoCardSemAgenda_AcompanhaOCard()
    {
        using var factory = new TestDbContextFactory();
        var c = await PrepararAsync(factory, carlaTemAgenda: false);
        await using var _ = c.Db;
        c.Db.CrmLeads.Add(LeadDoTrafego(c.Ana.Id, SegundaMeioDia));
        await c.Db.SaveChangesAsync();

        await Processar(c, Pagina("Cliente do anúncio", c.Carla.Email!));

        Assert.Equal(c.Carla.Id, (await c.Db.CrmLeads.SingleAsync()).ResponsavelId);
    }

    [Fact]
    public async Task LeadDoNotion_ContinuaComOVendedorDoCard_MesmoForaDaAgenda()
    {
        using var factory = new TestDbContextFactory();
        var c = await PrepararAsync(factory);
        await using var _ = c.Db;
        var doNotion = LeadDoTrafego(c.Ana.Id, SegundaMeioDia);
        doNotion.MetaLeadId = null;
        doNotion.ConsentimentoOrigem = OrigemLead.MarcadorSincronizacaoNotion; // veio do Notion: quem manda é o card
        c.Db.CrmLeads.Add(doNotion);
        await c.Db.SaveChangesAsync();

        await Processar(c, Pagina("Cliente do anúncio", c.Carla.Email!));

        Assert.Equal(c.Carla.Id, (await c.Db.CrmLeads.SingleAsync()).ResponsavelId);
    }
}
