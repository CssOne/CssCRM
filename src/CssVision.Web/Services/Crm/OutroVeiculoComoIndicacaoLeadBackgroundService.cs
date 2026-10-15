using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Notion;
using Microsoft.EntityFrameworkCore;

namespace CssVision.Web.Services.Crm;

/// <summary>
/// Carga única: as vendas de "Outro veículo" (veículo adicional) de clientes do tráfego pago que ficaram como venda de Lead passam a ser indicação ("Indicação Lead"):
/// o card vai para "Venda concluída (Indicação)", perde a etiqueta do produto (AGV...) e a do anúncio (origem/campanha), e a venda leva o canal "Indicação Lead".
/// Mesma regra de <c>OpportunityService.ComoIndicacaoLeadDeOutroVeiculo</c>, que vale daqui para frente. O resultado, com as placas, fica na tela de sincronização do Notion.
/// Roda uma vez (marcador em CrmParametros); falha só vai para o log e tenta de novo na próxima inicialização.
/// </summary>
public sealed class OutroVeiculoComoIndicacaoLeadBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<OutroVeiculoComoIndicacaoLeadBackgroundService> logger) : BackgroundService
{
    public const string ChaveExecutado = "crm:outro-veiculo-indicacao-lead-em";
    public const string ChaveStatus = "notion:outro-veiculo-indicacao-lead";

    private static readonly TimeSpan EsperaInicial = TimeSpan.FromMinutes(4);

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
            var resumo = $"{DateTimeOffset.UtcNow:O} | vendas de outro veículo passadas para Indicação Lead: {placas.Count}" + (placas.Count > 0 ? $" (placas: {string.Join(", ", placas)})" : "");
            await GravarAsync(db, ChaveStatus, resumo, stoppingToken);
            await GravarAsync(db, ChaveExecutado, resumo, stoppingToken);
            logger.LogInformation("Outro veículo como Indicação Lead: {Resumo}", resumo);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao passar o outro veículo para Indicação Lead — o app segue; tenta de novo na próxima inicialização.");
        }
    }

    /// <returns>As placas (ou os nomes, sem placa) dos cards ajustados.</returns>
    public static async Task<List<string>> ExecutarAsync(ApplicationDbContext db, CancellationToken ct)
    {
        var etapasPorNome = await db.CrmLeadStages.AsNoTracking().Where(s => s.Ativa).ToDictionaryAsync(s => s.Nome.Trim(), s => s.Id, ct);
        var ids = await db.CrmLeads.AsNoTracking()
            .Where(l => !l.Arquivado && l.VeiculoAdicionalDeLeadId != null
                && l.Oportunidades.Any(o => !o.Arquivado && o.Etapa.Tipo == TipoEtapaPipeline.Ganho))
            .Select(l => l.Id)
            .ToListAsync(ct);

        var ajustados = new List<string>();
        foreach (var id in ids)
        {
            ct.ThrowIfCancellationRequested();
            var lead = await db.CrmLeads.Include(l => l.Oportunidades).FirstOrDefaultAsync(l => l.Id == id, ct);
            // Só o que ainda é venda de Lead; indicação (inclusive "Indicação Lead") já está certa.
            if (lead is null || !OpportunityService.EhOutroVeiculoDeLead(lead)) { db.ChangeTracker.Clear(); continue; }

            OpportunityService.ComoIndicacaoLeadDeOutroVeiculo(lead);
            NotionSyncService.TrocarColunaLeadIndicacao(lead, etapasPorNome);
            foreach (var venda in lead.Oportunidades.Where(o => !o.Arquivado)) venda.TipoIndicacao = TipoIndicacaoLead.IndicacaoLead;
            await db.SaveChangesAsync(ct);
            ajustados.Add(string.IsNullOrWhiteSpace(lead.Placa) ? lead.NomeOuRazaoSocial : lead.Placa!);
            db.ChangeTracker.Clear();
        }
        return ajustados;
    }

    private static async Task GravarAsync(ApplicationDbContext db, string chave, string valor, CancellationToken ct)
    {
        var existente = await db.CrmParametros.FirstOrDefaultAsync(p => p.Chave == chave, ct);
        if (existente is null) db.CrmParametros.Add(new CrmParametro { Chave = chave, Valor = valor });
        else existente.Valor = valor;
        await db.SaveChangesAsync(ct);
    }
}
