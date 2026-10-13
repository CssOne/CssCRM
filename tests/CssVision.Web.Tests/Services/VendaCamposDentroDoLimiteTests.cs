using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Texto maior que a coluna ou número fora do limite viram erro claro (400), não "Erro inesperado (500)" do banco.</summary>
public class VendaCamposDentroDoLimiteTests
{
    private static VeiculoUpsertRequest Veiculo(string? placa = "ABC1D23", string? descricao = "Fiat Uno", string? chassi = null) =>
        new(descricao, placa, 50000m, null, null, null, null, chassi);

    [Theory]
    [InlineData("ABC1D23 FIAT UNO 2015")]   // placa digitada com o modelo junto
    [InlineData("12345678901")]             // 11 caracteres
    public void PlacaMaiorQue10_EErroClaro(string placa)
    {
        var erro = Assert.Throws<CrmBusinessException>(() =>
            OpportunityService.ExigirCamposDaVendaDentroDoLimite(null, null, null, Veiculo(placa), null));

        Assert.Equal("campo_muito_longo", erro.Codigo);
        Assert.Contains("placa", erro.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ChassiDescricaoCpfEstadoECanalMuitoLongos_SaoRecusados()
    {
        Assert.Equal("campo_muito_longo", Assert.Throws<CrmBusinessException>(() =>
            OpportunityService.ExigirCamposDaVendaDentroDoLimite(null, null, null, Veiculo(chassi: new string('X', 31)), null)).Codigo);
        Assert.Equal("campo_muito_longo", Assert.Throws<CrmBusinessException>(() =>
            OpportunityService.ExigirCamposDaVendaDentroDoLimite(null, null, null, Veiculo(descricao: new string('D', 201)), null)).Codigo);
        Assert.Equal("campo_muito_longo", Assert.Throws<CrmBusinessException>(() =>
            OpportunityService.ExigirCamposDaVendaDentroDoLimite("123.456.789-09 / 98", null, null, null, null)).Codigo);
        Assert.Equal("campo_muito_longo", Assert.Throws<CrmBusinessException>(() =>
            OpportunityService.ExigirCamposDaVendaDentroDoLimite(null, "MG-BR", null, null, null)).Codigo);
        Assert.Equal("campo_muito_longo", Assert.Throws<CrmBusinessException>(() =>
            OpportunityService.ExigirCamposDaVendaDentroDoLimite(null, null, new string('C', 81), null, null)).Codigo);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(100.01)]
    [InlineData(1000)]
    public void PorcentagemForaDe0a100_EErroClaro(double porcentagem)
    {
        var erro = Assert.Throws<CrmBusinessException>(() =>
            OpportunityService.ExigirCamposDaVendaDentroDoLimite(null, null, null, null, (decimal)porcentagem));

        Assert.Equal("porcentagem_invalida", erro.Codigo);
    }

    [Fact]
    public void ValorGrandeDemais_EErroClaro()
    {
        var erro = Assert.Throws<CrmBusinessException>(() =>
            OpportunityService.ExigirCamposDaVendaDentroDoLimite(null, null, null, null, null, ("Mensalidade", 1_000_000_000_000m)));

        Assert.Equal("valor_invalido", erro.Codigo);
        Assert.Contains("Mensalidade", erro.Message);
    }

    [Fact]
    public void DadosNormais_PassamSemErro()
    {
        OpportunityService.ExigirCamposDaVendaDentroDoLimite("12345678909", "MG", "Pessoal", Veiculo("ABC1D23"), 23m,
            ("Mensalidade", 258.74m), ("Pagamento de adesão", 344.98m), ("Total", null));
        OpportunityService.ExigirCamposDaVendaDentroDoLimite(null, null, null, Veiculo("ABC-1D23"), 0m);
        OpportunityService.ExigirCamposDaVendaDentroDoLimite(null, null, null, Veiculo(new string('X', 10)), 100m);
    }
}
