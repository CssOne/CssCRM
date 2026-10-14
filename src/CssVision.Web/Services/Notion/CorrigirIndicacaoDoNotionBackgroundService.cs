using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CssVision.Web.Services.Notion;

/// <summary>
/// Carga única depois da correção de <c>IndicacaoMarcada</c> (agora "INDICAÇÃO" vale como "SIM"): ajusta no CRM as vendas de cards "Lead" que fecharam como indicação
/// (ver <see cref="NotionSyncService.CorrigirIndicacaoMarcadaComoIndicacaoAsync"/>). Roda uma vez, bem depois de o app subir; falha só vai para o log e tenta de novo na
/// próxima inicialização enquanto o marcador não existir.
/// </summary>
public sealed class CorrigirIndicacaoDoNotionBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<NotionSyncOptions> options,
    ILogger<CorrigirIndicacaoDoNotionBackgroundService> logger) : BackgroundService
{
    public const string ChaveExecutado = "notion:indicacao-marcada-corrigida-em";
    public const string ChaveStatus = "notion:indicacao-marcada-corrigida";

    private static readonly TimeSpan EsperaInicial = TimeSpan.FromMinutes(6);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            if (!options.Value.Enabled || string.IsNullOrWhiteSpace(options.Value.Token)) return;
            await Task.Delay(EsperaInicial, stoppingToken);

            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            if (await db.CrmParametros.AsNoTracking().AnyAsync(p => p.Chave == ChaveExecutado, stoppingToken)) return;

            var servico = scope.ServiceProvider.GetRequiredService<NotionSyncService>();
            var resumo = $"{DateTimeOffset.UtcNow:O} | {await servico.CorrigirIndicacaoMarcadaComoIndicacaoAsync(options.Value.Token!, stoppingToken)}";
            await GravarAsync(db, ChaveStatus, resumo, stoppingToken);
            await GravarAsync(db, ChaveExecutado, resumo, stoppingToken);
            logger.LogInformation("Indicação marcada no Notion corrigida: {Resumo}", resumo);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao corrigir as vendas de indicação do Notion — o app segue; tenta de novo na próxima inicialização.");
        }
    }

    private static async Task GravarAsync(ApplicationDbContext db, string chave, string valor, CancellationToken ct)
    {
        var existente = await db.CrmParametros.FirstOrDefaultAsync(p => p.Chave == chave, ct);
        if (existente is null) db.CrmParametros.Add(new CrmParametro { Chave = chave, Valor = valor });
        else existente.Valor = valor;
        await db.SaveChangesAsync(ct);
    }
}
