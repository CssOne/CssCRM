using CssVision.Web.Services.Crm;
using Xunit;

namespace CssVision.Web.Tests.Services;

public class DocumentValidationTests
{
    [Theory]
    [InlineData("529.982.247-25", true)]
    [InlineData("52998224725", true)]
    [InlineData("111.111.111-11", false)]
    [InlineData("123.456.789-00", false)]
    public void ValidarCpf_DeveValidarCorretamente(string cpf, bool esperado)
    {
        var normalizado = DocumentValidation.NormalizarDocumento(cpf, out var valido);
        Assert.Equal(esperado, valido);
        Assert.Equal(11, normalizado!.Length);
    }

    [Theory]
    [InlineData("11.444.777/0001-61", true)]
    [InlineData("11444777000161", true)]
    [InlineData("11.111.111/1111-11", false)]
    public void ValidarCnpj_DeveValidarCorretamente(string cnpj, bool esperado)
    {
        var normalizado = DocumentValidation.NormalizarDocumento(cnpj, out var valido);
        Assert.Equal(esperado, valido);
        Assert.Equal(14, normalizado!.Length);
    }

    [Theory]
    [InlineData("joao@teste.com", "joao@teste.com")]
    [InlineData("  Maria@Teste.COM  ", "maria@teste.com")]
    [InlineData("invalido", null)]
    public void NormalizarEmail_DeveNormalizarOuRejeitar(string entrada, string? esperado)
    {
        Assert.Equal(esperado, DocumentValidation.NormalizarEmail(entrada));
    }

    [Theory]
    [InlineData("(11) 98888-7777", "11988887777")]
    [InlineData("+55 11 98888-7777", "11988887777")]
    [InlineData("5511988887777", "11988887777")]
    public void NormalizarTelefone_DeveRemoverFormatacaoECodigoPais(string entrada, string esperado)
    {
        Assert.Equal(esperado, DocumentValidation.NormalizarTelefone(entrada));
    }

    [Fact]
    public void MascararDocumento_NaoDeveExporCpfCompleto()
    {
        var mascarado = DocumentValidation.MascararDocumento("52998224725");
        Assert.DoesNotContain("982.247", mascarado);
        Assert.Contains("***", mascarado);
    }
}
