using System.Diagnostics;
using Amazon.S3;
using Amazon.S3.Model;
using CssVision.Web.Services.Storage;
using Microsoft.Extensions.Options;
using Npgsql;

namespace CssVision.Web.Services.Backup;

/// <summary>Gera a cópia completa do banco num arquivo temporário (formato do pg_dump, compactado).</summary>
public interface IDumpBanco
{
    /// <returns>Caminho do arquivo gerado — quem chama apaga depois.</returns>
    Task<string> GerarAsync(CancellationToken ct);
}

/// <summary>Onde as cópias ficam guardadas (fora do banco).</summary>
public interface IArmazenamentoBackup
{
    Task SalvarAsync(string chave, string caminhoArquivo, CancellationToken ct);
    Task<Stream> AbrirAsync(string chave, CancellationToken ct);
    Task ExcluirAsync(string chave, CancellationToken ct);
}

/// <summary>
/// pg_dump (cliente do PostgreSQL 16, instalado na imagem — ver Dockerfile) com a mesma connection
/// string do app. A senha vai por variável de ambiente do processo filho, nunca na linha de comando.
/// </summary>
public sealed class PgDumpBanco(IConfiguration configuration, ILogger<PgDumpBanco> logger) : IDumpBanco
{
    private static readonly TimeSpan TempoMaximo = TimeSpan.FromMinutes(20);

    public async Task<string> GerarAsync(CancellationToken ct)
    {
        var conexao = new NpgsqlConnectionStringBuilder(configuration.GetConnectionString("Default"));
        var arquivo = Path.Combine(Path.GetTempPath(), $"crm-backup-{Guid.NewGuid():N}.dump");

        var inicio = new ProcessStartInfo("pg_dump")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        foreach (var arg in new[] { "--format=custom", "--compress=6", "--no-owner", "--no-privileges", "--file", arquivo })
            inicio.ArgumentList.Add(arg);
        inicio.Environment["PGHOST"] = conexao.Host;
        inicio.Environment["PGPORT"] = conexao.Port.ToString();
        inicio.Environment["PGDATABASE"] = conexao.Database;
        inicio.Environment["PGUSER"] = conexao.Username;
        inicio.Environment["PGPASSWORD"] = conexao.Password;
        // RDS com PostgreSQL 16 exige SSL; localmente sem SSL também funciona ("prefer").
        inicio.Environment["PGSSLMODE"] = "prefer";

        Process processo;
        try
        {
            processo = Process.Start(inicio) ?? throw new InvalidOperationException("Não foi possível iniciar o pg_dump.");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new InvalidOperationException("pg_dump não está instalado neste servidor (a imagem de produção já o inclui).");
        }

        using (processo)
        using (var limite = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            limite.CancelAfter(TempoMaximo);
            var erros = processo.StandardError.ReadToEndAsync(limite.Token);
            try
            {
                await processo.WaitForExitAsync(limite.Token);
            }
            catch (OperationCanceledException)
            {
                try { processo.Kill(entireProcessTree: true); } catch { /* já terminou */ }
                ApagarSeExistir(arquivo);
                throw;
            }

            if (processo.ExitCode != 0)
            {
                var mensagem = (await erros).Trim();
                ApagarSeExistir(arquivo);
                logger.LogError("pg_dump falhou (código {Codigo}): {Erro}", processo.ExitCode, mensagem);
                throw new InvalidOperationException($"pg_dump falhou: {(mensagem.Length > 500 ? mensagem[..500] : mensagem)}");
            }
        }

        return arquivo;
    }

    internal static void ApagarSeExistir(string caminho)
    {
        try { if (File.Exists(caminho)) File.Delete(caminho); } catch { /* arquivo temporário */ }
    }
}

/// <summary>
/// Produção: pasta "backups/" do mesmo bucket dos anexos — privada (a leitura pública do bucket
/// vale só para "uploads/*", ver infra/aws/s3.tf) e com permissão de gravação do app (iam.tf).
/// </summary>
public sealed class S3ArmazenamentoBackup(IAmazonS3 s3, IOptions<S3StorageOptions> options) : IArmazenamentoBackup
{
    private readonly string _bucket = options.Value.BucketName;

    public async Task SalvarAsync(string chave, string caminhoArquivo, CancellationToken ct) =>
        await s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _bucket,
            Key = chave,
            FilePath = caminhoArquivo,
            ContentType = "application/octet-stream",
            ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256,
        }, ct);

    public async Task<Stream> AbrirAsync(string chave, CancellationToken ct)
    {
        var resposta = await s3.GetObjectAsync(_bucket, chave, ct);
        return resposta.ResponseStream;
    }

    public async Task ExcluirAsync(string chave, CancellationToken ct) =>
        await s3.DeleteObjectAsync(_bucket, chave, ct);
}

/// <summary>Desenvolvimento (sem S3): pasta App_Data/backups, fora do wwwroot (não é servida pela web).</summary>
public sealed class LocalArmazenamentoBackup(IWebHostEnvironment ambiente) : IArmazenamentoBackup
{
    private string Caminho(string chave) =>
        Path.Combine(ambiente.ContentRootPath, "App_Data", chave.Replace('/', Path.DirectorySeparatorChar));

    public Task SalvarAsync(string chave, string caminhoArquivo, CancellationToken ct)
    {
        var destino = Caminho(chave);
        Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
        File.Copy(caminhoArquivo, destino, overwrite: true);
        return Task.CompletedTask;
    }

    public Task<Stream> AbrirAsync(string chave, CancellationToken ct) =>
        Task.FromResult<Stream>(File.OpenRead(Caminho(chave)));

    public Task ExcluirAsync(string chave, CancellationToken ct)
    {
        PgDumpBanco.ApagarSeExistir(Caminho(chave));
        return Task.CompletedTask;
    }
}
