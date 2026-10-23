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

    /// <summary>Nome da coluna do quadro de leads em que o consultor informa o valor da adesão.</summary>
    public const string EtapaCotacao = "Cotação";

    /// <summary>
    /// Leads em "Cotação" com valor de adesão informado e ainda sem oportunidade aberta (quem já tem oportunidade
    /// aberta entra pelo valor dela, sem contar duas vezes). A consulta de leads já vem filtrada pelo chamador.
    /// </summary>
    public static IQueryable<CrmLead> EmCotacaoComAdesao(IQueryable<CrmLead> leads) =>
        leads.Where(l => !l.Arquivado && l.Etapa != null && l.Etapa.Nome == EtapaCotacao && l.ValorAdesao > 0
            && !l.Oportunidades.Any(o => !o.Arquivado && o.Etapa.Tipo == TipoEtapaPipeline.Aberta));

    /// <summary>
    /// Oportunidades abertas e o valor em pipeline, por responsável. Com <paramref name="leads"/>, o valor soma também a adesão
    /// informada na "Cotação" do quadro de leads (a quantidade segue sendo só de oportunidades).
    /// </summary>
    public static async Task<Dictionary<Guid, (int Quantidade, decimal Valor)>> AbertasAsync(
        IQueryable<CrmOpportunity> oportunidades, CancellationToken ct, IQueryable<CrmLead>? leads = null)
    {
        var linhas = await oportunidades
            .Where(o => o.Etapa.Tipo == TipoEtapaPipeline.Aberta)
            .GroupBy(o => o.ResponsavelId)
            .Select(g => new { g.Key, Quantidade = g.Count(), Valor = g.Sum(o => (decimal?)o.ValorEstimado) ?? 0m })
            .ToListAsync(ct);
        var resultado = linhas.ToDictionary(x => x.Key, x => (x.Quantidade, x.Valor));
        if (leads is null) return resultado;

        var cotacoes = await EmCotacaoComAdesao(leads).Where(l => l.ResponsavelId != null)
            .GroupBy(l => l.ResponsavelId!.Value)
            .Select(g => new { g.Key, Valor = g.Sum(l => l.ValorAdesao ?? 0m) })
            .ToListAsync(ct);
        foreach (var c in cotacoes)
        {
            resultado.TryGetValue(c.Key, out var atual);
            resultado[c.Key] = (atual.Quantidade, atual.Valor + c.Valor);
        }
        return resultado;
    }

    /// <summary>
    /// Ganhas e perdidas por responsável. <paramref name="fechadas"/> já vem filtrada pelo período de
    /// fechamento; valor ganho = ValorFinal, ou Total/adesão quando ele é 0 (ou ValorEstimado), adesão = PagamentoAdesao.
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
                Valor = g.Sum(o => (decimal?)(o.ValorFinal > 0 ? o.ValorFinal.Value : o.Total > 0 ? o.Total.Value : o.PagamentoAdesao > 0 ? o.PagamentoAdesao.Value : o.ValorEstimado)) ?? 0m,
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

    /// <summary>
    /// Conversão = vendas fechadas no período ÷ leads que chegaram no período (limitada a 100%). A conta antiga
    /// (ganhas ÷ ganhas+perdidas das oportunidades) dava sempre 100%: os cards do Notion perdidos nunca viram
    /// oportunidade, só leads na coluna "Perdido".
    /// </summary>
    public static decimal TaxaConversaoLeads(int vendas, int leads) =>
        leads <= 0 ? 0m : Math.Min(100m, Math.Round(100m * vendas / leads, 1));

    public static decimal TaxaConversao(Fechadas? f) =>
        f is null || f.Ganhas + f.Perdidas == 0 ? 0m : Math.Round(100m * f.Ganhas / (f.Ganhas + f.Perdidas), 1);
}
