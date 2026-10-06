using CssVision.Web.Services.Crm;
using Xunit;

namespace CssVision.Web.Tests.Services;

public class LeadServiceValorConversaoTests
{
    [Fact]
    public void EscolherValorConversao_DevePreferirOportunidade_QuandoTemValor()
    {
        Assert.Equal(500m, LeadService.EscolherValorConversao(500m, 300m));
        Assert.Equal(500m, LeadService.EscolherValorConversao(500m, null));
    }

    [Fact]
    public void EscolherValorConversao_DeveUsarValorDoLead_QuandoOportunidadeNaoTemValor()
    {
        Assert.Equal(300m, LeadService.EscolherValorConversao(null, 300m));
        Assert.Equal(300m, LeadService.EscolherValorConversao(0m, 300m));
    }

    [Fact]
    public void EscolherValorConversao_DeveRetornarNulo_QuandoNaoHaValor()
    {
        Assert.Null(LeadService.EscolherValorConversao(null, null));
        Assert.Null(LeadService.EscolherValorConversao(0m, 0m));
        Assert.Null(LeadService.EscolherValorConversao(null, 0m));
    }
}
