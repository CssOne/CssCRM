using CssVision.Web.Services.Marketing;
using Xunit;

namespace CssVision.Web.Tests.Services;

public class MetaFieldMappingTests
{
    [Theory]
    [InlineData("Número de Telefone", "numero_de_telefone")]
    [InlineData("E-mail", "e_mail")]
    [InlineData("full_name", "full_name")]
    public void NormalizeKey_DeveRemoverAcentosEEspacos(string entrada, string esperado)
    {
        Assert.Equal(esperado, MetaFieldMapping.NormalizeKey(entrada));
    }

    [Fact]
    public void GetMappedValue_DeveEncontrarPeloAliasCorreto()
    {
        var fields = new Dictionary<string, string> { ["whatsapp"] = "11988887777" };
        Assert.Equal("11988887777", MetaFieldMapping.GetMappedValue(fields, "phone_number"));
    }

    [Fact]
    public void GetMappedValue_DeveRetornarNuloQuandoNaoEncontrado()
    {
        var fields = new Dictionary<string, string> { ["outro_campo"] = "valor" };
        Assert.Null(MetaFieldMapping.GetMappedValue(fields, "email"));
    }

    [Fact]
    public void ResolveName_DeveUsarFullNameQuandoPresente()
    {
        var fields = new Dictionary<string, string> { ["full_name"] = "João Silva" };
        Assert.Equal("João Silva", MetaFieldMapping.ResolveName(fields));
    }

    [Fact]
    public void ResolveName_DeveCombinarFirstELastNameQuandoFullNameAusente()
    {
        var fields = new Dictionary<string, string> { ["first_name"] = "João", ["last_name"] = "Silva" };
        Assert.Equal("João Silva", MetaFieldMapping.ResolveName(fields));
    }

    [Fact]
    public void ResolveName_DeveRetornarNuloQuandoNenhumCampoDeNomePresente()
    {
        var fields = new Dictionary<string, string> { ["email"] = "joao@teste.com" };
        Assert.Null(MetaFieldMapping.ResolveName(fields));
    }

    [Fact]
    public void NormalizeFieldData_DeveIgnorarCamposSemValores()
    {
        var fieldData = new List<MetaFieldDatum>
        {
            new("Nome Completo", ["João Silva"]),
            new("Campo Vazio", []),
            new(null, ["valor"]),
        };

        var resultado = MetaFieldMapping.NormalizeFieldData(fieldData);

        var item = Assert.Single(resultado);
        Assert.Equal("nome_completo", item.Key);
        Assert.Equal("João Silva", item.Value);
    }
}
