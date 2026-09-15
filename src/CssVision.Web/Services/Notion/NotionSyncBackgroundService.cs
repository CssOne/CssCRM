using Microsoft.Extensions.Options;

namespace CssVision.Web.Services.Notion;

/// <summary>Dispara NotionSyncService.SincronizarTudoAsync em intervalos regulares enquanto o app estiver no ar.</summary>
public sealed class NotionSyncBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<NotionSyncOptions> options,
    ILogger<NotionSyncBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var config = options.Value;
        if (!config.Enabled || string.IsNullOrWhiteSpace(config.Token))
        {
            logger.LogInformation("Sincronização com o Notion desabilitada (sem token configurado).");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(1, config.IntervalMinutes)));
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var syncService = scope.ServiceProvider.GetRequiredService<NotionSyncService>();
                var resumo = await syncService.SincronizarTudoAsync(config.Token, stoppingToken);
                logger.LogInformation("Sincronização com o Notion concluída: {Resumo}", resumo);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Falha inesperada na sincronização com o Notion — tentando de novo no próximo ciclo.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
