using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using Microsoft.EntityFrameworkCore;

namespace CssVision.Web.Services.Crm;

/// <summary>
/// Carga única: desmarca "Teve indicação" das vendas que estão com o campo marcado mas com valor da indicação em 0,00 (ou sem valor). A tela de
/// venda concluída marcava a indicação sozinha para cards de indicação e a venda ficava com indicação de R$ 0,00. Só o "Teve indicação" é desmarcado:
/// o canal de aquisição, o valor e o resto da venda ficam como estão. Roda uma vez (marcador em CrmParametros); falha só vai para o log.
/// </summary>
public sealed class DesmarcarIndicacaoSemValorBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<DesmarcarIndicacaoSemValorBackgroundService> logger) : BackgroundService
{
    public const string ChaveExecutado = "crm:indicacao-sem-valor-desmarcada-em";
    public const string ChaveStatus = "notion:indicacao-sem-valor-desmarcada";

    private static readonly TimeSpan EsperaInicial = TimeSpan.FromMinutes(3);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(EsperaInicial, stoppingToken);

            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Database.SetCommandTimeout(TimeSpan.FromSeconds(120));

            if (await db.CrmParametros.AsNoTracking().AnyAsync(p => p.Chave == ChaveExecutado, stoppingToken)) return;

            var desmarcadas = await ExecutarAsync(db, stoppingToken);
            var resumo = $"{DateTimeOffset.UtcNow:O} | vendas com indicação de R$ 0,00 desmarcadas: {desmarcadas}";
            await GravarAsync(db, ChaveStatus, resumo, stoppingToken);
            await GravarAsync(db, ChaveExecutado, resumo, stoppingToken);
            logger.LogInformation("Indicação sem valor desmarcada em {Quantidade} venda(s).", desmarcadas);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao desmarcar a indicação sem valor — o app segue; tenta de novo na próxima inicialização.");
        }
    }

    /// <returns>Quantas oportunidades tiveram o "Teve indicação" desmarcado.</returns>
    public static Task<int> ExecutarAsync(ApplicationDbContext db, CancellationToken ct) =>
        db.CrmOpportunities
            .Where(o => o.Indicacao == true && (o.ValorIndicacao == null || o.ValorIndicacao == 0m))
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Indicacao, false), ct);

    private static async Task GravarAsync(ApplicationDbContext db, string chave, string valor, CancellationToken ct)
    {
        var existente = await db.CrmParametros.FirstOrDefaultAsync(p => p.Chave == chave, ct);
        if (existente is null) db.CrmParametros.Add(new CrmParametro { Chave = chave, Valor = valor });
        else existente.Valor = valor;
        await db.SaveChangesAsync(ct);
    }
}
