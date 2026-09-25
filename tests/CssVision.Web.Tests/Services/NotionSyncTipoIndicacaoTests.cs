using System.Text.Json;
using CssVision.Web.Authorization;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Services.Notion;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Tipo de indicação e "Indicação?" vindos dos campos do card do Notion.</summary>
public class NotionSyncTipoIndicacaoTests
{
    private static JsonElement Pagina(string? tipo, bool indicacaoSim, string status = "VENDA CONCLUIDA", string? oQue = "AGV",
        string? vendedorEmail = null, string? whatsapp = null, string nomePropTipo = "Tpo de Indicação? ")
    {
        var props = new Dictionary<string, object>
        {
            ["Name"] = new { type = "title", title = new object[] { new { plain_text = "Davi" } } },
            ["WhatsApp"] = new { type = "rich_text", rich_text = whatsapp is null ? Array.Empty<object>() : new object[] { new { plain_text = whatsapp } } },
            ["Status"] = new { type = "select", select = new { name = status } },
            ["O que"] = new { type = "select", select = oQue is null ? null : new { name = oQue } },
            ["Indicação?"] = new { type = "select", select = indicacaoSim ? new { name = "SIM" } : null },
            // Mesmo nome de propriedade (número) sem "?": não pode ser confundido com o select.
            ["Indicação"] = new { type = "number", number = 0 },
            [nomePropTipo] = new { type = "select", select = tipo is null ? null : new { name = tipo } },
        };
        if (vendedorEmail is not null)
        {
            props["Vendedor"] = new { type = "people", people = new object[] { new { name = "V", person = new { email = vendedorEmail } } } };
        }
        var json = JsonSerializer.Serialize(new { id = Guid.NewGuid().ToString(), properties = props });
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    [Theory]
    [InlineData("PESSOAL", true, "AGV", "Pessoal")]
    [InlineData("LEAD", true, "AGV", "Lead")]
    [InlineData("CONTEMPLANDO SONHOS", false, "AGV", "Contemplando Sonhos")]
    [InlineData(null, true, "AGV", "Indicação")]
    [InlineData(null, false, "AGV", "Lead")]       // sem os campos: regra antiga pelo "O que"
    [InlineData(null, false, null, "Indicação")]
    public void TipoIndicacaoDoCard(string? tipo, bool indicacaoSim, string? oQue, string esperado) =>
        Assert.Equal(esperado, NotionSyncService.TipoIndicacaoDoCard(Pagina(tipo, indicacaoSim, oQue: oQue), oQue));

    [Fact]
    public void TipoIndicacao_AchaOCampoPeloNomeAproximado() =>
        Assert.Equal("Pessoal", NotionSyncService.TipoIndicacaoDoCard(Pagina("PESSOAL", false, nomePropTipo: "Tipo de indicação?"), "AGV"));

    private static async Task<(ApplicationDbContext Db, NotionSyncService Service, Guid Regional, Guid Ganho, Guid Placeholder, Dictionary<string, Guid> Etapas, Guid Ana)> PrepararAsync(TestDbContextFactory factory)
    {
        var db = factory.CreateContext();
        var regional = new CrmRegional { Nome = "MG132" };
        db.CrmRegionais.Add(regional);
        var ganho = await factory.CriarEtapaAsync(db, "Ganho", 1, TipoEtapaPipeline.Ganho);
        var etapas = new Dictionary<string, Guid>();
        var ordem = 1;
        foreach (var nome in new[] { "Venda concluída (Leads)", "Venda concluída (Indicação)", "Em atendimento (Leads)", "Em atendimento (Indicação)" })
        {
            etapas[nome] = (await factory.ObterOuCriarEtapaLeadAsync(db, nome, ordem++)).Id;
        }
        var placeholder = await factory.CriarUsuarioAsync(db, "Placeholder");
        var ana = await factory.CriarUsuarioAsync(db, "Ana");
        await factory.AtribuirPapelAsync(db, ana, Roles.Comercial);
        var service = new NotionSyncService(db, TestDbContextFactory.CreateUserManager(db), NullLogger<NotionSyncService>.Instance);
        return (db, service, regional.Id, ganho.Id, placeholder.Id, etapas, ana.Id);
    }

    [Fact]
    public async Task VendaIndicacaoPessoal_LeadFicaPessoalNaColunaDeIndicacao_EOportunidadeMarcadaComoIndicacao()
    {
        using var factory = new TestDbContextFactory();
        var (db, service, regional, ganho, placeholder, etapas, ana) = await PrepararAsync(factory);
        await using var _ = db;
        var anaEmail = (await db.Users.FindAsync(ana))!.Email;

        await service.ProcessarPaginaAsync(Pagina("PESSOAL", true, vendedorEmail: anaEmail), regional, "MG132", etapas, ganho, placeholder, false, CancellationToken.None);

        var lead = await db.CrmLeads.SingleAsync();
        Assert.Equal("Pessoal", lead.TipoIndicacao);
        Assert.True(lead.CriadoManualmente);
        Assert.Equal(etapas["Venda concluída (Indicação)"], lead.EtapaId);
        var oportunidade = await db.CrmOpportunities.SingleAsync();
        Assert.True(oportunidade.Indicacao);
        Assert.Equal("Pessoal", oportunidade.TipoIndicacao);
    }

    [Fact]
    public async Task Revisao_CardDeOutroVendedor_AtualizaOTipo_EMudaParaAColunaPar()
    {
        using var factory = new TestDbContextFactory();
        var (db, service, regional, ganho, placeholder, etapas, _) = await PrepararAsync(factory);
        await using var __ = db;
        // Lead importado antes, classificado como Lead pelo "O que", na coluna de Leads.
        db.CrmLeads.Add(new CrmLead
        {
            NomeOuRazaoSocial = "Davi", TipoPessoa = TipoPessoa.Fisica, TelefoneNormalizado = "31992142811",
            TipoIndicacao = "Lead", CriadoManualmente = false, EtapaId = etapas["Venda concluída (Leads)"],
            NotionStatus = "VENDA CONCLUIDA", ConsentimentoOrigem = OrigemLead.MarcadorMigracaoNotion, ResponsavelId = placeholder,
        });
        await db.SaveChangesAsync();

        await service.DevolverAoVendedorDoCardAsync(
            Pagina("PESSOAL", true, whatsapp: "(31) 99214-2811"), regional, "MG132", placeholder, CancellationToken.None, etapas);

        var lead = await db.CrmLeads.SingleAsync();
        Assert.Equal("Pessoal", lead.TipoIndicacao);
        Assert.Equal(etapas["Venda concluída (Indicação)"], lead.EtapaId);
    }
}
