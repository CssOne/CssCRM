namespace CssVision.Web.Services.Storage;

/// <summary>
/// Abstrai onde os arquivos enviados pelo usuário (anexos de oportunidade, avatares) ficam
/// guardados: disco local (dev, ou produção single-instance simples) ou S3 (produção com
/// múltiplas instâncias / durabilidade fora da instância). Ver LocalFileStorageService e
/// S3FileStorageService — a escolha é feita em ServiceCollectionExtensions.AddCrmFileStorage
/// conforme a presença de Storage:S3:BucketName na configuração.
/// </summary>
public interface IFileStorageService
{
    /// <summary>Salva o conteúdo sob a pasta lógica indicada e devolve a URL pública para acessá-lo depois.</summary>
    Task<string> SalvarAsync(string pasta, string nomeArquivo, Stream conteudo, string? contentType, CancellationToken ct);

    /// <summary>Remove o arquivo referenciado pela URL (devolvida antes por SalvarAsync), se existir. Nunca lança.</summary>
    Task ExcluirSeExistirAsync(string? url, CancellationToken ct = default);
}
