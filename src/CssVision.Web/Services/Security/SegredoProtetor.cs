using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;

namespace CssVision.Web.Services.Security;

/// <summary>
/// Guarda segredos de terceiros que o sistema precisa devolver em texto (hoje: a senha de app do e-mail de cada consultor).
/// Com <c>Seguranca:ChaveSegredos</c> configurada (variável de ambiente / secret, FORA do banco), usa AES-256-GCM com essa chave: quem tem só o backup do banco
/// não consegue ler as senhas. Sem a chave, cai no Data Protection do ASP.NET — cujas chaves ficam no próprio banco, então o backup traz o segredo e a chave juntos —
/// e avisa no log. Valores antigos (sem prefixo) continuam legíveis.
/// </summary>
public interface ISegredoProtetor
{
    /// <summary>True quando há chave externa configurada (os segredos novos ficam protegidos fora do banco).</summary>
    bool UsaChaveExterna { get; }

    string Proteger(string texto);

    /// <summary>Lança <see cref="CryptographicException"/> se o valor não puder ser lido (chave trocada, dado corrompido).</summary>
    string Revelar(string protegido);
}

public sealed class SegredoProtetor : ISegredoProtetor
{
    private const string PrefixoChaveExterna = "v2:";
    private const string PrefixoDataProtection = "v1:";
    private const int TamanhoNonce = 12;
    private const int TamanhoTag = 16;
    public const int TamanhoMinimoDaChave = 16;

    private readonly byte[]? _chave;
    private readonly IDataProtector _dataProtection;

    public SegredoProtetor(IConfiguration configuracao, IDataProtectionProvider dataProtection, ILogger<SegredoProtetor> logger)
    {
        _dataProtection = dataProtection.CreateProtector("CssVision.CrmContaEmail");
        var segredo = configuracao["Seguranca:ChaveSegredos"]?.Trim();
        // O .env de produção sai do Secrets Manager com "jq -r": uma chave que faltou no segredo vira o texto "null" — e uma chave curta é fraca demais. As duas valem como "sem chave".
        if (string.IsNullOrEmpty(segredo) || segredo.Equals("null", StringComparison.OrdinalIgnoreCase) || segredo.Length < TamanhoMinimoDaChave)
        {
            logger.LogWarning("Seguranca:ChaveSegredos ausente ou curta demais (mínimo 16 caracteres): as senhas de e-mail dos consultores ficam protegidas só pelo Data Protection (chaves no banco). " +
                              "Defina a chave por variável de ambiente (ex.: openssl rand -base64 32).");
            return;
        }
        _chave = SHA256.HashData(Encoding.UTF8.GetBytes(segredo));
    }

    public bool UsaChaveExterna => _chave is not null;

    public string Proteger(string texto)
    {
        if (_chave is null) return PrefixoDataProtection + _dataProtection.Protect(texto);

        var nonce = RandomNumberGenerator.GetBytes(TamanhoNonce);
        var claro = Encoding.UTF8.GetBytes(texto);
        var cifrado = new byte[claro.Length];
        var tag = new byte[TamanhoTag];
        using var aes = new AesGcm(_chave, TamanhoTag);
        aes.Encrypt(nonce, claro, cifrado, tag);
        return PrefixoChaveExterna + Convert.ToBase64String([.. nonce, .. tag, .. cifrado]);
    }

    public string Revelar(string protegido)
    {
        if (protegido.StartsWith(PrefixoChaveExterna, StringComparison.Ordinal))
        {
            if (_chave is null) throw new CryptographicException("Valor protegido com a chave externa, mas Seguranca:ChaveSegredos não está configurada.");
            var bytes = Convert.FromBase64String(protegido[PrefixoChaveExterna.Length..]);
            if (bytes.Length < TamanhoNonce + TamanhoTag) throw new CryptographicException("Valor protegido inválido.");
            var nonce = bytes[..TamanhoNonce];
            var tag = bytes[TamanhoNonce..(TamanhoNonce + TamanhoTag)];
            var cifrado = bytes[(TamanhoNonce + TamanhoTag)..];
            var claro = new byte[cifrado.Length];
            using var aes = new AesGcm(_chave, TamanhoTag);
            aes.Decrypt(nonce, cifrado, tag, claro);
            return Encoding.UTF8.GetString(claro);
        }

        var dp = protegido.StartsWith(PrefixoDataProtection, StringComparison.Ordinal) ? protegido[PrefixoDataProtection.Length..] : protegido;
        return _dataProtection.Unprotect(dp);
    }
}
