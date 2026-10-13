using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CssVision.Web.Services.Notion;

/// <summary>
/// Carga única da tag de anúncio do Notion ("Facebook ADS"/"Google ads") nos leads antigos (<see cref="CrmLead.TagDeAnuncioNotion"/>). A sincronização já grava a tag
/// dos cards que ela processa, mas só os editados depois dela; sem esta carga, um card antigo que tem só a tag (sem ID do Meta nem GCLID) seria tratado como "sem
/// sinal de anúncio" ao concluir a venda. Pede ao Notion só os cards com a tag (filtro no servidor do Notion) e marca os leads ligados a eles.
/// Roda uma vez, bem depois de o app subir, e qualquer falha só vai para o log (tenta de novo na próxima inicialização, enquanto o marcador não existir).
/// </summary>
public sealed class PreencherTagDeAnuncioBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<NotionSyncOptions> options,
    ILogger<PreencherTagDeAnuncioBackgroundService> logger) : BackgroundService
{
    public const string ChaveExecutado = "notion:tag-anuncio-preenchida-em";
    public const string ChaveStatus = "notion:tag-anuncio-status";

    private static readonly TimeSpan EsperaInicial = TimeSpan.FromMinutes(4);
    private const int TamanhoDoLote = 500;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            if (!options.Value.Enabled || string.IsNullOrWhiteSpace(options.Value.Token)) return;
            await Task.Delay(EsperaInicial, stoppingToken);

            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Database.SetCommandTimeout(TimeSpan.FromSeconds(120));

            if (await db.CrmParametros.AsNoTracking().AnyAsync(p => p.Chave == ChaveExecutado, stoppingToken)) return;

            var (cards, marcados) = await ExecutarAsync(db, new NotionClient(options.Value.Token!), stoppingToken);
            await GravarAsync(db, ChaveExecutado, $"{DateTimeOffset.UtcNow:O} | cards com a tag: {cards}; leads marcados agora: {marcados}", stoppingToken);
            logger.LogInformation("Tag de anúncio do Notion: {Cards} card(s) com a tag, {Marcados} lead(s) marcado(s).", cards, marcados);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha na carga da tag de anúncio do Notion — o app segue normalmente; tenta de novo na próxima inicialização.");
        }
    }

    /// <returns>Quantos cards do Notion têm a tag e quantos leads foram marcados agora.</returns>
    public static async Task<(int Cards, int Marcados)> ExecutarAsync(ApplicationDbContext db, NotionClient notion, CancellationToken ct)
    {
        object filtro = new
        {
            or = new object[]
            {
                new { property = "Tags", select = new { equals = "Facebook ADS" } },
                new { property = "Tags", select = new { equals = "Google ads" } },
            },
        };

        int cards = 0, marcados = 0;
        foreach (var dataSourceId in NotionSyncService.IdsDasBasesSincronizadas)
        {
            var lote = new List<string>(TamanhoDoLote);
            await foreach (var page in notion.QueryAsync(dataSourceId, filtro, ct))
            {
                cards++;
                lote.Add(page.PageId());
                if (lote.Count >= TamanhoDoLote)
                {
                    marcados += await MarcarAsync(db, lote, ct);
                    lote.Clear();
                }
            }
            if (lote.Count > 0) marcados += await MarcarAsync(db, lote, ct);
        }
        return (cards, marcados);
    }

    internal static Task<int> MarcarAsync(ApplicationDbContext db, List<string> ids, CancellationToken ct) =>
        db.CrmLeads.Where(l => l.NotionPageId != null && ids.Contains(l.NotionPageId) && !l.TagDeAnuncioNotion)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.TagDeAnuncioNotion, true), ct);

    private static async Task GravarAsync(ApplicationDbContext db, string chave, string valor, CancellationToken ct)
    {
        var existente = await db.CrmParametros.FirstOrDefaultAsync(p => p.Chave == chave, ct);
        if (existente is null) db.CrmParametros.Add(new CrmParametro { Chave = chave, Valor = valor });
        else existente.Valor = valor;
        await db.SaveChangesAsync(ct);
    }
}
