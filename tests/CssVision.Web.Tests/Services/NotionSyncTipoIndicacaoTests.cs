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
        string? vendedorEmail = null, string? whatsapp = null, string nomePropTipo = "Tpo de Indicação? ",
        string? metaLeadId = null, string? gclid = null)
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
        if (metaLeadId is not null)
        {
            props["[META] Lead ID"] = new { type = "rich_text", rich_text = new object[] { new { plain_text = metaLeadId } } };
        }
        if (gclid is not null)
        {
            props["GCLID"] = new { type = "rich_text", rich_text = new object[] { new { plain_text = gclid } } };
        }
        if (vendedorEmail is not null)
        {
            props["Vendedor"] = new { type = "people", people = new object[] { new { name = "V", person = new { email = vendedorEmail } } } };
        }
        var json = JsonSerializer.Serialize(new { id = Guid.NewGuid().ToString(), properties = props });
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    [Theory]
    [InlineData("PESSOAL", true, "AGV", "Pessoal")]
    [InlineData("LEAD", true, "AGV", "Indicação Lead")] // venda de lead que fechou por indicação
    [InlineData("LEAD", false, "AGV", "Lead")]
    [InlineData("CONTEMPLANDO SONHOS", false, "AGV", "Contemplando Sonhos")]
    [InlineData("PARCERIA", true, "AGV", "Parceria")]
    [InlineData("AÇÃO EXTERNA", true, "AGV", "Ação Externa")]
    [InlineData("ACAO EXTERNA", true, "AGV", "Ação Externa")]
    [InlineData("css", true, "AGV", "CSS")]
    [InlineData(null, true, "AGV", "Indicação")]
    [InlineData(null, false, "AGV", "Lead")]       // sem os campos: regra antiga pelo "O que"
    [InlineData(null, false, null, "Indicação")]
    public void TipoIndicacaoDoCard(string? tipo, bool indicacaoSim, string? oQue, string esperado) =>
        Assert.Equal(esperado, NotionSyncService.TipoIndicacaoDoCard(Pagina(tipo, indicacaoSim, oQue: oQue), oQue));

    [Fact]
    public void CardLeadComIndicacaoSim_ForaDeVendaConcluida_SegueLead() =>
        Assert.Equal("Lead", NotionSyncService.TipoIndicacaoDoCard(Pagina("LEAD", true, status: "EM ATENDIMENTO"), "AGV"));

    [Theory]
    [InlineData("Em atendimento (Indicação)", "Lead", false, "Em atendimento (Leads)")]
    [InlineData("Em atendimento (Leads)", "Pessoal", true, "Em atendimento (Indicação)")]
    [InlineData("Venda concluída (Indicação)", "Lead", false, "Venda concluída (Leads)")]
    [InlineData("Venda concluída (Leads)", "Indicação Lead", true, "Venda concluída (Indicação)")]
    [InlineData("Em atendimento (Leads)", "Lead", false, "Em atendimento (Leads)")]
    [InlineData("Cotação", "Lead", false, "Cotação")]
    public void ColunaSeguraAEtiqueta_MesmoSemMudancaDeEtiqueta(string colunaAtual, string tipo, bool manual, string esperada)
    {
        var etapas = new[] { "Em atendimento (Leads)", "Em atendimento (Indicação)", "Venda concluída (Leads)", "Venda concluída (Indicação)", "Cotação" }
            .ToDictionary(n => n, _ => Guid.NewGuid());
        var lead = new CrmLead { TipoIndicacao = tipo, CriadoManualmente = manual, EtapaId = etapas[colunaAtual] };

        NotionSyncService.TrocarColunaLeadIndicacao(lead, etapas);

        Assert.Equal(etapas[esperada], lead.EtapaId);
    }

    [Fact]
    public void TipoIndicacao_AchaOCampoPeloNomeAproximado() =>
        Assert.Equal("Pessoal", NotionSyncService.TipoIndicacaoDoCard(Pagina("PESSOAL", false, nomePropTipo: "Tipo de indicação?"), "AGV"));

    [Theory]
    [InlineData("AGV ELETRICO")]   // grafia do Notion, sem acento
    [InlineData("AGV ELÉTRICO")]
    [InlineData("agv elétrico")]
    [InlineData(" AGV Elétrico ")]
    [InlineData("AGV")]
    [InlineData("loovi")]
    public void OQue_ConhecidoEhLead_SemDependerDeAcentoOuMaiuscula(string oQue) =>
        Assert.Equal("Lead", NotionSyncService.TipoIndicacaoDoCard(Pagina(null, false, oQue: oQue), oQue));

    [Theory]
    [InlineData(null, "123456789", null, null, "Lead")]                  // sem "O que", mas com ID de lead da Meta
    [InlineData(null, null, "Cj0KCQ", null, "Lead")]                    // sem "O que", mas com GCLID
    [InlineData("OUTRO PRODUTO", "123456789", null, null, "Lead")]       // "O que" desconhecido com anúncio
    [InlineData(null, "123456789", null, "PESSOAL", "Pessoal")]           // tipo explícito do Notion vale mais que o anúncio
    [InlineData(null, "123456789", null, "LEAD", "Lead")]
    [InlineData("OUTRO PRODUTO", null, null, null, "Indicação")]          // sem anúncio, sem "O que" conhecido: continua indicação
    public void CardComSinalDeAnuncio_NuncaViraIndicacaoPorPadrao(string? oQue, string? metaLeadId, string? gclid, string? tipo, string esperado) =>
        Assert.Equal(esperado, NotionSyncService.TipoIndicacaoDoCard(Pagina(tipo, false, oQue: oQue, metaLeadId: metaLeadId, gclid: gclid), oQue));

    [Fact]
    public void IndicacaoMarcadaNoNotion_ValeMaisQueOAnuncio() =>
        Assert.Equal("Indicação", NotionSyncService.TipoIndicacaoDoCard(Pagina(null, true, oQue: "AGV ELETRICO", metaLeadId: "123456789"), "AGV ELETRICO"));

    [Fact]
    public async Task CardDeAnuncioEmAtendimento_AGVEletricoSemAcento_FicaNaColunaDeLeads()
    {
        using var factory = new TestDbContextFactory();
        var (db, service, regional, ganho, placeholder, etapas, ana) = await PrepararAsync(factory);
        await using var _ = db;
        var anaEmail = (await db.Users.FindAsync(ana))!.Email;

        await service.ProcessarPaginaAsync(
            Pagina(null, false, status: "EM ATENDIMENTO", oQue: "AGV ELETRICO", vendedorEmail: anaEmail, metaLeadId: "987654321"),
            regional, "MG132", etapas, ganho, placeholder, false, CancellationToken.None);

        var lead = await db.CrmLeads.SingleAsync();
        Assert.Equal("Lead", lead.TipoIndicacao);
        Assert.False(lead.CriadoManualmente);
        Assert.Equal(etapas["Em atendimento (Leads)"], lead.EtapaId);
    }

    [Fact]
    public async Task CardDeAnuncioSemOQue_FicaNaColunaDeLeads()
    {
        using var factory = new TestDbContextFactory();
        var (db, service, regional, ganho, placeholder, etapas, ana) = await PrepararAsync(factory);
        await using var _ = db;
        var anaEmail = (await db.Users.FindAsync(ana))!.Email;

        await service.ProcessarPaginaAsync(
            Pagina(null, false, status: "EM ATENDIMENTO", oQue: null, vendedorEmail: anaEmail, gclid: "Cj0KCQ"),
            regional, "MG132", etapas, ganho, placeholder, false, CancellationToken.None);

        var lead = await db.CrmLeads.SingleAsync();
        Assert.Equal("Lead", lead.TipoIndicacao);
        Assert.Equal(etapas["Em atendimento (Leads)"], lead.EtapaId);
    }

    [Fact]
    public async Task Revisao_Anuncio_TiraOLeadDaColunaDeIndicacaoSemEtiquetaExplicita()
    {
        using var factory = new TestDbContextFactory();
        var (db, service, regional, _, placeholder, etapas, _) = await PrepararAsync(factory);
        await using var __ = db;
        // Como estava em produção: "AGV ELETRICO" classificado como Indicação, na coluna de Indicação.
        db.CrmLeads.Add(new CrmLead
        {
            NomeOuRazaoSocial = "Davi", TipoPessoa = TipoPessoa.Fisica, TelefoneNormalizado = "31992142811",
            TipoIndicacao = "Indicação", CriadoManualmente = true, EtapaId = etapas["Em atendimento (Indicação)"],
            NotionStatus = "EM ATENDIMENTO", ConsentimentoOrigem = OrigemLead.MarcadorSincronizacaoNotion, ResponsavelId = placeholder,
        });
        await db.SaveChangesAsync();

        await service.DevolverAoVendedorDoCardAsync(
            Pagina(null, false, status: "EM ATENDIMENTO", oQue: "AGV ELETRICO", whatsapp: "(31) 99214-2811", metaLeadId: "987654321"),
            regional, "MG132", placeholder, CancellationToken.None, etapas);

        var lead = await db.CrmLeads.SingleAsync();
        Assert.Equal("Lead", lead.TipoIndicacao);
        Assert.Equal(etapas["Em atendimento (Leads)"], lead.EtapaId);
    }

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

/// <summary>Parceria, Ação Externa e CSS são tipos de indicação: viram etiqueta no cartão e saem do tipo "Lead".</summary>
public class NovosTiposDeIndicacaoTests
{
    [Theory]
    [InlineData("Parceria")]
    [InlineData("Ação Externa")]
    [InlineData("CSS")]
    public void NovoTipo_ContaComoIndicacao_NaoComoLead(string tipo)
    {
        Assert.True(NotionEtapaLead.EhIndicacao(criadoManualmente: false, tipoIndicacao: tipo));
        Assert.False(TipoIndicacaoLead.EhLead(tipo));
        // O tipo mantém a grafia escolhida (a etiqueta do cartão mostra exatamente este texto).
        Assert.Equal(tipo, NotionSyncService.NormalizarTipoIndicacao(tipo.ToUpperInvariant()));
    }
}
