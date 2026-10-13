using CssVision.Web.Extensions;
using Npgsql;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>O app nunca pode abrir mais conexões do que o banco aceita (RDS db.t4g.small: 81 no total).</summary>
public class ConexaoComBancoTests
{
    private const string Base = "Host=localhost;Port=5432;Database=crm;Username=u;Password=p";

    [Fact]
    public void SemTamanhoDePool_UsaOLimiteDoApp()
    {
        var conexao = ServiceCollectionExtensions.ConexaoComPoolLimitado(Base)!;

        Assert.Equal(ServiceCollectionExtensions.MaximoConexoesComOBanco, new NpgsqlConnectionStringBuilder(conexao).MaxPoolSize);
        Assert.True(ServiceCollectionExtensions.MaximoConexoesComOBanco < 81);
    }

    [Theory]
    [InlineData("Maximum Pool Size=25", 25)]
    [InlineData("MaxPoolSize=15", 15)]
    public void TamanhoDePoolDaConfiguracao_Vence(string trecho, int esperado)
    {
        var conexao = ServiceCollectionExtensions.ConexaoComPoolLimitado($"{Base};{trecho}")!;

        Assert.Equal(esperado, new NpgsqlConnectionStringBuilder(conexao).MaxPoolSize);
    }

    [Fact]
    public void ConexaoVazia_ContinuaVazia() => Assert.Null(ServiceCollectionExtensions.ConexaoComPoolLimitado(null));
}
