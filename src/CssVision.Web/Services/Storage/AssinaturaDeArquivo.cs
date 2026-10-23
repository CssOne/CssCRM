namespace CssVision.Web.Services.Storage;

/// <summary>
/// Confere o conteúdo real do arquivo enviado (os primeiros bytes), não só o nome: um executável ou HTML renomeado para
/// ".pdf"/".png" passa pela checagem de extensão, mas não tem a assinatura do formato.
/// </summary>
public static class AssinaturaDeArquivo
{
    /// <summary>Lê o começo do arquivo e diz se ele é mesmo do formato da extensão (".pdf", ".jpg", ".jpeg", ".png", ".webp").</summary>
    public static async Task<bool> ConfereAsync(Stream conteudo, string extensao, CancellationToken ct)
    {
        var inicio = new byte[12];
        var lidos = 0;
        while (lidos < inicio.Length)
        {
            var n = await conteudo.ReadAsync(inicio.AsMemory(lidos), ct);
            if (n == 0) break;
            lidos += n;
        }
        if (conteudo.CanSeek) conteudo.Position = 0;

        return Confere(inicio.AsSpan(0, lidos), extensao);
    }

    public static bool Confere(ReadOnlySpan<byte> inicio, string extensao) =>
        extensao.ToLowerInvariant() switch
        {
            ".pdf" => inicio.StartsWith("%PDF-"u8),
            ".jpg" or ".jpeg" => inicio.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF }),
            ".png" => inicio.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
            // WEBP: "RIFF" + tamanho (4 bytes) + "WEBP".
            ".webp" => inicio.Length >= 12 && inicio[..4].SequenceEqual("RIFF"u8) && inicio[8..12].SequenceEqual("WEBP"u8),
            _ => false,
        };
}
