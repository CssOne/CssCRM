using CssVision.Web.Services.Marketing;
using Xunit;

namespace CssVision.Web.Tests.Services;

public class MetaCapiOptionsTests
{
    private static MetaCapiOptions Opcoes() => new()
    {
        PixelId = "padrao",
        AccessToken = "token-padrao",
        PixelsPorOQue = [new MetaCapiPixelPorOQue { OQue = "AGV REGIONAL 134", PixelId = "regional", AccessToken = "token-regional" }],
    };

    [Theory]
    [InlineData("AGV REGIONAL 134")]
    [InlineData("  agv regional 134 ")]
    public void ParaLead_DeveUsarPixelDedicado_QuandoOQueBate(string oQue)
    {
        var efetivo = Opcoes().ParaLead(oQue);

        Assert.Equal("regional", efetivo.PixelId);
        Assert.Equal("token-regional", efetivo.AccessToken);
    }

    [Theory]
    [InlineData("AGV")]
    [InlineData("")]
    [InlineData(null)]
    public void ParaLead_DeveUsarPixelPadrao_QuandoNaoHaPixelDedicado(string? oQue)
    {
        var efetivo = Opcoes().ParaLead(oQue);

        Assert.Equal("padrao", efetivo.PixelId);
        Assert.Equal("token-padrao", efetivo.AccessToken);
    }

    [Fact]
    public void ParaLead_DeveUsarPixelPorRegional_QuandoEntradaSoTemRegional()
    {
        var opcoes = Opcoes();
        opcoes.PixelsPorOQue.Add(new MetaCapiPixelPorOQue { Regional = "MG132", PixelId = "externo", AccessToken = "token-externo" });

        Assert.Equal("externo", opcoes.ParaLead("AGV", "mg132").PixelId);
        Assert.Equal("padrao", opcoes.ParaLead("AGV", "MG999").PixelId);
        Assert.Equal("padrao", opcoes.ParaLead("AGV", null).PixelId);
    }

    [Fact]
    public void ParaLead_EntradaComOQueERegional_ExigeOsDois()
    {
        var opcoes = new MetaCapiOptions
        {
            PixelId = "padrao",
            AccessToken = "t",
            PixelsPorOQue = [new MetaCapiPixelPorOQue { OQue = "AGV TRUCK", Regional = "MG132", PixelId = "caminhao132", AccessToken = "t2" }],
        };

        Assert.Equal("caminhao132", opcoes.ParaLead("AGV TRUCK", "MG132").PixelId);
        Assert.Equal("padrao", opcoes.ParaLead("AGV TRUCK", "MG134").PixelId);
    }

    [Fact]
    public void ParaLead_EntradaSemCriterio_Ignorada()
    {
        var opcoes = new MetaCapiOptions
        {
            PixelId = "padrao",
            AccessToken = "t",
            PixelsPorOQue = [new MetaCapiPixelPorOQue { PixelId = "vazio", AccessToken = "t2" }],
        };

        Assert.Equal("padrao", opcoes.ParaLead("AGV", "MG132").PixelId);
    }
}
