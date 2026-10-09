using System.Text.Json;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Notion;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Valores da venda que vêm do card do Notion: mensalidade, adesão, porcentagem, indicação, estado, rastreador e vistoria.</summary>
public class NotionSyncValoresDaVendaTests
{
    private static JsonElement Pagina(Dictionary<string, object> numeros, string? estadoSelect = null, string? estadoTexto = null)
    {
        var props = new Dictionary<string, object>();
        foreach (var (nome, valor) in numeros) props[nome] = new { type = "number", number = valor };
        if (estadoSelect is not null) props["Estado"] = new { type = "select", select = new { name = estadoSelect } };
        if (estadoTexto is not null) props["ESTADO"] = new { type = "rich_text", rich_text = new object[] { new { plain_text = estadoTexto } } };
        return JsonDocument.Parse(JsonSerializer.Serialize(new { id = Guid.NewGuid().ToString(), properties = props })).RootElement.Clone();
    }

    [Fact]
    public void GravaTodosOsValoresDoCard()
    {
        var venda = new CrmOpportunity { Veiculo = new CrmVeiculo() };
        var card = Pagina(new()
        {
            ["Mensalidade"] = 299.82, ["Adesão"] = 250, ["Porcentagem"] = 0.25, ["Indicação"] = 50, ["Rastreador"] = 100, ["Vistoriador"] = 30,
        }, estadoSelect: "MG");

        NotionSyncService.AplicarValoresDaVenda(venda, card);

        Assert.Equal(299.82m, venda.Mensalidade);
        Assert.Equal(250m, venda.PagamentoAdesao);
        Assert.Equal(25m, venda.Porcentagem); // o Notion guarda fração; o CRM, pontos
        Assert.Equal(50m, venda.ValorIndicacao);
        Assert.Equal("MG", venda.Estado);
        Assert.Equal(100m, venda.Veiculo!.Rastreador);
        Assert.Equal(30m, venda.Veiculo.ValorVistoria);
    }

    [Fact]
    public void CampoVazioNoNotion_NaoApagaOQueJaEstaNoCrm()
    {
        var venda = new CrmOpportunity { Mensalidade = 100m, PagamentoAdesao = 80m, Estado = "SP", Veiculo = new CrmVeiculo { Rastreador = 40m } };

        NotionSyncService.AplicarValoresDaVenda(venda, Pagina(new()));

        Assert.Equal((100m, 80m, "SP", 40m), (venda.Mensalidade, venda.PagamentoAdesao, venda.Estado, venda.Veiculo!.Rastreador));
    }

    [Theory]
    [InlineData("rj", null, "RJ")]
    [InlineData(null, "go", "GO")]
    [InlineData("Minas", null, null)] // não é sigla de 2 letras
    public void Estado_VemDoSelectOuDoTexto_ESoComDuasLetras(string? select, string? texto, string? esperado)
    {
        var venda = new CrmOpportunity();

        NotionSyncService.AplicarValoresDaVenda(venda, Pagina(new(), select, texto));

        Assert.Equal(esperado, venda.Estado);
    }
}
