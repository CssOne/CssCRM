using System.Security.Cryptography;
using System.Text;

namespace CssVision.Web.Services.Marketing;

/// <summary>Validação da assinatura X-Hub-Signature-256 dos webhooks da Meta (porta de webhook.ts::validateMetaSignature).</summary>
public static class MetaWebhookSignature
{
    private const string Prefix = "sha256=";

    /// <summary>
    /// Se o header não vier, a validação é pulada (mesmo comportamento do Worker) — só bloqueia
    /// quando o header está presente e não bate com o HMAC calculado.
    /// </summary>
    public static bool IsValid(string rawBody, string? signatureHeader, string appSecret)
    {
        if (string.IsNullOrEmpty(signatureHeader)) return true;
        if (!signatureHeader.StartsWith(Prefix, StringComparison.Ordinal)) return false;

        var receivedHex = signatureHeader[Prefix.Length..];
        byte[] receivedBytes;
        try
        {
            receivedBytes = Convert.FromHexString(receivedHex);
        }
        catch (FormatException)
        {
            return false;
        }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(appSecret));
        var computedBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(rawBody));

        return CryptographicOperations.FixedTimeEquals(computedBytes, receivedBytes);
    }
}
