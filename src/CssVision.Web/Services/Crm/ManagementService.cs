using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Authorization;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Domain.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CssVision.Web.Services.Crm;

public sealed class ManagementService(
    ApplicationDbContext db,
    ICurrentUserService currentUser,
    IEquipeComercialService equipe,
    UserManager<ApplicationUser> userManager) : IManagementService
{
    /// <summary>Oportunidades abertas sem troca de etapa há mais de N dias entram no alerta de estagnação.</summary>
    private const int DiasSemMovimentacaoAlerta = 10;

    public async Task<GestaoComercialResumoDto> ObterResumoAsync(DateOnly? dataInicio, DateOnly? dataFim, CancellationToken ct)
    {
        ExigirGestaoComercial();

        var hoje = DateOnly.FromDateTime(DateTime.UtcNow);
        var inicio = (dataInicio ?? hoje.AddMonths(-1)).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var fim = (dataFim ?? hoje).ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
        var agora = DateTimeOffset.UtcNow;

        var visiveis = await equipe.ObterVendedoresVisiveisAsync(ct);

        var leadsQuery = db.CrmLeads.AsNoTracking().Where(l => !l.Arquivado);
        var oportunidadesQuery = db.CrmOpportunities.AsNoTracking().Where(o => !o.Arquivado);
        if (visiveis is not null)
        {
            leadsQuery = leadsQuery.Where(l => l.ResponsavelId != null && visiveis.Contains(l.ResponsavelId.Value));
            oportunidadesQuery = oportunidadesQuery.Where(o => visiveis.Contains(o.ResponsavelId));
        }

        var tempoMedioContatoHoras = await ObterTempoMedioPrimeiroContatoAsync(leadsQuery, ct);
        var tempoMedioPorEtapa = await ObterTempoMedioPorEtapaAsync(oportunidadesQuery, ct);
        var oportunidadesSemMovimentacao = await ObterOportunidadesSemMovimentacaoAsync(oportunidadesQuery, agora, ct);
        var ranking = await ObterRankingAsync(visiveis, inicio, fim, ct);
        var motivosPerda = await ObterMotivosPerdaAsync(oportunidadesQuery, inicio, fim, ct);

        return new GestaoComercialResumoDto(tempoMedioContatoHoras, tempoMedioPorEtapa, oportunidadesSemMovimentacao, ranking, motivosPerda);
    }

    public async Task<IReadOnlyList<VendedorResumoDto>> ObterVendedoresAsync(CancellationToken ct, bool incluirInativos = false)
    {
        ExigirGestaoComercial();

        var visiveis = await equipe.ObterVendedoresVisiveisAsync(ct);
        var query = db.Users.AsNoTracking();
        if (incluirInativos)
        {
            // Inativos: só quem é vendedor (papel Comercial) — os ativos seguem como sempre.
            var comercial = db.UserRoles.Join(db.Roles.Where(r => r.Name == Roles.Comercial), ur => ur.RoleId, r => r.Id, (ur, _) => ur.UserId);
            query = query.Where(u => u.Ativo || comercial.Contains(u.Id));
        }
        else
        {
            query = query.Where(u => u.Ativo);
        }
        if (visiveis is not null) query = query.Where(u => visiveis.Contains(u.Id));

        var vendedores = await query
            .OrderByDescending(u => u.Ativo).ThenBy(u => u.NomeCompleto)
            .Select(u => new { u.Id, u.NomeCompleto, u.LimiteMensalLeads, u.LimiteDiarioLeads, u.Ativo, u.RecebeLeads })
            .ToListAsync(ct);
        var resultado = new List<VendedorResumoDto>();
        var inicioMes = new DateTimeOffset(new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1), TimeSpan.Zero);
        var inicioDia = LeadAssignmentService.InicioDoDia();

        foreach (var v in vendedores)
        {
            var leadsAtivos = await db.CrmLeads.CountAsync(l => l.ResponsavelId == v.Id && !l.Arquivado, ct);
            var abertas = await db.CrmOpportunities.CountAsync(o => o.ResponsavelId == v.Id && !o.Arquivado && o.Etapa.Tipo == TipoEtapaPipeline.Aberta, ct);
            // Mesma contagem dos limites: só leads do tráfego pago (ver LeadAssignmentService).
            var doTrafego = db.CrmLeads.Where(OrigemLead.VeioDoTrafegoPago).Where(l => l.ResponsavelId == v.Id);
            var recebidosNoMes = await doTrafego.CountAsync(l => l.CriadoEm >= inicioMes, ct);
            var recebidosHoje = await doTrafego.CountAsync(l => (l.ResponsavelAtribuidoEm ?? l.CriadoEm) >= inicioDia, ct);
            resultado.Add(new VendedorResumoDto(v.Id, v.NomeCompleto, leadsAtivos, abertas, v.LimiteMensalLeads, recebidosNoMes, v.LimiteDiarioLeads, recebidosHoje, v.Ativo, v.RecebeLeads));
        }

        return resultado;
    }

    public async Task<IReadOnlyList<ConsultorDesempenhoDto>> ObterDesempenhoConsultoresAsync(DateOnly? mesReferencia, CancellationToken ct)
    {
        ExigirGestaoComercial();

        var hoje = DateOnly.FromDateTime(DateTime.UtcNow);
        var mes = new DateOnly((mesReferencia ?? hoje).Year, (mesReferencia ?? hoje).Month, 1);
        var inicioMes = mes.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var fimMes = mes.AddMonths(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var visiveis = await equipe.ObterVendedoresVisiveisAsync(ct);
        var idsComPapelComercial = (await userManager.GetUsersInRoleAsync(Roles.Comercial)).Select(u => u.Id).ToHashSet();

        var query = db.Users.AsNoTracking().Include(u => u.Regional).Where(u => idsComPapelComercial.Contains(u.Id));
        if (visiveis is not null) query = query.Where(u => visiveis.Contains(u.Id));

        var consultores = await query.ToListAsync(ct);

        var metas = await db.CrmSalesGoals.AsNoTracking()
            .Where(g => g.MesReferencia == mes)
            .ToDictionaryAsync(g => g.VendedorId, ct);

        var resultado = new List<ConsultorDesempenhoDto>();
        foreach (var c in consultores)
        {
            var leadsAtivos = await db.CrmLeads.CountAsync(l => l.ResponsavelId == c.Id && !l.Arquivado, ct);
            var oportunidades = db.CrmOpportunities.AsNoTracking().Where(o => o.ResponsavelId == c.Id && !o.Arquivado);

            var abertas = await oportunidades.Where(o => o.Etapa.Tipo == TipoEtapaPipeline.Aberta).CountAsync(ct);
            var valorPipeline = await oportunidades.Where(o => o.Etapa.Tipo == TipoEtapaPipeline.Aberta).SumAsync(o => (decimal?)o.ValorEstimado, ct) ?? 0m;

            var fechadasNoMes = oportunidades.Where(o =>
                o.Etapa.Tipo != TipoEtapaPipeline.Aberta && o.DataEfetivaFechamento >= inicioMes && o.DataEfetivaFechamento < fimMes);
            var ganhas = await fechadasNoMes.CountAsync(o => o.Etapa.Tipo == TipoEtapaPipeline.Ganho, ct);
            var perdidas = await fechadasNoMes.CountAsync(o => o.Etapa.Tipo == TipoEtapaPipeline.Perdido, ct);
            var valorGanho = await fechadasNoMes.Where(o => o.Etapa.Tipo == TipoEtapaPipeline.Ganho)
                .SumAsync(o => (decimal?)(o.ValorFinal ?? o.ValorEstimado), ct) ?? 0m;
            var taxa = (ganhas + perdidas) == 0 ? 0m : Math.Round(100m * ganhas / (ganhas + perdidas), 1);

            var recebidosNoMes = await db.CrmLeads.Where(OrigemLead.VeioDoTrafegoPago).CountAsync(l => l.ResponsavelId == c.Id && l.CriadoEm >= inicioMes, ct);

            metas.TryGetValue(c.Id, out var meta);
            var metaValor = meta?.MetaValor ?? 0m;
            var percentualMeta = metaValor == 0 ? 0m : Math.Round(100m * valorGanho / metaValor, 1);

            resultado.Add(new ConsultorDesempenhoDto(
                c.Id, c.NomeCompleto, c.Email!, c.PhoneNumber, c.Regional?.Nome, c.Ativo,
                leadsAtivos, abertas, valorPipeline, ganhas, valorGanho, taxa,
                c.LimiteMensalLeads, recebidosNoMes, metaValor, valorGanho, percentualMeta));
        }

        return resultado.OrderByDescending(r => r.ValorGanho).ToList();
    }

    public async Task<IReadOnlyList<RedistribuicaoHistoricoDto>> ObterHistoricoRedistribuicoesAsync(CancellationToken ct)
    {
        ExigirGestaoComercial();

        var visiveis = await equipe.ObterVendedoresVisiveisAsync(ct);
        var query = db.CrmLeadAssignmentHistories.AsNoTracking()
            .Include(h => h.Lead)
            .Include(h => h.ResponsavelAnterior)
            .Include(h => h.ResponsavelNovo)
            .Include(h => h.AlteradoPor)
            .AsQueryable();

        if (visiveis is not null) query = query.Where(h => visiveis.Contains(h.ResponsavelNovoId));

        var historico = await query.OrderByDescending(h => h.AlteradoEm).Take(100).ToListAsync(ct);

        return historico.Select(h => new RedistribuicaoHistoricoDto(
            h.LeadId, h.Lead.NomeOuRazaoSocial, h.ResponsavelAnterior?.NomeCompleto, h.ResponsavelNovo.NomeCompleto,
            h.AlteradoPor.NomeCompleto, h.Motivo, h.AlteradoEm)).ToList();
    }

    public async Task AtualizarLimiteMensalAsync(Guid vendedorId, AtualizarLimiteMensalRequest request, CancellationToken ct)
    {
        ExigirGestaoComercial();

        if (!await equipe.PodeAcessarVendedorAsync(vendedorId, ct))
        {
            throw new CrmForbiddenException("Você não pode alterar o limite deste vendedor.");
        }

        if (request.Limite is < 0)
        {
            throw new CrmBusinessException("O limite mensal não pode ser negativo.", "limite_invalido");
        }

        var vendedor = await db.Users.FirstOrDefaultAsync(u => u.Id == vendedorId, ct)
            ?? throw new CrmNotFoundException("Vendedor", vendedorId);

        vendedor.LimiteMensalLeads = request.Limite;
        await db.SaveChangesAsync(ct);
    }

    public async Task AtualizarLimiteDiarioAsync(Guid vendedorId, AtualizarLimiteDiarioRequest request, CancellationToken ct)
    {
        ExigirGestaoComercial();

        if (!await equipe.PodeAcessarVendedorAsync(vendedorId, ct))
        {
            throw new CrmForbiddenException("Você não pode alterar o limite deste vendedor.");
        }

        if (request.Limite is < 0)
        {
            throw new CrmBusinessException("O limite diário não pode ser negativo.", "limite_invalido");
        }

        var vendedor = await db.Users.FirstOrDefaultAsync(u => u.Id == vendedorId, ct)
            ?? throw new CrmNotFoundException("Vendedor", vendedorId);

        vendedor.LimiteDiarioLeads = request.Limite;
        await db.SaveChangesAsync(ct);
    }

    public async Task AtualizarRecebeLeadsAsync(Guid vendedorId, AtualizarRecebeLeadsRequest request, CancellationToken ct)
    {
        ExigirGestaoComercial();

        if (!await equipe.PodeAcessarVendedorAsync(vendedorId, ct))
        {
            throw new CrmForbiddenException("Você não pode alterar este vendedor.");
        }

        var vendedor = await db.Users.FirstOrDefaultAsync(u => u.Id == vendedorId, ct)
            ?? throw new CrmNotFoundException("Vendedor", vendedorId);

        vendedor.RecebeLeads = request.RecebeLeads;
        await db.SaveChangesAsync(ct);
    }

    // --- auxiliares ---

    private void ExigirGestaoComercial()
    {
        if (!currentUser.PodeGerirComercial)
        {
            throw new Api.Contracts.Common.CrmForbiddenException("Apenas gestores comerciais podem acessar a gestão comercial.");
        }
    }

    private static async Task<double> ObterTempoMedioPrimeiroContatoAsync(IQueryable<CrmLead> leadsQuery, CancellationToken ct)
    {
        var leadIds = await leadsQuery.Select(l => l.Id).ToListAsync(ct);
        if (leadIds.Count == 0) return 0;

        var dados = await leadsQuery
            .Select(l => new
            {
                l.Id,
                l.CriadoEm,
                PrimeiroContato = l.Atividades
                    .Where(a => a.Status == StatusAtividade.Concluida)
                    .OrderBy(a => a.DataHoraConclusao)
                    .Select(a => a.DataHoraConclusao)
                    .FirstOrDefault()
            })
            .Where(x => x.PrimeiroContato != null)
            .ToListAsync(ct);

        if (dados.Count == 0) return 0;

        var horas = dados.Select(d => (d.PrimeiroContato!.Value - d.CriadoEm).TotalHours).ToList();
        return Math.Round(horas.Average(), 1);
    }

    private static async Task<List<TempoMedioEtapaDto>> ObterTempoMedioPorEtapaAsync(IQueryable<CrmOpportunity> oportunidadesQuery, CancellationToken ct)
    {
        var opportunityIds = await oportunidadesQuery.Select(o => o.Id).ToListAsync(ct);
        if (opportunityIds.Count == 0) return [];

        var historico = await oportunidadesQuery
            .SelectMany(o => o.HistoricoEtapas)
            .Select(h => new { h.OpportunityId, h.EtapaNova.Nome, h.AlteradoEm })
            .ToListAsync(ct);

        var agora = DateTimeOffset.UtcNow;
        var duracoes = new List<(string Etapa, double Dias)>();

        foreach (var grupo in historico.GroupBy(h => h.OpportunityId))
        {
            var ordenado = grupo.OrderBy(h => h.AlteradoEm).ToList();
            for (var i = 0; i < ordenado.Count; i++)
            {
                var fim = i + 1 < ordenado.Count ? ordenado[i + 1].AlteradoEm : agora;
                duracoes.Add((ordenado[i].Nome, (fim - ordenado[i].AlteradoEm).TotalDays));
            }
        }

        return duracoes
            .GroupBy(d => d.Etapa)
            .Select(g => new TempoMedioEtapaDto(g.Key, Math.Round(g.Average(x => x.Dias), 1)))
            .ToList();
    }

    private static async Task<List<OportunidadeParadaDto>> ObterOportunidadesSemMovimentacaoAsync(
        IQueryable<CrmOpportunity> oportunidadesQuery, DateTimeOffset agora, CancellationToken ct)
    {
        var limite = agora.AddDays(-DiasSemMovimentacaoAlerta);

        var paradas = await oportunidadesQuery
            .Include(o => o.Lead)
            .Include(o => o.Etapa)
            .Include(o => o.Responsavel)
            .Where(o => o.Etapa.Tipo == TipoEtapaPipeline.Aberta && o.EtapaDesde < limite)
            .OrderBy(o => o.EtapaDesde)
            .Take(20)
            .Select(o => new { o.Id, LeadNome = o.Lead.NomeOuRazaoSocial, EtapaNome = o.Etapa.Nome, ResponsavelNome = o.Responsavel.NomeCompleto, o.EtapaDesde, o.ValorEstimado })
            .ToListAsync(ct);

        return paradas.Select(o => new OportunidadeParadaDto(
            o.Id, o.LeadNome, o.EtapaNome, o.ResponsavelNome, (int)(agora - o.EtapaDesde).TotalDays, o.ValorEstimado)).ToList();
    }

    private async Task<List<RankingComercialDto>> ObterRankingAsync(List<Guid>? visiveis, DateTimeOffset inicio, DateTimeOffset fim, CancellationToken ct)
    {
        var vendedoresQuery = db.Users.AsNoTracking().Where(u => u.Ativo);
        if (visiveis is not null) vendedoresQuery = vendedoresQuery.Where(u => visiveis.Contains(u.Id));
        var vendedores = await vendedoresQuery.Select(u => new { u.Id, u.NomeCompleto }).ToListAsync(ct);

        var resultado = new List<RankingComercialDto>();
        foreach (var v in vendedores)
        {
            var fechadas = db.CrmOpportunities.AsNoTracking().Where(o => o.ResponsavelId == v.Id && !o.Arquivado &&
                o.Etapa.Tipo != TipoEtapaPipeline.Aberta && o.DataEfetivaFechamento >= inicio && o.DataEfetivaFechamento <= fim);
            var ganhas = await fechadas.CountAsync(o => o.Etapa.Tipo == TipoEtapaPipeline.Ganho, ct);
            var perdidas = await fechadas.CountAsync(o => o.Etapa.Tipo == TipoEtapaPipeline.Perdido, ct);
            var valorGanho = await fechadas.Where(o => o.Etapa.Tipo == TipoEtapaPipeline.Ganho)
                .SumAsync(o => (decimal?)(o.ValorFinal ?? o.ValorEstimado), ct) ?? 0m;
            var taxa = (ganhas + perdidas) == 0 ? 0m : Math.Round(100m * ganhas / (ganhas + perdidas), 1);

            resultado.Add(new RankingComercialDto(v.Id, v.NomeCompleto, 0, valorGanho, ganhas, taxa));
        }

        var ordenado = resultado.OrderByDescending(r => r.ValorGanho).ToList();
        return ordenado.Select((r, i) => r with { Posicao = i + 1 }).ToList();
    }

    private static async Task<List<MotivoPerdaResumoDto>> ObterMotivosPerdaAsync(
        IQueryable<CrmOpportunity> oportunidadesQuery, DateTimeOffset inicio, DateTimeOffset fim, CancellationToken ct)
    {
        var bruto = await oportunidadesQuery
            .Where(o => o.Etapa.Tipo == TipoEtapaPipeline.Perdido && o.DataEfetivaFechamento >= inicio && o.DataEfetivaFechamento <= fim && o.MotivoPerda != null)
            .GroupBy(o => o.MotivoPerda!.Descricao)
            .Select(g => new { Motivo = g.Key, Quantidade = g.Count(), Valor = g.Sum(o => o.ValorEstimado) })
            .OrderByDescending(g => g.Quantidade)
            .ToListAsync(ct);

        return bruto.Select(b => new MotivoPerdaResumoDto(b.Motivo, b.Quantidade, b.Valor)).ToList();
    }
}
