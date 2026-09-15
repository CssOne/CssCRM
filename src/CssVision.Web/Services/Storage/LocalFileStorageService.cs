namespace CssVision.Web.Services.Storage;

/// <summary>Guarda os arquivos em wwwroot/uploads, servidos depois via UseStaticFiles. Usado em
/// desenvolvimento e em produção single-instance sem Storage:S3:BucketName configurado.</summary>
public sealed class LocalFileStorageService(IWebHostEnvironment ambiente) : IFileStorageService
{
    public async Task<string> SalvarAsync(string pasta, string nomeArquivo, Stream conteudo, string? contentType, CancellationToken ct)
    {
        var diretorio = Path.Combine(ambiente.WebRootPath, "uploads", pasta);
        Directory.CreateDirectory(diretorio);

        await using var arquivo = new FileStream(Path.Combine(diretorio, nomeArquivo), FileMode.Create);
        await conteudo.CopyToAsync(arquivo, ct);

        return $"/uploads/{pasta}/{nomeArquivo}";
    }

    public Task ExcluirSeExistirAsync(string? url, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(url)) return Task.CompletedTask;

        var caminho = Path.Combine(ambiente.WebRootPath, url.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(caminho))
        {
            File.Delete(caminho);
        }
        return Task.CompletedTask;
    }
}
