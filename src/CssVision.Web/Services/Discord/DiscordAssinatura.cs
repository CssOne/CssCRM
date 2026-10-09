using System.Text;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace CssVision.Web.Services.Discord;

/// <summary>
/// O Discord assina cada requisição de comando com Ed25519 (cabeçalhos <c>X-Signature-Ed25519</c> e <c>X-Signature-Timestamp</c>): a assinatura é sobre
/// <c>timestamp + corpo</c>, conferida com a chave pública da aplicação. Sem assinatura válida o CRM não executa nada (e o Discord só aceita o endereço
/// se ele recusar assinatura errada). Também recusa requisição velha (5 minutos) para não aceitar repetição de uma antiga.
/// </summary>
public static class DiscordAssinatura
{
    public static readonly TimeSpan IdadeMaxima = TimeSpan.FromMinutes(5);

    public static bool EhValida(string chavePublicaHex, string? timestamp, string corpo, string? assinaturaHex, DateTimeOffset agora)
    {
        if (string.IsNullOrWhiteSpace(timestamp) || string.IsNullOrWhiteSpace(assinaturaHex) || chavePublicaHex.Length != 64) return false;
        if (!long.TryParse(timestamp, out var segundos)) return false;
        var enviado = DateTimeOffset.FromUnixTimeSeconds(segundos);
        if ((agora - enviado).Duration() > IdadeMaxima) return false;

        try
        {
            var chave = Convert.FromHexString(chavePublicaHex);
            var assinatura = Convert.FromHexString(assinaturaHex);
            if (chave.Length != 32 || assinatura.Length != 64) return false;

            var verificador = new Ed25519Signer();
            verificador.Init(false, new Ed25519PublicKeyParameters(chave, 0));
            var mensagem = Encoding.UTF8.GetBytes(timestamp + corpo);
            verificador.BlockUpdate(mensagem, 0, mensagem.Length);
            return verificador.VerifySignature(assinatura);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            return false;
        }
    }
}
