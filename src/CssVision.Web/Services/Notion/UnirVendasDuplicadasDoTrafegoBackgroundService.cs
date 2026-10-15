using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using Microsoft.EntityFrameworkCore;

namespace CssVision.Web.Services.Notion;

/// <summary>
/// Carga única: arquiva os leads do Notion que duplicam a venda de um lead do tráfego pago (ver <see cref="DuplicidadeDeVendaDoNotion"/>). O resultado, com as placas arquivadas,
/// fica na tela de sincronização do Notion para conferência. Roda uma vez (marcador em CrmParametros); falha só vai para o log e tenta de novo na próxima inicialização.
/// </summary>
public sealed class UnirVendasDuplicadasDoTrafegoBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<UnirVendasDuplicadasDoTrafegoBackgroundService> logger) : BackgroundService
{
    public const string ChaveExecutado = "notion:vendas-duplicadas-do-trafego-em";
    public const string ChaveStatus = "notion:vendas-duplicadas-do-trafego";

    private static readonly TimeSpan EsperaInicial = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(EsperaInicial, stoppingToken);

            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Database.SetCommandTimeout(TimeSpan.FromSeconds(120));
            if (await db.CrmParametros.AsNoTracking().AnyAsync(p => p.Chave == ChaveExecutado, stoppingToken)) return;

            var placas = await ExecutarAsync(db, stoppingToken);
            var resumo = $"{DateTimeOffset.UtcNow:O} | leads do Notion arquivados por duplicarem a venda de um lead do tráfego pago: {placas.Count}"
                + (placas.Count > 0 ? $" (placas: {string.Join(", ", placas)})" : "");
            await GravarAsync(db, ChaveStatus, resumo, stoppingToken);
            await GravarAsync(db, ChaveExecutado, resumo, stoppingToken);
            logger.LogInformation("Vendas duplicadas do tráfego: {Resumo}", resumo);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao unir as vendas duplicadas do tráfego — o app segue; tenta de novo na próxima inicialização.");
        }
    }

    /// <returns>As placas dos leads arquivados.</returns>
    public static async Task<List<string>> ExecutarAsync(ApplicationDbContext db, CancellationToken ct)
    {
        var marcadoresNotion = new[] { OrigemLead.MarcadorMigracaoNotion, OrigemLead.MarcadorSincronizacaoNotion };
        var candidatos = await db.CrmLeads.AsNoTracking()
            .Where(n => !n.Arquivado && n.Placa != null && n.ResponsavelId != null && n.ConsentimentoOrigem != null && marcadoresNotion.Contains(n.ConsentimentoOrigem)
                && db.CrmLeads.Any(s => s.Id != n.Id && !s.Arquivado && s.Placa == n.Placa && s.ResponsavelId == n.ResponsavelId
                    && s.ConsentimentoOrigem != OrigemLead.MarcadorMigracaoNotion && s.ConsentimentoOrigem != OrigemLead.MarcadorSincronizacaoNotion))
            .Select(n => n.Id)
            .ToListAsync(ct);

        var placas = new List<string>();
        foreach (var id in candidatos)
        {
            ct.ThrowIfCancellationRequested();
            var lead = await db.CrmLeads.FirstOrDefaultAsync(l => l.Id == id, ct);
            if (lead is null) continue;
            var placa = lead.Placa;
            if (await DuplicidadeDeVendaDoNotion.ArquivarSeDuplicadoDeTrafegoAsync(db, lead, ct) && placa is not null) placas.Add(placa);
            db.ChangeTracker.Clear();
        }
        return placas;
    }

    private static async Task GravarAsync(ApplicationDbContext db, string chave, string valor, CancellationToken ct)
    {
        var existente = await db.CrmParametros.FirstOrDefaultAsync(p => p.Chave == chave, ct);
        if (existente is null) db.CrmParametros.Add(new CrmParametro { Chave = chave, Valor = valor });
        else existente.Valor = valor;
        await db.SaveChangesAsync(ct);
    }
}
