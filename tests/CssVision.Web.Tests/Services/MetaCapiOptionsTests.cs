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
}
