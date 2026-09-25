using System.Text.Json;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Notion;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Cards do Notion com dados faltando ou conflitantes continuam virando lead, com o que tiver.</summary>
public class NotionSyncDadosCompletosTests
{
    private static object Texto(string? valor) => new
    {
        type = "rich_text",
        rich_text = valor is null ? Array.Empty<object>() : new object[] { new { plain_text = valor } },
    };

    private static JsonElement Pagina(string? nome, string? whatsapp = null, string? email = null, string? campanha = null, string? vendedorEmail = null)
    {
        var props = new Dictionary<string, object>
        {
            ["Name"] = new { type = "title", title = nome is null ? Array.Empty<object>() : new object[] { new { plain_text = nome } } },
            ["WhatsApp"] = Texto(whatsapp),
            ["E-mail"] = Texto(email),
            ["Status"] = new { type = "select", select = new { name = "EM ATENDIMENTO" } },
        };
        if (campanha is not null) props["Campanha"] = new { type = "select", select = new { name = campanha } };
        if (vendedorEmail is not null)
        {
            props["Vendedor"] = new { type = "people", people = new object[] { new { name = "Ana Vendedora", person = new { email = vendedorEmail } } } };
        }
        var json = JsonSerializer.Serialize(new { id = Guid.NewGuid().ToString(), properties = props });
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    private static async Task<(ApplicationDbContext Db, NotionSyncService Service, Guid Regional, Guid Ganho, Guid Placeholder, Dictionary<string, Guid> Etapas)> PrepararAsync(TestDbContextFactory factory)
    {
        var db = factory.CreateContext();
        var regional = new CrmRegional { Nome = "MG132" };
        db.CrmRegionais.Add(regional);
        var ganho = await factory.CriarEtapaAsync(db, "Ganho", 1, TipoEtapaPipeline.Ganho);
        var etapa = await factory.ObterOuCriarEtapaLeadAsync(db, "Em atendimento (Leads)", 1);
        var placeholder = await factory.CriarUsuarioAsync(db, "Placeholder");
        var service = new NotionSyncService(db, TestDbContextFactory.CreateUserManager(db), NullLogger<NotionSyncService>.Instance);
        return (db, service, regional.Id, ganho.Id, placeholder.Id, new Dictionary<string, Guid> { ["Em atendimento (Leads)"] = etapa.Id });
    }

    [Fact]
    public async Task CardSemNome_EntraComNomeProvisorio()
    {
        using var factory = new TestDbContextFactory();
        var (db, service, regional, ganho, placeholder, etapas) = await PrepararAsync(factory);
        await using var _ = db;

        await service.ProcessarPaginaAsync(Pagina(null, whatsapp: "(31) 99999-0000"), regional, "MG132", etapas, ganho, placeholder, false, CancellationToken.None);
        await service.ProcessarPaginaAsync(Pagina(null), regional, "MG132", etapas, ganho, placeholder, false, CancellationToken.None);

        var nomes = await db.CrmLeads.Select(l => l.NomeOuRazaoSocial).OrderBy(n => n).ToListAsync();
        Assert.Equal(["(31) 99999-0000", "Sem nome (Notion)"], nomes);
    }

    [Fact]
    public async Task EmailDeOutroLead_NaoImpedeOCardDeEntrar()
    {
        using var factory = new TestDbContextFactory();
        var (db, service, regional, ganho, placeholder, etapas) = await PrepararAsync(factory);
        await using var _ = db;
        // Card já ligado a um lead, mas com o e-mail de outro cliente do CRM (índice único).
        var pagina = Pagina("Maria", whatsapp: "31988887777", email: "a@b.com", campanha: new string('x', 300));
        db.CrmLeads.AddRange(
            new CrmLead { NomeOuRazaoSocial = "Outro", TipoPessoa = TipoPessoa.Fisica, Email = "a@b.com", EmailNormalizado = "a@b.com" },
            new CrmLead { NomeOuRazaoSocial = "Maria antiga", TipoPessoa = TipoPessoa.Fisica, NotionPageId = pagina.GetProperty("id").GetString() });
        await db.SaveChangesAsync();

        // Campanha maior que a coluna (120) também não derruba o card.
        await service.ProcessarPaginaAsync(pagina, regional, "MG132", etapas, ganho, placeholder, false, CancellationToken.None);

        var maria = await db.CrmLeads.SingleAsync(l => l.NomeOuRazaoSocial == "Maria");
        Assert.Null(maria.EmailNormalizado);
        Assert.Equal("31988887777", maria.TelefoneNormalizado);
        Assert.Equal(120, maria.Campanha!.Length);
    }

    [Fact]
    public async Task CardDeConsultorAtivo_FicaComEle()
    {
        using var factory = new TestDbContextFactory();
        var (db, service, regional, ganho, placeholder, etapas) = await PrepararAsync(factory);
        await using var _ = db;
        var ana = await factory.CriarUsuarioAsync(db, "Ana");

        await service.ProcessarPaginaAsync(Pagina("João", vendedorEmail: ana.Email), regional, "MG132", etapas, ganho, placeholder, false, CancellationToken.None);

        Assert.Equal(ana.Id, (await db.CrmLeads.SingleAsync()).ResponsavelId);
    }
}
