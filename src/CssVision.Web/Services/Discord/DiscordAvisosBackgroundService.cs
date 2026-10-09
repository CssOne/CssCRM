using Microsoft.Extensions.Options;

namespace CssVision.Web.Services.Discord;

/// <summary>
/// Dispara os avisos automáticos dos canais do Discord (lead novo, leads parados, resumo do dia). Acorda a cada 2 minutos; quem decide se já é hora (depois das 9h30),
/// se está ligado e se já saiu hoje é o próprio serviço de avisos. Nada acontece enquanto o administrador não ligar o aviso na tela do Discord.
/// </summary>
public sealed class DiscordAvisosBackgroundService(IServiceScopeFactory scopeFactory, IOptions<DiscordOptions> options, ILogger<DiscordAvisosBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan EsperaInicial = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan Intervalo = TimeSpan.FromMinutes(2);

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
                    var avisos = escopo.ServiceProvider.GetRequiredService<IDiscordAvisosNosCanaisService>();
                    // Cada aviso é independente: a falha de um não impede os outros neste ciclo.
                    await Tentar(() => avisos.PublicarLeadsNovosAsync(stoppingToken), "lead novo");
                    await Tentar(() => avisos.PublicarLeadsParadosDoDiaAsync(stoppingToken), "leads parados");
                    await Tentar(() => avisos.PublicarResumoDoDiaAsync(stoppingToken), "resumo do dia");
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Falha nos avisos automáticos do Discord — tenta de novo no próximo ciclo.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task Tentar(Func<Task<int>> aviso, string nome)
    {
        try
        {
            await aviso();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha no aviso automático \"{Aviso}\" no Discord — tenta de novo no próximo ciclo.", nome);
        }
    }
}
