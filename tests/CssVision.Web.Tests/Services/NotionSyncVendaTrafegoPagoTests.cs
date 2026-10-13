using System.Text.Json;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Services.Notion;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>O sync segue a mesma regra do CRM: Lead sem sinal de anúncio vira Indicação quando tem venda; com sinal (do card ou do lead), continua Lead.</summary>
public class NotionSyncVendaTrafegoPagoTests
{
    private static JsonElement Card(string? tag = null, string? utm = null, string? campanha = null, string? gclid = null, string? metaLeadId = null)
    {
        var props = new Dictionary<string, object>
        {
            ["Name"] = new { type = "title", title = new object[] { new { plain_text = "Cliente" } } },
            ["Status"] = new { type = "select", select = new { name = "VENDA CONCLUIDA" } },
            ["Tags"] = new { type = "select", select = tag is null ? null : new { name = tag } },
            ["UTM SOURCE"] = new { type = "rich_text", rich_text = utm is null ? Array.Empty<object>() : new object[] { new { plain_text = utm } } },
            ["CAMPANHA"] = new { type = "select", select = campanha is null ? null : new { name = campanha } },
            ["GCLID"] = new { type = "rich_text", rich_text = gclid is null ? Array.Empty<object>() : new object[] { new { plain_text = gclid } } },
            ["[META] Lead ID"] = new { type = "rich_text", rich_text = metaLeadId is null ? Array.Empty<object>() : new object[] { new { plain_text = metaLeadId } } },
        };
        return JsonDocument.Parse(JsonSerializer.Serialize(new { id = Guid.NewGuid().ToString(), properties = props })).RootElement.Clone();
    }

    private static CrmLead LeadDoNotion(string tipo = "Lead") => new() { TipoIndicacao = tipo, ConsentimentoOrigem = OrigemLead.MarcadorSincronizacaoNotion };

    [Fact]
    public void LeadSemSinalDeAnuncio_ComVenda_ViraIndicacao()
    {
        var lead = LeadDoNotion();

        NotionSyncService.AplicarRegraDeVendaDeTrafegoPago(lead, Card(), temVenda: true);

        Assert.Equal("Indicação", lead.TipoIndicacao);
    }

    [Theory]
    [InlineData("tag")]
    [InlineData("utm")]
    [InlineData("campanha")]
    [InlineData("gclid")]
    [InlineData("meta")]
    public void SinalDeAnuncioNoCard_MantemComoLead(string sinal)
    {
        var lead = LeadDoNotion();
        var card = sinal switch
        {
            "tag" => Card(tag: "Facebook ADS"),
            "utm" => Card(utm: "google"),
            "campanha" => Card(campanha: "Lookalike"),
            "gclid" => Card(gclid: "abc"),
            _ => Card(metaLeadId: "123"),
        };

        NotionSyncService.AplicarRegraDeVendaDeTrafegoPago(lead, card, temVenda: true);

        Assert.Equal("Lead", lead.TipoIndicacao);
    }

    [Fact]
    public void SinalGuardadoNoLead_MantemComoLead_MesmoComCardSemSinal()
    {
        var lead = LeadDoNotion();
        lead.TagDeAnuncioNotion = true; // marcado pela carga única / por um sync anterior

        NotionSyncService.AplicarRegraDeVendaDeTrafegoPago(lead, Card(), temVenda: true);

        Assert.Equal("Lead", lead.TipoIndicacao);
    }

    [Fact]
    public void SemVenda_NaoMudaAEtiqueta()
    {
        var lead = LeadDoNotion();

        NotionSyncService.AplicarRegraDeVendaDeTrafegoPago(lead, Card(), temVenda: false);

        Assert.Equal("Lead", lead.TipoIndicacao);
    }

    [Theory]
    [InlineData("Pessoal")]
    [InlineData("Indicação")]
    [InlineData("Indicação Lead")]
    public void OutrasEtiquetas_NaoSaoTocadas(string tipo)
    {
        var lead = LeadDoNotion(tipo);

        NotionSyncService.AplicarRegraDeVendaDeTrafegoPago(lead, Card(), temVenda: true);

        Assert.Equal(tipo, lead.TipoIndicacao);
    }
}
