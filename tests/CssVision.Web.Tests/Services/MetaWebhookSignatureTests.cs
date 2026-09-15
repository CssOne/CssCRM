using System.Security.Cryptography;
using System.Text;
using CssVision.Web.Services.Marketing;
using Xunit;

namespace CssVision.Web.Tests.Services;

public class MetaWebhookSignatureTests
{
    private const string AppSecret = "segredo-de-teste";

    [Fact]
    public void IsValid_DeveAceitarAssinaturaCorreta()
    {
        var body = "{\"object\":\"page\"}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(AppSecret));
        var hash = Convert.ToHexStringLower(hmac.ComputeHash(Encoding.UTF8.GetBytes(body)));

        Assert.True(MetaWebhookSignature.IsValid(body, $"sha256={hash}", AppSecret));
    }

    [Fact]
    public void IsValid_DeveRejeitarAssinaturaIncorreta()
    {
        Assert.False(MetaWebhookSignature.IsValid("{}", "sha256=abc123", AppSecret));
    }

    [Fact]
    public void IsValid_DevePularValidacaoQuandoHeaderAusente()
    {
        Assert.True(MetaWebhookSignature.IsValid("{}", null, AppSecret));
    }

    [Fact]
    public void IsValid_DeveRejeitarPrefixoInvalido()
    {
        Assert.False(MetaWebhookSignature.IsValid("{}", "md5=abc123", AppSecret));
    }

    [Fact]
    public void IsValid_DeveRejeitarHexInvalidoSemLancarExcecao()
    {
        Assert.False(MetaWebhookSignature.IsValid("{}", "sha256=xyz-nao-e-hex", AppSecret));
    }
}
