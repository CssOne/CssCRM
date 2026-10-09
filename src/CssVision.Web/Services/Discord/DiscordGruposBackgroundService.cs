using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using CssVision.Web.Data;

namespace CssVision.Web.Services.Discord;

/// <summary>
/// Mantém os grupos do Discord em dia com o CRM (gente nova que vinculou a conta, mudança de regional ou de grupo, usuário inativado).
/// Só entra em ação <b>depois</b> que um administrador rodou a primeira sincronização manual (existe mapeamento de canais): assim nada é
/// criado no Discord sem que alguém tenha mandado. A primeira espera de 2 minutos devolve o controle na hora, sem atrasar a subida do app.
/// </summary>
public sealed class DiscordGruposBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<DiscordOptions> options,
    ILogger<DiscordGruposBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan EsperaInicial = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan Intervalo = TimeSpan.FromMinutes(30);

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
                    var db = escopo.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    if (await db.CrmDiscordCanais.AnyAsync(stoppingToken))
                    {
                        var resultado = await escopo.ServiceProvider.GetRequiredService<IDiscordGruposService>().SincronizarAsync(stoppingToken);
                        if (resultado.Falhas.Count > 0)
                        {
                            logger.LogWarning("Sincronização automática dos grupos do Discord terminou com {Falhas} falha(s): {Primeira}", resultado.Falhas.Count, resultado.Falhas[0]);
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Falha na sincronização automática dos grupos do Discord — tenta de novo no próximo ciclo.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
