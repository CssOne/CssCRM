using Microsoft.Extensions.Options;

namespace CssVision.Web.Services.Discord;

/// <summary>
/// Dispara o resumo diário de leads parados nos canais do Discord. Acorda a cada 10 minutos; quem decide se já é hora (depois das 9h30),
/// se está ligado e se já saiu hoje é o próprio serviço de avisos. Nada acontece enquanto o administrador não ligar o aviso na tela do Discord.
/// </summary>
public sealed class DiscordAvisosBackgroundService(IServiceScopeFactory scopeFactory, IOptions<DiscordOptions> options, ILogger<DiscordAvisosBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan EsperaInicial = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan Intervalo = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(EsperaInicial, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var timer = new PeriodicTimer(Intervalo);
        do
        {
            try
            {
                if (options.Value.Configurado)
                {
                    await using var escopo = scopeFactory.CreateAsyncScope();
                    await escopo.ServiceProvider.GetRequiredService<IDiscordAvisosNosCanaisService>().PublicarLeadsParadosDoDiaAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Falha no resumo diário de leads parados no Discord — tenta de novo no próximo ciclo.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
