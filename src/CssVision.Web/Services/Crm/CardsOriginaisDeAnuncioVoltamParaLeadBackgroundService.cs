using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using Microsoft.EntityFrameworkCore;

namespace CssVision.Web.Services.Crm;

/// <summary>
/// Carga única: clientes do tráfego pago (card original, não "Outro veículo") que ficaram com a etiqueta "Indicação Lead" e na coluna "Venda concluída (Indicação)"
/// — a venda foi marcada como indicação na hora de concluir — voltam para "Venda concluída (Leads)" com a etiqueta "Lead", e a venda perde o canal "Indicação Lead".
/// Só os cards da lista abaixo (conferida com o time), e só se ainda estiverem exatamente nesse estado. Vai pelo servidor porque a edição do card exige cidade e
/// placa, que estes cards (vindos do Notion) não têm. A placa da Andreia (digitada errado no Notion) é corrigida pelo contrato. Roda uma vez (marcador em
/// CrmParametros); falha só vai para o log.
/// </summary>
public sealed class CardsOriginaisDeAnuncioVoltamParaLeadBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<CardsOriginaisDeAnuncioVoltamParaLeadBackgroundService> logger) : BackgroundService
{
    public const string ChaveExecutado = "crm:cards-de-anuncio-voltam-para-lead-em";
    public const string ChaveStatus = "notion:cards-de-anuncio-voltam-para-lead";

    private static readonly TimeSpan EsperaInicial = TimeSpan.FromMinutes(5);

    public static readonly Guid[] Cards =
    [
        Guid.Parse("f2b5ad7e-0c9f-4072-a714-3f2fd0775568"), // Francisco Santos EST8H78
        Guid.Parse("044d370d-7d9b-46d3-8732-6ce9ccbacbce"), // Andreia Biazi (65999957342)
        Guid.Parse("1e84e315-cf48-4bfe-8b8b-8d324696162c"), // Ronaldo Trindade FQE8198
        Guid.Parse("d760e823-b67e-4650-b6b6-9f85c35bb1c0"), // Naniconany DIY3C94
        Guid.Parse("b061ba6f-db70-47ae-bdd6-347d2229d874"), // Lucas Silva BAI8J88
        Guid.Parse("e9126a75-83ad-46f8-adaa-0c5fc60afe3f"), // Marina Leonel QQK5F96
        Guid.Parse("f63bd417-8644-4c95-a1aa-7fb9f0d0cb08"), // Benilson EVP2231
        Guid.Parse("6acfc212-bcb9-4c6f-b04f-98a69c100a04"), // Luis Silva
        Guid.Parse("4b85a7d6-45bd-4fa7-8d03-ecd382cad104"), // Vivian KVK1438
        Guid.Parse("f58099ac-8d95-4c15-a273-caf32ba77f11"), // William FBL2I86
    ];

    private static readonly Guid AndreiaId = Guid.Parse("044d370d-7d9b-46d3-8732-6ce9ccbacbce");
    private const string PlacaDaAndreia = "RAO9E76";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(EsperaInicial, stoppingToken);

            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Database.SetCommandTimeout(TimeSpan.FromSeconds(120));

            if (await db.CrmParametros.AsNoTracking().AnyAsync(p => p.Chave == ChaveExecutado, stoppingToken)) return;

            var corrigidos = await ExecutarAsync(db, Cards, stoppingToken);
            var resumo = $"{DateTimeOffset.UtcNow:O} | cards de anúncio de volta para Venda concluída (Leads): {corrigidos} de {Cards.Length}";
            await GravarAsync(db, ChaveStatus, resumo, stoppingToken);
            await GravarAsync(db, ChaveExecutado, resumo, stoppingToken);
            logger.LogInformation("Cards de anúncio voltaram para Lead: {Quantidade}.", corrigidos);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao devolver os cards de anúncio para Lead — o app segue; tenta de novo na próxima inicialização.");
        }
    }

    /// <returns>Quantos cards foram corrigidos.</returns>
    public static async Task<int> ExecutarAsync(ApplicationDbContext db, IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        var colunas = await db.CrmLeadStages.AsNoTracking()
            .Where(s => s.Nome == "Venda concluída (Leads)" || s.Nome == "Venda concluída (Indicação)").Select(s => new { s.Id, s.Nome }).ToListAsync(ct);
        var leads = colunas.FirstOrDefault(c => c.Nome == "Venda concluída (Leads)");
        var indicacao = colunas.FirstOrDefault(c => c.Nome == "Venda concluída (Indicação)");
        if (leads is null || indicacao is null) return 0;

        var cards = await db.CrmLeads.Where(l => ids.Contains(l.Id) && !l.Arquivado && l.VeiculoAdicionalDeLeadId == null && l.EtapaId == indicacao.Id
            && l.TipoIndicacao != null && l.TipoIndicacao.ToLower() == "indicação lead").ToListAsync(ct);
        var corrigidos = 0;
        foreach (var lead in cards)
        {
            var vendas = await db.CrmOpportunities.Where(o => o.LeadId == lead.Id && !o.Arquivado).ToListAsync(ct);
            if (vendas.Count != 1) continue; // só quem tem uma venda: com duas, uma delas é de outro veículo e precisa de decisão

            lead.TipoIndicacao = "Lead";
            lead.TipoIndicacaoEditadoNoCrm = true;
            lead.EtapaId = leads.Id;
            if (lead.Id == AndreiaId && string.IsNullOrWhiteSpace(lead.Placa)) lead.Placa = PlacaDaAndreia;
            foreach (var venda in vendas.Where(v => v.TipoIndicacao != null && v.TipoIndicacao.ToLower() == "indicação lead")) venda.TipoIndicacao = null;
            corrigidos++;
        }

        await db.SaveChangesAsync(ct);
        return corrigidos;
    }

    private static async Task GravarAsync(ApplicationDbContext db, string chave, string valor, CancellationToken ct)
    {
        var existente = await db.CrmParametros.FirstOrDefaultAsync(p => p.Chave == chave, ct);
        if (existente is null) db.CrmParametros.Add(new CrmParametro { Chave = chave, Valor = valor });
        else existente.Valor = valor;
        await db.SaveChangesAsync(ct);
    }
}
