using System.Text.Json;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Services.Notion;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Sinal de anúncio (tráfego pago): tag Facebook/Google, UTM, campanha, ID do Meta ou GCLID deixam o card como Lead — a menos que o tipo/indicação estejam preenchidos.</summary>
public class SinalDeAnuncioTests
{
    private static JsonElement Card(string? tag = null, string? utm = null, string? campanha = null, string? tipo = null, bool indicacaoSim = false, string? indicacaoValor = null)
    {
        var props = new Dictionary<string, object>
        {
            ["Name"] = new { type = "title", title = new object[] { new { plain_text = "Cliente" } } },
            ["Status"] = new { type = "select", select = new { name = "VENDA CONCLUIDA" } },
            ["Tags"] = new { type = "select", select = tag is null ? null : new { name = tag } },
            ["UTM SOURCE"] = new { type = "rich_text", rich_text = utm is null ? Array.Empty<object>() : new object[] { new { plain_text = utm } } },
            ["CAMPANHA"] = new { type = "select", select = campanha is null ? null : new { name = campanha } },
            ["Indicação?"] = new { type = "select", select = indicacaoValor is not null ? new { name = indicacaoValor } : indicacaoSim ? new { name = "SIM" } : null },
            ["Tpo de Indicação? "] = new { type = "select", select = tipo is null ? null : new { name = tipo } },
        };
        return JsonDocument.Parse(JsonSerializer.Serialize(new { id = Guid.NewGuid().ToString(), properties = props })).RootElement.Clone();
    }

    [Theory]
    [InlineData("Facebook ADS", true)]
    [InlineData("facebook ads", true)]
    [InlineData("Google ads", true)]
    [InlineData(" Google ads ", true)]
    [InlineData("Orgânico", false)]
    [InlineData("CAMINHÃO", false)]
    [InlineData("BRASIL", false)]
    [InlineData(null, false)]
    public void TagDeAnuncio(string? tag, bool esperado) => Assert.Equal(esperado, OrigemLead.EhTagDeAnuncio(tag));

    [Theory]
    [InlineData("tag")]
    [InlineData("utm")]
    [InlineData("campanha")]
    public void CardSemTipoComSinalDeAnuncio_EhLead_MesmoSemProduto(string sinal)
    {
        var card = sinal switch { "tag" => Card(tag: "Facebook ADS"), "utm" => Card(utm: "google"), _ => Card(campanha: "Lookalike") };

        // Sem "O que" a regra antiga o mandava para Indicação; com sinal de anúncio ele é Lead.
        Assert.Equal("Lead", NotionSyncService.TipoIndicacaoDoCard(card, null));
    }

    [Fact]
    public void CardSemSinalNemTipo_ContinuaPelaRegraAntiga() =>
        Assert.Equal("Indicação", NotionSyncService.TipoIndicacaoDoCard(Card(), null));

    [Theory]
    [InlineData("PESSOAL", false, "Indicação pessoal")]
    [InlineData(null, true, "Indicação")]
    public void TipoOuIndicacaoPreenchidos_VencemOSinalDeAnuncio(string? tipo, bool indicacaoSim, string esperado) =>
        Assert.Equal(esperado, NotionSyncService.TipoIndicacaoDoCard(Card(tag: "Facebook ADS", tipo: tipo, indicacaoSim: indicacaoSim), "AGV"));

    [Theory]
    [InlineData("SIM")]
    [InlineData("INDICAÇÃO")]
    [InlineData("Indicação")]
    public void LeadQueFechouComoIndicacao_ValeComSimOuIndicacao(string valor) =>
        Assert.Equal("Indicação Lead", NotionSyncService.TipoIndicacaoDoCard(Card(tipo: "LEAD", indicacaoValor: valor), "AGV"));

    [Fact]
    public void LeadSemNenhumSinal_NaoEhDeTrafegoPago() =>
        Assert.False(OrigemLead.DeTrafegoPago(new CrmLead { ConsentimentoOrigem = OrigemLead.MarcadorSincronizacaoNotion }));

