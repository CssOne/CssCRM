using System.Security.Cryptography;
using CssVision.Web.Services.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Proteção da senha de e-mail dos consultores: com chave externa o banco sozinho não revela o segredo.</summary>
public class SegredoProtetorTests
{
    private static SegredoProtetor Criar(string? chave, IDataProtectionProvider? dp = null) =>
        new(new ConfigurationBuilder().AddInMemoryCollection(chave is null ? [] : new Dictionary<string, string?> { ["Seguranca:ChaveSegredos"] = chave }).Build(),
            dp ?? new EphemeralDataProtectionProvider(), NullLogger<SegredoProtetor>.Instance);

    [Fact]
    public void ComChaveExterna_IdaEVolta_ENaoGuardaOTextoClaro()
    {
        var p = Criar("minha-chave-de-teste");

        var guardado = p.Proteger("senha-de-app-123");

        Assert.True(p.UsaChaveExterna);
        Assert.StartsWith("v2:", guardado);
        Assert.DoesNotContain("senha-de-app-123", guardado);
        Assert.Equal("senha-de-app-123", p.Revelar(guardado));
    }

    [Fact]
    public void ComChaveExterna_CadaProtecaoSaiDiferente()
    {
        var p = Criar("minha-chave-de-teste");

        Assert.NotEqual(p.Proteger("igual"), p.Proteger("igual"));
    }

    [Fact]
    public void ChaveDiferente_NaoLe_EValorAdulteradoNaoLe()
    {
        var guardado = Criar("chave-A-bem-comprida-123").Proteger("segredo");

        Assert.ThrowsAny<CryptographicException>(() => Criar("chave-B-bem-comprida-456").Revelar(guardado));
        var adulterado = guardado[..^3] + (guardado.EndsWith("AAA") ? "BBB" : "AAA");
        Assert.ThrowsAny<CryptographicException>(() => Criar("chave-A-bem-comprida-123").Revelar(adulterado));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("NULL")]
    [InlineData("curta")]
    [InlineData("   ")]
    public void ChaveAusenteNulaOuCurta_ContaComoSemChave(string valor)
    {
        // "null" é o que o jq grava no .env quando a chave não existe no segredo da AWS.
        var p = Criar(valor);

        Assert.False(p.UsaChaveExterna);
        Assert.StartsWith("v1:", p.Proteger("segredo"));
    }

    [Fact]
    public void SemChave_UsaDataProtection_ELeValoresAntigosSemPrefixo()
    {
        var dp = new EphemeralDataProtectionProvider();
        var p = Criar(null, dp);

        var guardado = p.Proteger("segredo");
        Assert.False(p.UsaChaveExterna);
        Assert.StartsWith("v1:", guardado);
        Assert.Equal("segredo", p.Revelar(guardado));

        // Linha gravada pela versão anterior: o payload do Data Protection puro, sem prefixo.
        var antigo = dp.CreateProtector("CssVision.CrmContaEmail").Protect("senha-antiga");
        Assert.Equal("senha-antiga", p.Revelar(antigo));
    }

    [Fact]
    public void ValorComChaveExterna_SemAChaveConfigurada_FalhaEmVezDeDevolverLixo()
    {
        var guardado = Criar("chave-A-bem-comprida-123").Proteger("segredo");

        Assert.ThrowsAny<CryptographicException>(() => Criar(null).Revelar(guardado));
    }
}
