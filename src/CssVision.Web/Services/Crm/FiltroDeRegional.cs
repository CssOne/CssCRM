using CssVision.Web.Domain.Crm;

namespace CssVision.Web.Services.Crm;

/// <summary>
/// Filtro de regional dos leads (quadro de leads e lista). Todo lead pertence a uma regional (hoje MG132 ou MG134), definida assim:
/// <list type="number">
/// <item>a regional escrita no próprio lead, ignorando espaços e maiúsculas ("MG 132" = "MG132"). Também vale o nome de um <b>grupo</b>
/// ("CSS Growth Sales", "Consultores Externos", ou "MG134 Consultores Externos"): eram regionais antes de virarem grupos, e o lead que
/// ainda carregar esse texto é da regional do grupo;</item>
/// <item>se o texto do lead não é regional nem grupo nenhum (vazio, "132"...), a do <b>consultor responsável</b> — a mesma regra do
/// Tráfego pago por regional;</item>
/// <item>sem nenhuma das duas (lead ainda sem responsável, ou de alguém sem regional), o lead é do <b>rodízio geral</b>: pertence a qualquer
/// regional que não seja exclusiva (<see cref="DistribuicaoOptions.RegionaisExclusivas"/>), como na distribuição de leads.</item>
/// </list>
/// Antes o filtro só comparava o texto do lead: os leads de tráfego pago, que chegam sem regional e vão para o consultor pelo rodízio,
/// sumiam ao escolher a regional. Quem tem regional própria nunca cai na do consultor: um lead marcado MG134 e atendido por alguém da
/// MG132 continua sendo da MG134.
/// </summary>
public static class FiltroDeRegional
{
    /// <param name="cadastradas">As regionais cadastradas (<c>db.CrmRegionais</c>).</param>
    /// <param name="grupos">Os grupos cadastrados (<c>db.CrmGrupos</c>): o nome de um grupo no lead vale como a regional dele.</param>
    /// <param name="regionais">As escolhidas no filtro.</param>
    /// <param name="regionaisExclusivas">As regionais exclusivas do rodízio (config), que não recebem lead sem regional.</param>
    public static IQueryable<CrmLead> Aplicar(
        IQueryable<CrmLead> query, IQueryable<CrmRegional> cadastradas, IQueryable<CrmGrupo> grupos,
        IEnumerable<string?> regionais, IEnumerable<string?>? regionaisExclusivas = null)
    {
        var alvo = Normalizados(regionais);
        if (alvo.Count == 0) return query;

        // Lead sem regional nenhuma só entra se alguma das escolhidas não for exclusiva (a exclusiva só recebe o que vem marcado com ela).
        var exclusivas = Normalizados(regionaisExclusivas ?? []).ToHashSet();
        var incluirSemRegional = alvo.Any(r => !exclusivas.Contains(r));

        // (Os textos são comparados sem espaços e em maiúsculas; o EF traduz Replace/ToUpper para replace()/upper() no Postgres.)
        return query.Where(l =>
            // 1) regional do próprio lead: o nome de uma regional escolhida, ou o nome de um grupo de uma regional escolhida
            (l.Regional != null && (
                alvo.Contains(l.Regional.Replace(" ", "").ToUpper())
                || grupos.Any(g => alvo.Contains(g.Regional.Nome.Replace(" ", "").ToUpper())
                    && (g.Nome.Replace(" ", "").ToUpper() == l.Regional.Replace(" ", "").ToUpper()
                        || (g.Regional.Nome + g.Nome).Replace(" ", "").ToUpper() == l.Regional.Replace(" ", "").ToUpper()))))
            || (
                // o texto do lead não é regional nem grupo de nenhuma regional (vazio, "132"...)
                (l.Regional == null
                    || (!cadastradas.Any(r => r.Nome.Replace(" ", "").ToUpper() == l.Regional.Replace(" ", "").ToUpper())
                        && !grupos.Any(g => g.Nome.Replace(" ", "").ToUpper() == l.Regional.Replace(" ", "").ToUpper()
                            || (g.Regional.Nome + g.Nome).Replace(" ", "").ToUpper() == l.Regional.Replace(" ", "").ToUpper())))
                && (
                    // 2) a do consultor responsável
                    (l.Responsavel != null && l.Responsavel.Regional != null && alvo.Contains(l.Responsavel.Regional.Nome.Replace(" ", "").ToUpper()))
                    // 3) sem nenhuma das duas: rodízio geral
                    || (incluirSemRegional && (l.Responsavel == null || l.Responsavel.Regional == null)))));
    }

    private static List<string> Normalizados(IEnumerable<string?> nomes) =>
        nomes.Select(NomeDeRegional.Normalizar).Where(n => n is not null).Select(n => n!).Distinct().ToList();
}
