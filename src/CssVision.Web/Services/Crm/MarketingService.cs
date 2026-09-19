using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace CssVision.Web.Services.Crm;

/// <summary>
/// Painel de tráfego pago pro papel Marketing (e Admin) — visão sem escopo de carteira/equipe,
/// já que aqui o interesse é a base inteira por origem/campanha, não "meus leads".
/// </summary>
public sealed class MarketingService(ApplicationDbContext db) : IMarketingService
{
    /// <summary>Nome (prefixo) das colunas de venda ganha do quadro de leads — ver LeadsKanban.tsx/CrmSeeder.cs.</summary>
    private const string EtapaVendaConcluida = "Venda concluída";
    private const string EtapaPerdido = "Perdido";

    public async Task<MarketingDashboardDto> ObterAsync(MarketingFilterRequest filtro, CancellationToken ct)
    {
        var hoje = DateOnly.FromDateTime(DateTime.UtcNow);
        var inicioPeriodo = filtro.DataInicio ?? hoje.AddDays(-29);
        var fimPeriodo = filtro.DataFim ?? hoje;
        var inicioUtc = inicioPeriodo.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var fimUtc = fimPeriodo.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

        var query = db.CrmLeads.AsNoTracking()
            .Include(l => l.Etapa)
            .Include(l => l.Responsavel)
            .Where(l => !l.Arquivado && l.CriadoEm >= inicioUtc && l.CriadoEm <= fimUtc);

        if (!string.IsNullOrWhiteSpace(filtro.Origem)) query = query.Where(l => l.Origem == filtro.Origem);
        if (!string.IsNullOrWhiteSpace(filtro.Campanha)) query = query.Where(l => l.Campanha == filtro.Campanha);

        var leads = await query
            .Select(l => new
            {
                l.Id,
                l.NomeOuRazaoSocial,
                l.Telefone,
                l.Telefone2,
                l.Origem,
                l.Campanha,
                l.UtmSource,
                l.UtmMedium,
                EtapaNome = l.Etapa != null ? l.Etapa.Nome : null,
                ResponsavelNome = l.Responsavel != null ? l.Responsavel.NomeCompleto : null,
                l.CriadoEm,
                l.EtapaId,
            })
            .ToListAsync(ct);

        var totalLeads = leads.Count;
        var leadsSemEtapa = leads.Count(l => l.EtapaId is null);
        var ganhos = leads.Count(l => l.EtapaNome != null && l.EtapaNome.StartsWith(EtapaVendaConcluida));
        var perdidos = leads.Count(l => l.EtapaNome == EtapaPerdido);
        var taxaConversao = (ganhos + perdidos) == 0 ? 0m : Math.Round(100m * ganhos / (ganhos + perdidos), 1);
        var leadsSemContato = leads.Count(l => string.IsNullOrWhiteSpace(l.Telefone) && string.IsNullOrWhiteSpace(l.Telefone2));

        var indicadores = new MarketingIndicadoresDto(totalLeads, leadsSemEtapa, ganhos, perdidos, taxaConversao, leadsSemContato);

        var porOrigem = leads
            .GroupBy(l => string.IsNullOrWhiteSpace(l.Origem) ? "Não informado" : l.Origem)
            .Select(g =>
            {
                var qtdGanhos = g.Count(l => l.EtapaNome != null && l.EtapaNome.StartsWith(EtapaVendaConcluida));
                var qtdPerdidos = g.Count(l => l.EtapaNome == EtapaPerdido);
                var taxa = (qtdGanhos + qtdPerdidos) == 0 ? 0m : Math.Round(100m * qtdGanhos / (qtdGanhos + qtdPerdidos), 1);
                return new MarketingOrigemDto(g.Key, g.Count(), qtdGanhos, taxa);
            })
            .OrderByDescending(o => o.TotalLeads)
            .ToList();

        var porCampanha = leads
            .Where(l => !string.IsNullOrWhiteSpace(l.Campanha))
            .GroupBy(l => l.Campanha!)
            .Select(g =>
            {
                var qtdGanhos = g.Count(l => l.EtapaNome != null && l.EtapaNome.StartsWith(EtapaVendaConcluida));
                var qtdPerdidos = g.Count(l => l.EtapaNome == EtapaPerdido);
                var taxa = (qtdGanhos + qtdPerdidos) == 0 ? 0m : Math.Round(100m * qtdGanhos / (qtdGanhos + qtdPerdidos), 1);
                return new MarketingCampanhaDto(g.Key, g.First().Origem, g.Count(), qtdGanhos, taxa, g.Max(l => l.CriadoEm));
            })
            .OrderByDescending(c => c.TotalLeads)
            .Take(50)
            .ToList();

        var leadsPorDia = leads
            .GroupBy(l => DateOnly.FromDateTime(l.CriadoEm.UtcDateTime))
            .ToDictionary(g => g.Key, g => g.Count());
        var evolucao = new List<MarketingEvolucaoDto>();
        for (var dia = inicioPeriodo; dia <= fimPeriodo; dia = dia.AddDays(1))
        {
            evolucao.Add(new MarketingEvolucaoDto(dia.ToString("dd/MM"), leadsPorDia.GetValueOrDefault(dia)));
        }

        var origensDisponiveis = await db.CrmLeads.AsNoTracking()
            .Where(l => l.Origem != null && l.Origem != "")
            .Select(l => l.Origem!)
            .Distinct()
            .OrderBy(o => o)
            .ToListAsync(ct);

        var ultimosLeads = leads
            .OrderByDescending(l => l.CriadoEm)
            .Take(50)
            .Select(l => new MarketingLeadItemDto(
                l.Id, l.NomeOuRazaoSocial, l.Telefone, l.Origem, l.Campanha, l.UtmSource, l.UtmMedium,
                l.EtapaNome, l.ResponsavelNome, l.CriadoEm))
            .ToList();

        return new MarketingDashboardDto(indicadores, porOrigem, porCampanha, evolucao, origensDisponiveis, ultimosLeads);
    }
}
