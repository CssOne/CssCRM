using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using Microsoft.EntityFrameworkCore;

namespace CssVision.Web.Services.Crm;

public sealed class DashboardService(
    ApplicationDbContext db,
    IEquipeComercialService equipe,
    IActivityService activityService) : IDashboardService
{
    /// <summary>Leads sem nenhum contato há mais de N dias entram no alerta de "parados".</summary>
    private const int DiasSemContatoAlerta = 5;

    public async Task<DashboardDto> ObterAsync(DashboardFilterRequest filtro, CancellationToken ct)
    {
        var hoje = DateOnly.FromDateTime(DateTime.UtcNow);
        var inicioPeriodo = filtro.DataInicio ?? new DateOnly(hoje.Year, hoje.Month, 1);
        var fimPeriodo = filtro.DataFim ?? hoje;
        var inicioUtc = inicioPeriodo.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var fimUtc = fimPeriodo.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
        var agora = DateTimeOffset.UtcNow;

        var visiveis = await equipe.ObterVendedoresVisiveisAsync(ct);
        if (filtro.VendedorId.HasValue)
        {
            if (visiveis is not null && !visiveis.Contains(filtro.VendedorId.Value))
            {
                throw new Api.Contracts.Common.CrmForbiddenException();
            }
            visiveis = [filtro.VendedorId.Value];
        }

        var leadsQuery = db.CrmLeads.AsNoTracking().Where(l => !l.Arquivado);
        if (visiveis is not null) leadsQuery = leadsQuery.Where(l => l.ResponsavelId != null && visiveis.Contains(l.ResponsavelId.Value));

        var oportunidadesQuery = db.CrmOpportunities.AsNoTracking().Where(o => !o.Arquivado);
        if (visiveis is not null) oportunidadesQuery = oportunidadesQuery.Where(o => visiveis.Contains(o.ResponsavelId));

        var atividadesQuery = db.CrmActivities.AsNoTracking().Where(a => !a.Arquivado);
        if (visiveis is not null) atividadesQuery = atividadesQuery.Where(a => visiveis.Contains(a.ResponsavelId));

        var inicioHoje = hoje.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var fimHoje = hoje.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

        var novosLeads = await leadsQuery.CountAsync(l => l.CriadoEm >= inicioUtc && l.CriadoEm <= fimUtc, ct);
        var leadsSemContato = await leadsQuery.CountAsync(l => l.UltimoContatoEm == null, ct);
        var contatosHoje = await atividadesQuery.CountAsync(a =>
            a.Status == StatusAtividade.Pendente && a.DataHoraPrevista >= inicioHoje && a.DataHoraPrevista <= fimHoje, ct);
        var atividadesAtrasadas = await atividadesQuery.CountAsync(a => a.Status == StatusAtividade.Pendente && a.DataHoraPrevista < agora, ct);

        var abertas = oportunidadesQuery.Where(o => o.Etapa.Tipo == TipoEtapaPipeline.Aberta);
        var oportunidadesAbertas = await abertas.CountAsync(ct);
        var valorPipeline = await abertas.SumAsync(o => (decimal?)o.ValorEstimado, ct) ?? 0m;

        var fechadasNoPeriodo = oportunidadesQuery.Where(o =>
            o.Etapa.Tipo != TipoEtapaPipeline.Aberta && o.DataEfetivaFechamento >= inicioUtc && o.DataEfetivaFechamento <= fimUtc);
        var ganhas = fechadasNoPeriodo.Where(o => o.Etapa.Tipo == TipoEtapaPipeline.Ganho);
        var perdidas = fechadasNoPeriodo.Where(o => o.Etapa.Tipo == TipoEtapaPipeline.Perdido);

        var qtdGanhas = await ganhas.CountAsync(ct);
        var qtdPerdidas = await perdidas.CountAsync(ct);
        var valorGanho = await ganhas.SumAsync(o => (decimal?)(o.ValorFinal ?? o.ValorEstimado), ct) ?? 0m;
        var taxaConversao = (qtdGanhas + qtdPerdidas) == 0 ? 0m : Math.Round(100m * qtdGanhas / (qtdGanhas + qtdPerdidas), 1);
        var ticketMedio = qtdGanhas == 0 ? 0m : Math.Round(valorGanho / qtdGanhas, 2);

        var indicadores = new DashboardIndicadoresDto(
            novosLeads, leadsSemContato, contatosHoje, atividadesAtrasadas,
            oportunidadesAbertas, valorPipeline, taxaConversao, ticketMedio, valorGanho, qtdGanhas);

        // Meta comercial do mês corrente para os vendedores visíveis.
        var mesReferencia = new DateOnly(hoje.Year, hoje.Month, 1);
        var metaQuery = db.CrmSalesGoals.AsNoTracking().Where(g => g.MesReferencia == mesReferencia);
        if (visiveis is not null) metaQuery = metaQuery.Where(g => visiveis.Contains(g.VendedorId));
        var metaValor = await metaQuery.SumAsync(g => (decimal?)g.MetaValor, ct) ?? 0m;
        var realizadoMes = await oportunidadesQuery
            .Where(o => o.Etapa.Tipo == TipoEtapaPipeline.Ganho &&
                        o.DataEfetivaFechamento >= mesReferencia.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc))
            .SumAsync(o => (decimal?)(o.ValorFinal ?? o.ValorEstimado), ct) ?? 0m;
        var meta = new MetaResultadoDto(metaValor, realizadoMes, metaValor == 0 ? 0m : Math.Round(100m * realizadoMes / metaValor, 1));

        var funilBruto = await oportunidadesQuery
            .Where(o => o.Etapa.Tipo == TipoEtapaPipeline.Aberta)
            .GroupBy(o => new { o.Etapa.Nome, o.Etapa.Ordem })
            .Select(g => new { g.Key.Nome, g.Key.Ordem, Quantidade = g.Count(), Valor = g.Sum(o => o.ValorEstimado) })
            .OrderBy(g => g.Ordem)
            .ToListAsync(ct);
        var funil = funilBruto.Select(f => new FunilEtapaDto(f.Nome, f.Quantidade, f.Valor)).ToList();

        var evolucao = await ObterEvolucaoVendasAsync(oportunidadesQuery, hoje, ct);

        var origemBruto = await leadsQuery
            .Where(l => l.Origem != null)
            .GroupBy(l => l.Origem)
            .Select(g => new { Origem = g.Key!, Quantidade = g.Count() })
            .OrderByDescending(g => g.Quantidade)
            .ToListAsync(ct);
        var origens = origemBruto.Select(o => new OrigemLeadDto(o.Origem, o.Quantidade)).ToList();

        var desempenho = await ObterDesempenhoPorVendedorAsync(visiveis, inicioUtc, fimUtc, ct);

        var atividadesDoDiaPagina = await activityService.ListarAsync(
            new ActivityFilterRequest { Visao = VisaoAtividade.Hoje, TamanhoPagina = 20 }, ct);

        var leadsParados = await ObterLeadsParadosAsync(leadsQuery, agora, ct);

        return new DashboardDto(indicadores, meta, funil, evolucao, origens, desempenho, atividadesDoDiaPagina.Itens, leadsParados);
    }

    private static async Task<List<EvolucaoVendasDto>> ObterEvolucaoVendasAsync(IQueryable<CrmOpportunity> oportunidadesQuery, DateOnly hoje, CancellationToken ct)
    {
        var inicioJanela = new DateOnly(hoje.Year, hoje.Month, 1).AddMonths(-5);
        var inicioJanelaUtc = inicioJanela.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var fechamentos = await oportunidadesQuery
            .Where(o => o.Etapa.Tipo == TipoEtapaPipeline.Ganho && o.DataEfetivaFechamento >= inicioJanelaUtc)
            .Select(o => new { o.DataEfetivaFechamento, Valor = o.ValorFinal ?? o.ValorEstimado })
            .ToListAsync(ct);

        var resultado = new List<EvolucaoVendasDto>();
        for (var i = 5; i >= 0; i--)
        {
            var mes = new DateOnly(hoje.Year, hoje.Month, 1).AddMonths(-i);
            var doMes = fechamentos.Where(f => f.DataEfetivaFechamento!.Value.Year == mes.Year && f.DataEfetivaFechamento.Value.Month == mes.Month).ToList();
            resultado.Add(new EvolucaoVendasDto(mes.ToString("MM/yyyy"), doMes.Sum(f => f.Valor), doMes.Count));
        }

        return resultado;
    }

    private async Task<List<DesempenhoVendedorDto>> ObterDesempenhoPorVendedorAsync(
        List<Guid>? visiveis, DateTimeOffset inicioUtc, DateTimeOffset fimUtc, CancellationToken ct)
    {
        var vendedoresQuery = db.Users.AsNoTracking().AsQueryable();
        if (visiveis is not null) vendedoresQuery = vendedoresQuery.Where(u => visiveis.Contains(u.Id));

        var vendedores = await vendedoresQuery.Select(u => new { u.Id, u.NomeCompleto }).ToListAsync(ct);
        var resultado = new List<DesempenhoVendedorDto>();

        foreach (var vendedor in vendedores)
        {
            var leads = await db.CrmLeads.AsNoTracking().CountAsync(l => l.ResponsavelId == vendedor.Id && !l.Arquivado, ct);
            var oportunidades = db.CrmOpportunities.AsNoTracking().Where(o => o.ResponsavelId == vendedor.Id && !o.Arquivado);
            var abertas = await oportunidades.Where(o => o.Etapa.Tipo == TipoEtapaPipeline.Aberta).CountAsync(ct);
            var valorPipeline = await oportunidades.Where(o => o.Etapa.Tipo == TipoEtapaPipeline.Aberta).SumAsync(o => (decimal?)o.ValorEstimado, ct) ?? 0m;

            var fechadasPeriodo = oportunidades.Where(o => o.Etapa.Tipo != TipoEtapaPipeline.Aberta &&
                o.DataEfetivaFechamento >= inicioUtc && o.DataEfetivaFechamento <= fimUtc);
            var ganhas = await fechadasPeriodo.CountAsync(o => o.Etapa.Tipo == TipoEtapaPipeline.Ganho, ct);
            var perdidas = await fechadasPeriodo.CountAsync(o => o.Etapa.Tipo == TipoEtapaPipeline.Perdido, ct);
            var valorGanho = await fechadasPeriodo.Where(o => o.Etapa.Tipo == TipoEtapaPipeline.Ganho)
                .SumAsync(o => (decimal?)(o.ValorFinal ?? o.ValorEstimado), ct) ?? 0m;
            var taxa = (ganhas + perdidas) == 0 ? 0m : Math.Round(100m * ganhas / (ganhas + perdidas), 1);

            resultado.Add(new DesempenhoVendedorDto(vendedor.Id, vendedor.NomeCompleto, leads, abertas, valorPipeline, ganhas, valorGanho, taxa));
        }

        return resultado.OrderByDescending(d => d.ValorGanho).ToList();
    }

    private static async Task<List<AlertaLeadParadoDto>> ObterLeadsParadosAsync(IQueryable<CrmLead> leadsQuery, DateTimeOffset agora, CancellationToken ct)
    {
        var limite = agora.AddDays(-DiasSemContatoAlerta);

        var parados = await leadsQuery
            .Include(l => l.Responsavel)
            .Where(l => l.Status != StatusLead.Convertido && l.Status != StatusLead.Descartado)
            .Where(l => l.UltimoContatoEm == null ? l.CriadoEm < limite : l.UltimoContatoEm < limite)
            .OrderBy(l => l.UltimoContatoEm ?? l.CriadoEm)
            .Take(15)
            .Select(l => new { l.Id, l.NomeOuRazaoSocial, ResponsavelNome = l.Responsavel != null ? l.Responsavel.NomeCompleto : null, l.UltimoContatoEm, l.CriadoEm })
            .ToListAsync(ct);

        return parados.Select(l => new AlertaLeadParadoDto(
            l.Id, l.NomeOuRazaoSocial, l.ResponsavelNome,
            (int)(agora - (l.UltimoContatoEm ?? l.CriadoEm)).TotalDays)).ToList();
    }
}
