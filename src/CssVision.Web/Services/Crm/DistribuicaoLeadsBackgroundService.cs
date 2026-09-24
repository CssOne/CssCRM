namespace CssVision.Web.Services.Crm;

/// <summary>
/// A cada minuto, entrega a um consultor os leads do tráfego pago que chegaram sem responsável
/// (ninguém ativo e abaixo do limite naquele momento) — ver ILeadAssignmentService.DistribuirPendentesAsync.
/// </summary>
public sealed class DistribuicaoLeadsBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<DistribuicaoLeadsBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan Intervalo = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Intervalo);
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var distribuicao = scope.ServiceProvider.GetRequiredService<ILeadAssignmentService>();
                var distribuidos = await distribuicao.DistribuirPendentesAsync(stoppingToken);
                if (distribuidos > 0)
                {
                    logger.LogInformation("{Quantidade} lead(s) do tráfego pago sem responsável distribuído(s).", distribuidos);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Falha ao distribuir leads sem responsável — tentando de novo no próximo ciclo.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
