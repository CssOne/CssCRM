using CssVision.Web.Domain.Crm;
using Microsoft.EntityFrameworkCore;

namespace CssVision.Web.Services.Crm;

/// <summary>
/// Números por vendedor calculados de uma vez, agrupados no banco (uma consulta por métrica para
/// todos os vendedores). Substitui os laços "para cada vendedor, N consultas", que com ~80 usuários
/// viravam centenas de consultas por tela e travavam o banco com várias pessoas usando ao mesmo tempo.
/// </summary>
public static class ContagensPorVendedor
{
    public sealed record Fechadas(int Ganhas, int Perdidas, decimal ValorGanho, decimal ValorAdesao);

    /// <summary>Quantidade de leads por responsável (a consulta já vem filtrada pelo chamador).</summary>
    public static Task<Dictionary<Guid, int>> ContarLeadsAsync(IQueryable<CrmLead> leads, CancellationToken ct) =>
        leads.Where(l => l.ResponsavelId != null)
            .GroupBy(l => l.ResponsavelId!.Value)
            .Select(g => new { g.Key, Quantidade = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Quantidade, ct);

    /// <summary>Oportunidades abertas e o valor estimado delas, por responsável.</summary>
    public static async Task<Dictionary<Guid, (int Quantidade, decimal Valor)>> AbertasAsync(IQueryable<CrmOpportunity> oportunidades, CancellationToken ct)
    {
        var linhas = await oportunidades
            .Where(o => o.Etapa.Tipo == TipoEtapaPipeline.Aberta)
            .GroupBy(o => o.ResponsavelId)
            .Select(g => new { g.Key, Quantidade = g.Count(), Valor = g.Sum(o => (decimal?)o.ValorEstimado) ?? 0m })
            .ToListAsync(ct);
        return linhas.ToDictionary(x => x.Key, x => (x.Quantidade, x.Valor));
    }

    /// <summary>
    /// Ganhas e perdidas por responsável. <paramref name="fechadas"/> já vem filtrada pelo período de
    /// fechamento; valor ganho = ValorFinal (ou ValorEstimado), adesão = PagamentoAdesao.
    /// </summary>
    public static async Task<Dictionary<Guid, Fechadas>> FechadasAsync(IQueryable<CrmOpportunity> fechadas, CancellationToken ct)
    {
        var linhas = await fechadas
            .Where(o => o.Etapa.Tipo == TipoEtapaPipeline.Ganho || o.Etapa.Tipo == TipoEtapaPipeline.Perdido)
            .GroupBy(o => new { o.ResponsavelId, o.Etapa.Tipo })
            .Select(g => new
            {
                g.Key.ResponsavelId,
                g.Key.Tipo,
                Quantidade = g.Count(),
                Valor = g.Sum(o => (decimal?)(o.ValorFinal ?? o.ValorEstimado)) ?? 0m,
                Adesao = g.Sum(o => o.PagamentoAdesao) ?? 0m,
            })
            .ToListAsync(ct);

        return linhas
            .GroupBy(x => x.ResponsavelId)
            .ToDictionary(g => g.Key, g =>
            {
                var ganho = g.FirstOrDefault(x => x.Tipo == TipoEtapaPipeline.Ganho);
                var perdido = g.FirstOrDefault(x => x.Tipo == TipoEtapaPipeline.Perdido);
                return new Fechadas(ganho?.Quantidade ?? 0, perdido?.Quantidade ?? 0, ganho?.Valor ?? 0m, ganho?.Adesao ?? 0m);
            });
    }

    public static decimal TaxaConversao(Fechadas? f) =>
        f is null || f.Ganhas + f.Perdidas == 0 ? 0m : Math.Round(100m * f.Ganhas / (f.Ganhas + f.Perdidas), 1);
}
