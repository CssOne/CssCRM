using CssVision.Web.Domain.Crm;

namespace CssVision.Web.Services.Backup;

/// <summary>
/// Backup automático uma vez por dia, de madrugada (03:00 em Brasília, fora do horário de uso).
/// Ao subir o app, se o último backup tiver mais de um dia (ex.: servidor ficou parado), faz um logo.
/// </summary>
public sealed class BackupDiarioBackgroundService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<BackupDiarioBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan Brasilia = TimeSpan.FromHours(-3);
    private static readonly TimeOnly Horario = new(3, 0);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Backup:Automatico", true))
        {
            logger.LogInformation("Backup automático desabilitado (Backup:Automatico = false).");
            return;
        }

        // Deixa o app terminar de subir (migrações, sincronizações) antes do primeiro.
        await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        await FazerSeAtrasadoAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(ProximoHorario(DateTimeOffset.UtcNow) - DateTimeOffset.UtcNow, stoppingToken);
            await FazerAsync(stoppingToken);
        }
    }

    /// <summary>Próxima vez que for 03:00 em Brasília.</summary>
    public static DateTimeOffset ProximoHorario(DateTimeOffset agoraUtc)
    {
        var local = agoraUtc.ToOffset(Brasilia);
        var hoje = new DateTimeOffset(DateOnly.FromDateTime(local.DateTime).ToDateTime(Horario), Brasilia);
        return hoje > local ? hoje : hoje.AddDays(1);
    }

    private async Task FazerSeAtrasadoAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var ultimo = await scope.ServiceProvider.GetRequiredService<IBackupService>().UltimoConcluidoEmAsync(ct);
            if (ultimo is null || DateTimeOffset.UtcNow - ultimo > TimeSpan.FromHours(26)) await FazerAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao verificar o último backup.");
        }
    }

    private async Task FazerAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IBackupService>().GerarAsync(OrigemBackup.Automatico, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            // Já registrado como "Falhou" na lista de backups; tenta de novo amanhã.
            logger.LogError(ex, "Backup automático falhou.");
        }
    }
}