    [Theory]
    [InlineData("meta")]
    [InlineData("gclid")]
    [InlineData("utm")]
    [InlineData("tag")]
    [InlineData("site")]
    [InlineData("campanha-notion")]
    public void CadaSinalDeAnuncio_BastaParaSerDeTrafegoPago(string sinal)
    {
        var lead = new CrmLead { ConsentimentoOrigem = OrigemLead.MarcadorSincronizacaoNotion };
        switch (sinal)
        {
            case "meta": lead.MetaLeadId = "123"; break;
            case "gclid": lead.Gclid = "abc"; break;
            case "utm": lead.UtmSource = "facebook"; break;
            case "tag": lead.TagDeAnuncioNotion = true; break;
            case "site": lead.ConsentimentoOrigem = OrigemLead.MarcadorFormularioSite; break;
            default: lead.Campanha = "Lookalike"; break;
        }

        Assert.True(OrigemLead.DeTrafegoPago(lead));
    }

    [Fact]
    public async Task ConsultaDoBanco_UsaAMesmaRegra_ComTagEUtm()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var notion = OrigemLead.MarcadorSincronizacaoNotion;
        db.CrmLeads.AddRange(
            new CrmLead { NomeOuRazaoSocial = "tag", TipoPessoa = TipoPessoa.Fisica, ConsentimentoOrigem = notion, TagDeAnuncioNotion = true },
            new CrmLead { NomeOuRazaoSocial = "utm", TipoPessoa = TipoPessoa.Fisica, ConsentimentoOrigem = notion, UtmSource = "facebook" },
            new CrmLead { NomeOuRazaoSocial = "meta", TipoPessoa = TipoPessoa.Fisica, ConsentimentoOrigem = notion, MetaLeadId = "1" },
            new CrmLead { NomeOuRazaoSocial = "nada", TipoPessoa = TipoPessoa.Fisica, ConsentimentoOrigem = notion });
        await db.SaveChangesAsync();

        var nomes = await db.CrmLeads.AsNoTracking().Where(OrigemLead.DeTrafegoPagoInclusiveNotion).Select(l => l.NomeOuRazaoSocial).OrderBy(n => n).ToListAsync();

        Assert.Equal(["meta", "tag", "utm"], nomes);
    }

    [Fact]
    public void CampanhaSoContaParaCardsDoNotion() =>
        Assert.False(OrigemLead.DeTrafegoPago(new CrmLead { ConsentimentoOrigem = "Cadastro manual", Campanha = "Lookalike" }));

    [Fact]
    public async Task CargaDaTag_MarcaSoOsLeadsLigadosAosCardsComATag()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var vendedor = await factory.CriarUsuarioAsync(db, "Vendedor");
        CrmLead Novo(string nome, string? pagina) => new() { NomeOuRazaoSocial = nome, TipoPessoa = TipoPessoa.Fisica, ResponsavelId = vendedor.Id, NotionPageId = pagina };
        db.CrmLeads.AddRange(Novo("Com tag", "pagina-1"), Novo("Outro com tag", "pagina-2"), Novo("Sem tag", "pagina-3"), Novo("Sem Notion", null));
        await db.SaveChangesAsync();

        var marcados = await PreencherTagDeAnuncioBackgroundService.MarcarAsync(db, ["pagina-1", "pagina-2", "pagina-inexistente"], CancellationToken.None);

        Assert.Equal(2, marcados);
        db.ChangeTracker.Clear();
        var porNome = await db.CrmLeads.AsNoTracking().ToDictionaryAsync(l => l.NomeOuRazaoSocial, l => l.TagDeAnuncioNotion);
        Assert.True(porNome["Com tag"]);
        Assert.True(porNome["Outro com tag"]);
        Assert.False(porNome["Sem tag"]);
        Assert.False(porNome["Sem Notion"]);
        Assert.Equal(0, await PreencherTagDeAnuncioBackgroundService.MarcarAsync(db, ["pagina-1", "pagina-2"], CancellationToken.None));
    }
}
