using CssVision.Web.Services.Crm;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>A máscara do documento na lista de leads nunca pode estourar (derrubava a página inteira com erro 500).</summary>
public class MascararDocumentoTests
{
    [Theory]
    [InlineData("12345678909", "123.***.**89-09")]
    [InlineData("12345678000195", "12.***.**8/0001-95")]
    public void CpfECnpj_MantemOFormatoDeSempre(string documento, string esperado) =>
        Assert.Equal(esperado, DocumentValidation.MascararDocumento(documento));

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("1", "*")]
    [InlineData("100", "***")]
    [InlineData("1003", "****")]
    [InlineData("100309", "10**09")]
    [InlineData("1234567890", "12******90")]
    [InlineData("1234567890123", "12*********23")]
    public void DocumentoDeOutroTamanho_NaoEstoura(string? documento, string esperado) =>
        Assert.Equal(esperado, DocumentValidation.MascararDocumento(documento));
}
