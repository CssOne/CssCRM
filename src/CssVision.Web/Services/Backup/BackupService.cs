using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using Microsoft.EntityFrameworkCore;

namespace CssVision.Web.Services.Backup;

public interface IBackupService
{
    Task<CrmBackupDto> GerarAsync(OrigemBackup origem, CancellationToken ct);
    Task<IReadOnlyList<CrmBackupDto>> ListarAsync(CancellationToken ct);
    Task<(Stream Conteudo, string NomeArquivo)> AbrirAsync(Guid id, CancellationToken ct);
    /// <summary>Hora do último backup concluído (nulo se nunca houve).</summary>
    Task<DateTimeOffset?> UltimoConcluidoEmAsync(CancellationToken ct);
}

/// <summary>
/// Backup completo do banco do CRM, guardado fora dele (S3 em produção). Complementa os backups
/// automáticos do RDS: esses ficam presos à conta da AWS e ao próprio RDS — em 30/09/2026 a conta
/// foi encerrada e o banco ficou inacessível. Estes arquivos o administrador baixa pela tela de
/// Backups e guarda onde quiser.
/// </summary>
public sealed class BackupService(
    ApplicationDbContext db,
    IDumpBanco dump,
    IArmazenamentoBackup armazenamento,
    IAuditSink audit,
    ILogger<BackupService> logger) : IBackupService
{
    /// <summary>Quantos backups concluídos ficam guardados (os mais antigos são apagados).</summary>
    public const int QuantidadeGuardada = 30;

    /// <summary>Um backup por vez no processo (o diário e o "gerar agora" não se atropelam).</summary>
    private static readonly SemaphoreSlim UmPorVez = new(1, 1);

    private static readonly TimeSpan Brasilia = TimeSpan.FromHours(-3);

    public async Task<CrmBackupDto> GerarAsync(OrigemBackup origem, CancellationToken ct)
    {
        if (!await UmPorVez.WaitAsync(TimeSpan.Zero, ct))
        {
            throw new CrmBusinessException("Já existe um backup sendo gerado. Aguarde terminar.", "backup_em_andamento");
        }

        try
        {
            var backup = new CrmBackup { Status = StatusBackup.EmAndamento, Origem = origem };
            db.CrmBackups.Add(backup);
            await db.SaveChangesAsync(ct);

            string? arquivo = null;
            try
            {
                // Contagens antes da cópia: servem para conferir que o arquivo tem tudo.
                backup.Leads = await db.CrmLeads.CountAsync(ct);
                backup.Oportunidades = await db.CrmOpportunities.CountAsync(ct);
                backup.Usuarios = await db.Users.CountAsync(ct);

                arquivo = await dump.GerarAsync(ct);
                var agora = DateTimeOffset.UtcNow.ToOffset(Brasilia);
                var chave = $"backups/crm-{agora:yyyyMMdd-HHmmss}.dump";
                await armazenamento.SalvarAsync(chave, arquivo, ct);

                backup.Chave = chave;
                backup.TamanhoBytes = new FileInfo(arquivo).Length;
                backup.Status = StatusBackup.Concluido;
                backup.ConcluidoEm = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct);
                logger.LogInformation("Backup {Chave} concluído ({Tamanho} bytes, {Leads} leads).", chave, backup.TamanhoBytes, backup.Leads);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogError(ex, "Falha ao gerar o backup do banco.");
                backup.Status = StatusBackup.Falhou;
                backup.Erro = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
                backup.ConcluidoEm = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(CancellationToken.None);
                throw new CrmBusinessException($"Não foi possível gerar o backup: {backup.Erro}", "backup_falhou");
            }
            finally
            {
                if (arquivo is not null) PgDumpBanco.ApagarSeExistir(arquivo);
            }

            await audit.RegistrarAsync("BackupGerado", nameof(CrmBackup), backup.Id, new { backup.Chave, backup.Origem }, ct);
            await AplicarRetencaoAsync(ct);
            return ParaDto(backup);
        }
        finally
        {
            UmPorVez.Release();
        }
    }

    /// <summary>Guarda só os <see cref="QuantidadeGuardada"/> backups concluídos mais recentes.</summary>
    private async Task AplicarRetencaoAsync(CancellationToken ct)
    {
        var antigos = await db.CrmBackups
            .Where(b => b.Status == StatusBackup.Concluido)
            .OrderByDescending(b => b.CriadoEm)
            .Skip(QuantidadeGuardada)
            .ToListAsync(ct);

        foreach (var antigo in antigos)
        {
            try
            {
                if (antigo.Chave is not null) await armazenamento.ExcluirAsync(antigo.Chave, ct);
                antigo.Status = StatusBackup.Removido;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Não foi possível apagar o backup antigo {Chave}; tento de novo no próximo.", antigo.Chave);
            }
        }
        if (antigos.Count > 0) await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<CrmBackupDto>> ListarAsync(CancellationToken ct) =>
        (await db.CrmBackups.AsNoTracking()
            .Where(b => b.Status != StatusBackup.Removido)
            .OrderByDescending(b => b.CriadoEm)
            .Take(60)
            .ToListAsync(ct))
        .Select(ParaDto)
        .ToList();

    public async Task<(Stream Conteudo, string NomeArquivo)> AbrirAsync(Guid id, CancellationToken ct)
    {
        var backup = await db.CrmBackups.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, ct)
            ?? throw new CrmNotFoundException("Backup", id);
        if (backup.Status != StatusBackup.Concluido || backup.Chave is null)
        {
            throw new CrmBusinessException("Este backup não está disponível para download.", "backup_indisponivel");
        }

        var conteudo = await armazenamento.AbrirAsync(backup.Chave, ct);
        await audit.RegistrarAsync("BackupBaixado", nameof(CrmBackup), backup.Id, new { backup.Chave }, ct);
        return (conteudo, Path.GetFileName(backup.Chave));
    }

    public Task<DateTimeOffset?> UltimoConcluidoEmAsync(CancellationToken ct) =>
        db.CrmBackups.AsNoTracking()
            .Where(b => b.Status == StatusBackup.Concluido)
            .OrderByDescending(b => b.CriadoEm)
            .Select(b => (DateTimeOffset?)b.CriadoEm)
            .FirstOrDefaultAsync(ct);

    private static CrmBackupDto ParaDto(CrmBackup b) => new(
        b.Id, b.Status.ToString(), b.Origem.ToString(), b.CriadoEm, b.ConcluidoEm, b.TamanhoBytes,
        b.Chave is null ? null : Path.GetFileName(b.Chave), b.Leads, b.Oportunidades, b.Usuarios, b.Erro);
}
