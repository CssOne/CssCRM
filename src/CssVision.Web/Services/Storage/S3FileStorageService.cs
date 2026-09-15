using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace CssVision.Web.Services.Storage;

/// <summary>
/// Guarda os arquivos num bucket S3 — sobrevive a troca/replicação da instância EC2 (disco local
/// não sobrevive). Os objetos ficam com leitura pública via bucket policy (definida no Terraform,
/// não aqui): mesma postura de segurança que o disco local já tinha (URL não-autenticada, mas com
/// caminho imprevisível por usar o Guid da oportunidade/usuário).
/// </summary>
public sealed class S3FileStorageService(IAmazonS3 s3, IOptions<S3StorageOptions> options) : IFileStorageService
{
    private readonly S3StorageOptions _options = options.Value;

    public async Task<string> SalvarAsync(string pasta, string nomeArquivo, Stream conteudo, string? contentType, CancellationToken ct)
    {
        var chave = $"uploads/{pasta}/{nomeArquivo}";

        await s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _options.BucketName,
            Key = chave,
            InputStream = conteudo,
            ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType,
            AutoCloseStream = false,
        }, ct);

        return MontarUrlPublica(chave);
    }

    public async Task ExcluirSeExistirAsync(string? url, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(url)) return;

        var chave = ExtrairChave(url);
        if (chave is null) return;

        try
        {
            await s3.DeleteObjectAsync(_options.BucketName, chave, ct);
        }
        catch (AmazonS3Exception)
        {
            // Objeto já não existe ou bucket inacessível — não deve derrubar a operação de negócio.
        }
    }

    private string MontarUrlPublica(string chave) =>
        string.IsNullOrWhiteSpace(_options.PublicBaseUrl)
            ? $"https://{_options.BucketName}.s3.{_options.Region}.amazonaws.com/{chave}"
            : $"{_options.PublicBaseUrl.TrimEnd('/')}/{chave}";

    /// <summary>Recupera a chave S3 a partir da URL pública devolvida por MontarUrlPublica.</summary>
    private string? ExtrairChave(string url)
    {
        var indice = url.IndexOf("/uploads/", StringComparison.Ordinal);
        return indice >= 0 ? url[(indice + 1)..] : null;
    }
}
