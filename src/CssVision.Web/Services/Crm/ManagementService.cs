using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Authorization;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Notion;
using CssVision.Web.Domain.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace CssVision.Web.Services.Crm;

public sealed class ManagementService(
    ApplicationDbContext db,
    ICurrentUserService currentUser,
    IEquipeComercialService equipe,
    UserManager<ApplicationUser> userManager,
    IMemoryCache? cache = null,
    ICrmEventHub? eventos = null) : IManagementService
{
    /// <summary>Oportunidades abertas sem troca de etapa há mais de N dias entram no alerta de estagnação.</summary>
    private const int DiasSemMovimentacaoAlerta = 10;

    public async Task<GestaoComercialResumoDto> ObterResumoAsync(DateOnly? dataInicio, DateOnly? dataFim, CancellationToken ct)
    {
        ExigirGestaoComercial();
        var escopo = RespostaEmCache.Escopo(await equipe.ObterVendedoresVisiveisAsync(ct));
        return await RespostaEmCache.ObterAsync(cache, eventos, "gestao-resumo", new { escopo, dataInicio, dataFim },
            () => CalcularResumoAsync(dataInicio, dataFim, ct));
    }

    private async Task<GestaoComercialResumoDto> CalcularResumoAsync(DateOnly? dataInicio, DateOnly? dataFim, CancellationToken ct)
    {
        // Sem período informado, o ranking e as métricas são do mês atual (Brasília) — antes eram os
        // "últimos 30 dias", que depois da virada do mês ainda mostravam o mês anterior.
        var hoje = HorarioBrasilia.Hoje;
        var inicio = HorarioBrasilia.Inicio(dataInicio ?? HorarioBrasilia.PrimeiroDiaDoMes(hoje));
        var fim = HorarioBrasilia.Fim(dataFim ?? hoje);
        var agora = DateTimeOffset.UtcNow;

        var visiveis = await equipe.ObterVendedoresVisiveisAsync(ct);

        var leadsQuery = db.CrmLeads.AsNoTracking().Where(l => !l.Arquivado);
        var oportunidadesQuery = db.CrmOpportunities.AsNoTracking().Where(o => !o.Arquivado);
        if (visiveis is not null)
        {
            leadsQuery = leadsQuery.Where(l => visiveis.Contains(l.ResponsavelId ?? Guid.Empty));
            oportunidadesQuery = oportunidadesQuery.Where(o => visiveis.Contains(o.ResponsavelId));
        }

        var (tempoMedioContatoHoras, leadsComContato, contatoPorVendedor) = await ObterTempoMedioPrimeiroContatoAsync(leadsQuery, inicio, fim, ct);
        var tempoMedioPorEtapa = await ObterTempoMedioPorEtapaAsync(oportunidadesQuery, ct);
        var oportunidadesSemMovimentacao = await ObterOportunidadesSemMovimentacaoAsync(oportunidadesQuery, agora, ct);
        var ranking = await ObterRankingAsync(visiveis, inicio, fim, ct);
        var motivosPerda = await ObterMotivosPerdaAsync(oportunidadesQuery, inicio, fim, ct);

        return new GestaoComercialResumoDto(tempoMedioContatoHoras, tempoMedioPorEtapa, oportunidadesSemMovimentacao, ranking, motivosPerda,
            leadsComContato, contatoPorVendedor);
    }

    public async Task<IReadOnlyList<VendedorResumoDto>> ObterVendedoresAsync(CancellationToken ct, bool incluirInativos = false)
    {
        ExigirGestaoOuFinanceiro();
        var escopo = RespostaEmCache.Escopo(await equipe.ObterVendedoresVisiveisAsync(ct));
        return await RespostaEmCache.ObterAsync(cache, eventos, "gestao-vendedores", new { escopo, incluirInativos },
            () => CalcularVendedoresAsync(incluirInativos, ct));
    }

    private async Task<IReadOnlyList<VendedorResumoDto>> CalcularVendedoresAsync(bool incluirInativos, CancellationToken ct)
    {
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
            // Consultores que estão num grupo da regional aparecem mesmo inativos (vêm marcados como inativos).
            var comercialEmGrupo = db.UserRoles.Join(db.Roles.Where(r => r.Name == Roles.Comercial), ur => ur.RoleId, r => r.Id, (ur, _) => ur.UserId);
            query = query.Where(u => u.Ativo || (u.GrupoId != null && comercialEmGrupo.Contains(u.Id)));
        }
        if (visiveis is not null) query = query.Where(u => visiveis.Contains(u.Id));
        // Administrador que só administra (não atua nas vendas) não aparece na carteira nem nas listas de consultores.
        query = query.Where(u => u.AtuaNasVendas);
        // Vendedores que o Notion não identifica ("Vendedor Notion xxxx") ficam fora do filtro e da carteira até alguém renomeá-los.
        query = query.Where(u => !u.NomeCompleto.StartsWith(NotionPageExtensions.PrefixoNomeProvisorio));

        var vendedores = await query
            .OrderByDescending(u => u.Ativo).ThenBy(u => u.NomeCompleto)
            .Select(u => new { u.Id, u.NomeCompleto, u.LimiteMensalLeads, u.LimiteDiarioLeads, u.Ativo, u.RecebeLeads, u.HorarioInicioLeads, u.HorarioFimLeads, u.DiasSemanaLeads, u.RecebeSomenteOQue, RegionalNome = u.Regional != null ? u.Regional.Nome : null })
            .ToListAsync(ct);
        var inicioMes = HorarioBrasilia.Inicio(HorarioBrasilia.PrimeiroDiaDoMes(HorarioBrasilia.Hoje));
        var inicioDia = LeadAssignmentService.InicioDoDia();
        var ids = vendedores.Select(v => (Guid?)v.Id).ToList();
        var idsOportunidade = vendedores.Select(v => v.Id).ToList();

        // Agrupado por vendedor no banco — antes eram 5 consultas por vendedor.
        var leadsDosVendedores = db.CrmLeads.AsNoTracking().Where(LixeiraDoQuadro.ForaDaLixeira).Where(l => ids.Contains(l.ResponsavelId));
        var leadsAtivos = await ContagensPorVendedor.ContarLeadsAsync(leadsDosVendedores.Where(l => !l.Arquivado), ct);
        var abertas = await ContagensPorVendedor.AbertasAsync(
            db.CrmOpportunities.AsNoTracking().Where(o => !o.Arquivado && idsOportunidade.Contains(o.ResponsavelId)), ct);
        // Mesma contagem dos limites: só leads do tráfego pago (ver LeadAssignmentService).
        var doTrafego = leadsDosVendedores.Where(OrigemLead.VeioDoTrafegoPago);
        var recebidosNoMes = await ContagensPorVendedor.ContarLeadsAsync(doTrafego.Where(l => l.CriadoEm >= inicioMes), ct);
        var recebidosHoje = await ContagensPorVendedor.ContarLeadsAsync(doTrafego.Where(l => (l.ResponsavelAtribuidoEm ?? l.CriadoEm) >= inicioDia), ct);
        var trafegoNoMes = await LeadsDeTrafegoNoMesAsync(leadsDosVendedores, ct);
        var trafegoMesAnterior = await LeadsDeTrafegoNoMesAsync(leadsDosVendedores, ct, mesesAtras: 1);

        return vendedores.Select(v => new VendedorResumoDto(
            v.Id, v.NomeCompleto, leadsAtivos.GetValueOrDefault(v.Id), abertas.GetValueOrDefault(v.Id).Quantidade,
            v.LimiteMensalLeads, recebidosNoMes.GetValueOrDefault(v.Id), v.LimiteDiarioLeads, recebidosHoje.GetValueOrDefault(v.Id),
            v.Ativo, v.RecebeLeads, trafegoNoMes.GetValueOrDefault(v.Id),
            v.HorarioInicioLeads?.ToString("HH:mm"), v.HorarioFimLeads?.ToString("HH:mm"), JanelaRecebimentoLeads.MascaraParaDias(v.DiasSemanaLeads),
            FiltroOQue.Separar(v.RecebeSomenteOQue), trafegoMesAnterior.GetValueOrDefault(v.Id), v.RegionalNome)).ToList();
    }

    public async Task<IReadOnlyList<ConsultorDesempenhoDto>> ObterDesempenhoConsultoresAsync(DateOnly? mesReferencia, CancellationToken ct)
    {
        ExigirGestaoComercial();

        var hoje = HorarioBrasilia.Hoje;
        var mes = HorarioBrasilia.PrimeiroDiaDoMes(mesReferencia ?? hoje);
        var inicioMes = HorarioBrasilia.Inicio(mes);
        var fimMes = HorarioBrasilia.Inicio(mes.AddMonths(1));

        var visiveis = await equipe.ObterVendedoresVisiveisAsync(ct);
        var idsComPapelComercial = (await userManager.GetUsersInRoleAsync(Roles.Comercial)).Select(u => u.Id).ToHashSet();

        var query = db.Users.AsNoTracking().Include(u => u.Regional).Where(u => idsComPapelComercial.Contains(u.Id) && u.AtuaNasVendas)
            .Where(u => !u.NomeCompleto.StartsWith(NotionPageExtensions.PrefixoNomeProvisorio));
        if (visiveis is not null) query = query.Where(u => visiveis.Contains(u.Id));

        var consultores = await query.ToListAsync(ct);

        var metas = await db.CrmSalesGoals.AsNoTracking()
            .Where(g => g.MesReferencia == mes)
            .ToDictionaryAsync(g => g.VendedorId, ct);

        var ids = consultores.Select(c => (Guid?)c.Id).ToList();
        var idsOportunidade = consultores.Select(c => c.Id).ToList();
        var leadsDosConsultores = db.CrmLeads.AsNoTracking().Where(LixeiraDoQuadro.ForaDaLixeira).Where(l => ids.Contains(l.ResponsavelId));
        var oportunidades = db.CrmOpportunities.AsNoTracking().Where(o => !o.Arquivado && idsOportunidade.Contains(o.ResponsavelId));

        // Agrupado por consultor no banco — antes eram 8 consultas por consultor.
        var leadsAtivos = await ContagensPorVendedor.ContarLeadsAsync(leadsDosConsultores.Where(l => !l.Arquivado), ct);
        var abertas = await ContagensPorVendedor.AbertasAsync(oportunidades, ct);
        var fechadas = await ContagensPorVendedor.FechadasAsync(
            oportunidades.Where(o => o.DataEfetivaFechamento >= inicioMes && o.DataEfetivaFechamento < fimMes), ct);
        var recebidos = await ContagensPorVendedor.ContarLeadsAsync(
            leadsDosConsultores.Where(OrigemLead.VeioDoTrafegoPago).Where(l => l.CriadoEm >= inicioMes), ct);
        var trafegoNoMes = await LeadsDeTrafegoNoMesAsync(leadsDosConsultores, ct);
        var leadsDoMes = await ContagensPorVendedor.ContarLeadsAsync(
            leadsDosConsultores.Where(l => !l.Arquivado && l.CriadoEm >= inicioMes && l.CriadoEm < fimMes), ct);

        return consultores
            .Select(c =>
            {
                abertas.TryGetValue(c.Id, out var aberta);
                fechadas.TryGetValue(c.Id, out var fechada);
                var valorGanho = fechada?.ValorGanho ?? 0m;
                metas.TryGetValue(c.Id, out var meta);
                var metaValor = meta?.MetaValor ?? 0m;
                // Meta de valor = adesão recebida nas vendas do mês.
                var adesaoDoMes = fechada?.ValorAdesao ?? 0m;
                var percentualMeta = metaValor == 0 ? 0m : Math.Round(100m * adesaoDoMes / metaValor, 1);
                return new ConsultorDesempenhoDto(
                    c.Id, c.NomeCompleto, c.Email!, c.PhoneNumber, c.Regional?.Nome, c.Ativo,
                    leadsAtivos.GetValueOrDefault(c.Id), aberta.Quantidade, aberta.Valor, fechada?.Ganhas ?? 0, valorGanho,
                    ContagensPorVendedor.TaxaConversaoLeads(fechada?.Ganhas ?? 0, leadsDoMes.GetValueOrDefault(c.Id)),
                    c.LimiteMensalLeads, recebidos.GetValueOrDefault(c.Id), metaValor, adesaoDoMes, percentualMeta, trafegoNoMes.GetValueOrDefault(c.Id));
            })
            .OrderByDescending(r => r.ValorGanho)
            .ToList();
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
        eventos?.PublicarQuadroAtualizado("gestao");
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
        eventos?.PublicarQuadroAtualizado("gestao");
    }

    public async Task AtualizarJanelaRecebimentoAsync(Guid vendedorId, AtualizarJanelaRecebimentoRequest request, CancellationToken ct)
    {
        ExigirGestaoComercial();

        if (!await equipe.PodeAcessarVendedorAsync(vendedorId, ct))
        {
            throw new CrmForbiddenException("Você não pode alterar o horário deste vendedor.");
        }

        var inicio = JanelaRecebimentoLeads.ParseHorario(request.HorarioInicio);
        var fim = JanelaRecebimentoLeads.ParseHorario(request.HorarioFim);
        if ((inicio is null) != (fim is null))
        {
            throw new CrmBusinessException("Informe o horário de início e o de fim (ou deixe os dois vazios).", "horario_incompleto");
        }

        var vendedor = await db.Users.FirstOrDefaultAsync(u => u.Id == vendedorId, ct)
            ?? throw new CrmNotFoundException("Vendedor", vendedorId);

        vendedor.HorarioInicioLeads = inicio;
        vendedor.HorarioFimLeads = fim;
        vendedor.DiasSemanaLeads = JanelaRecebimentoLeads.DiasParaMascara(request.DiasSemana);
        await db.SaveChangesAsync(ct);
        eventos?.PublicarQuadroAtualizado("gestao");
    }

    public async Task AtualizarTiposLeadAsync(Guid vendedorId, AtualizarTiposLeadRequest request, CancellationToken ct)
    {
        ExigirGestaoComercial();

        if (!await equipe.PodeAcessarVendedorAsync(vendedorId, ct))
        {
            throw new CrmForbiddenException("Você não pode alterar os tipos de lead deste vendedor.");
        }

        var vendedor = await db.Users.FirstOrDefaultAsync(u => u.Id == vendedorId, ct)
            ?? throw new CrmNotFoundException("Vendedor", vendedorId);

        vendedor.RecebeSomenteOQue = FiltroOQue.Juntar(request.OQue);
        await db.SaveChangesAsync(ct);
        eventos?.PublicarQuadroAtualizado("gestao");
    }

    public async Task AtualizarRecebeLeadsAsync(Guid vendedorId, AtualizarRecebeLeadsRequest request, CancellationToken ct)
    {
        ExigirGestaoOuFinanceiro();

        if (!await equipe.PodeAcessarVendedorAsync(vendedorId, ct))
        {
            throw new CrmForbiddenException("Você não pode alterar este vendedor.");
        }

        var vendedor = await db.Users.FirstOrDefaultAsync(u => u.Id == vendedorId, ct)
            ?? throw new CrmNotFoundException("Vendedor", vendedorId);

        vendedor.RecebeLeads = request.RecebeLeads;
        await db.SaveChangesAsync(ct);
        eventos?.PublicarQuadroAtualizado("gestao");
    }

    // --- auxiliares ---

    /// <summary>
    /// Leads de tráfego pago (Notion + sistema novo) que chegaram para o vendedor no mês corrente,
    /// pela data de chegada do lead (horário de Brasília).
    /// </summary>
    private static Task<Dictionary<Guid, int>> LeadsDeTrafegoNoMesAsync(IQueryable<CrmLead> leads, CancellationToken ct, int mesesAtras = 0)
    {
        // Mês de Brasília: 0 = mês atual, 1 = mês anterior (de 00:00 do dia 1 até 00:00 do dia 1 do mês seguinte).
        var primeiroDia = HorarioBrasilia.PrimeiroDiaDoMes(HorarioBrasilia.Hoje).AddMonths(-mesesAtras);
        var inicioMes = HorarioBrasilia.Inicio(primeiroDia);
        var fimMes = HorarioBrasilia.Inicio(primeiroDia.AddMonths(1));
        return ContagensPorVendedor.ContarLeadsAsync(
            leads.Where(OrigemLead.DeTrafegoPagoInclusiveNotion).Where(l => !l.Arquivado && l.CriadoEm >= inicioMes && l.CriadoEm < fimMes), ct);
    }

    /// <summary>Carteira por consultor e "recebe lead": gestão comercial e Financeiro (que vê só a regional dele — o escopo vem do EquipeComercialService).</summary>
    private void ExigirGestaoOuFinanceiro()
    {
        if (!currentUser.PodeGerirComercial && !currentUser.IsInRole(Authorization.Roles.Financeiro))
        {
            throw new Api.Contracts.Common.CrmForbiddenException("Apenas gestores comerciais e o financeiro podem acessar a gestão comercial.");
        }
    }

    private void ExigirGestaoComercial()
    {
        if (!currentUser.PodeGerirComercial)
        {
            throw new Api.Contracts.Common.CrmForbiddenException("Apenas gestores comerciais podem acessar a gestão comercial.");
        }
    }

    /// <summary>
    /// Tempo entre o lead chegar para o vendedor e o primeiro contato dele: a primeira vez que o
    /// vendedor move o lead de etapa no quadro (registro de auditoria "LeadMudouEtapa") ou conclui
    /// uma atividade — o que vier antes. Considera os leads do CRM (tráfego pago e cadastros; os do
    /// Notion foram trabalhados lá) que chegaram no período.
    /// </summary>
    private async Task<(double Horas, int Leads, IReadOnlyList<PrimeiroContatoVendedorDto> PorVendedor)> ObterTempoMedioPrimeiroContatoAsync(
        IQueryable<CrmLead> leadsQuery, DateTimeOffset inicio, DateTimeOffset fim, CancellationToken ct)
    {
        var inicioUtc = inicio;
        var fimUtc = fim;
        var leads = await leadsQuery
            .Where(l => l.ResponsavelId != null
                && l.ConsentimentoOrigem != OrigemLead.MarcadorMigracaoNotion
                && l.ConsentimentoOrigem != OrigemLead.MarcadorSincronizacaoNotion)
            .Select(l => new { l.Id, Chegada = l.ResponsavelAtribuidoEm ?? l.CriadoEm, VendedorId = l.ResponsavelId!.Value, Vendedor = l.Responsavel!.NomeCompleto })
            .Where(l => l.Chegada >= inicioUtc && l.Chegada <= fimUtc)
            .ToListAsync(ct);
        if (leads.Count == 0) return (0, 0, []);

        var ids = leads.Select(l => l.Id).ToList();
        var idsAnulaveis = ids.Select(id => (Guid?)id).ToList();
        var movimentos = await db.CrmAuditLogs.AsNoTracking()
            .Where(a => a.EntidadeTipo == nameof(CrmLead) && a.Acao == "LeadMudouEtapa" && idsAnulaveis.Contains(a.EntidadeId))
            .Select(a => new { LeadId = a.EntidadeId!.Value, a.OcorridoEm })
            .ToListAsync(ct);
        var atividades = await db.CrmActivities.AsNoTracking()
            .Where(a => ids.Contains(a.LeadId) && a.Status == StatusAtividade.Concluida && a.DataHoraConclusao != null)
            .Select(a => new { a.LeadId, OcorridoEm = a.DataHoraConclusao!.Value })
            .ToListAsync(ct);

        var contatos = movimentos.Concat(atividades)
            .GroupBy(c => c.LeadId)
            .ToDictionary(g => g.Key, g => g.Select(c => c.OcorridoEm).ToList());

        var tempos = leads
            .Select(l => new
            {
                l.VendedorId,
                l.Vendedor,
                // Primeiro contato depois de o lead chegar para o vendedor atual.
                Primeiro = contatos.TryGetValue(l.Id, out var lista) ? lista.Where(t => t >= l.Chegada).DefaultIfEmpty().Min() : default,
                l.Chegada,
            })
            .Where(t => t.Primeiro != default)
            .Select(t => new { t.VendedorId, t.Vendedor, Horas = (t.Primeiro - t.Chegada).TotalHours })
            .ToList();
        if (tempos.Count == 0) return (0, 0, []);

        var porVendedor = tempos
            .GroupBy(t => new { t.VendedorId, t.Vendedor })
            .Select(g => new PrimeiroContatoVendedorDto(g.Key.VendedorId, g.Key.Vendedor, Math.Round(g.Average(t => t.Horas), 1), g.Count()))
            .OrderBy(v => v.Horas)
            .ToList();
        return (Math.Round(tempos.Average(t => t.Horas), 1), tempos.Count, porVendedor);
    }

    private static async Task<List<TempoMedioEtapaDto>> ObterTempoMedioPorEtapaAsync(IQueryable<CrmOpportunity> oportunidadesQuery, CancellationToken ct)
    {
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
        var vendedoresQuery = db.Users.AsNoTracking().Where(u => u.Ativo && u.AtuaNasVendas);
        if (visiveis is not null) vendedoresQuery = vendedoresQuery.Where(u => visiveis.Contains(u.Id));
        var vendedores = await vendedoresQuery.Select(u => new { u.Id, u.NomeCompleto }).ToListAsync(ct);
        var ids = vendedores.Select(v => v.Id).ToList();

        var fechadas = await ContagensPorVendedor.FechadasAsync(db.CrmOpportunities.AsNoTracking()
            .Where(o => !o.Arquivado && ids.Contains(o.ResponsavelId) && o.DataEfetivaFechamento >= inicio && o.DataEfetivaFechamento <= fim), ct);
        var leadsDoPeriodo = await ContagensPorVendedor.ContarLeadsAsync(db.CrmLeads.AsNoTracking()
            .Where(l => !l.Arquivado && l.ResponsavelId != null && ids.Contains(l.ResponsavelId.Value) && l.CriadoEm >= inicio && l.CriadoEm <= fim), ct);

        var ordenado = vendedores
            .Select(v =>
            {
                fechadas.TryGetValue(v.Id, out var f);
                return new RankingComercialDto(v.Id, v.NomeCompleto, 0, f?.ValorGanho ?? 0m, f?.Ganhas ?? 0,
                    ContagensPorVendedor.TaxaConversaoLeads(f?.Ganhas ?? 0, leadsDoPeriodo.GetValueOrDefault(v.Id)), f?.ValorAdesao ?? 0m);
            })
            .OrderByDescending(r => r.ValorAdesao).ThenByDescending(r => r.ValorGanho)
            .ToList();
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
