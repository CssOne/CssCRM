using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using Microsoft.EntityFrameworkCore;
using CssVision.Web.Services.Notion;
using Microsoft.Extensions.Caching.Memory;

namespace CssVision.Web.Services.Crm;

public sealed class DashboardService(
    ApplicationDbContext db,
    IEquipeComercialService equipe,
    IActivityService activityService,
    ICurrentUserService currentUser,
    IMemoryCache? cache = null,
    ICrmEventHub? eventos = null) : IDashboardService
{
    /// <summary>Leads sem nenhum contato há mais de N dias entram no alerta de "parados".</summary>
    private const int DiasSemContatoAlerta = 5;

    /// <summary>Só leads que chegaram nos últimos N dias entram em "parados": os antigos (anos, do Notion) não são alerta.</summary>
    private const int DiasJanelaLeadsParados = 60;

    // Por usuário: as atividades do dia e a meta regional dependem de quem está vendo.
    public Task<DashboardDto> ObterAsync(DashboardFilterRequest filtro, CancellationToken ct) =>
        RespostaEmCache.ObterAsync(cache, eventos, "painel", new { usuario = currentUser.UserId, filtro }, () => CalcularAsync(filtro, ct));

    private async Task<DashboardDto> CalcularAsync(DashboardFilterRequest filtro, CancellationToken ct)
    {
        // Mês e dia de hoje no horário de Brasília: o painel passa para o mês novo à meia-noite daqui.
        var hoje = HorarioBrasilia.Hoje;
        var inicioPeriodo = filtro.DataInicio ?? HorarioBrasilia.PrimeiroDiaDoMes(hoje);
        var fimPeriodo = filtro.DataFim ?? hoje;
        var inicioUtc = HorarioBrasilia.Inicio(inicioPeriodo);
        var fimUtc = HorarioBrasilia.Fim(fimPeriodo);
        var agora = DateTimeOffset.UtcNow;

        var visiveis = await equipe.ObterVendedoresVisiveisAsync(ct);

        // Filtro de regional: só para quem tem visão total (os demais já estão presos à própria equipe). Fica só quem é da regional escolhida.
        var regionalFiltrada = currentUser.TemVisaoTotal ? filtro.RegionalId : null;
        if (regionalFiltrada is { } escolhida)
        {
            var daRegional = await db.Users.AsNoTracking().Where(u => u.RegionalId == escolhida).Select(u => u.Id).ToListAsync(ct);
            visiveis = visiveis is null ? daRegional : visiveis.Intersect(daRegional).ToList();
        }

        if (filtro.VendedorId.HasValue)
        {
            if (visiveis is not null && !visiveis.Contains(filtro.VendedorId.Value))
            {
                throw new Api.Contracts.Common.CrmForbiddenException();
            }
            visiveis = [filtro.VendedorId.Value];
        }

        var leadsQuery = db.CrmLeads.AsNoTracking().Where(l => !l.Arquivado);
        if (visiveis is not null) leadsQuery = leadsQuery.Where(l => visiveis.Contains(l.ResponsavelId ?? Guid.Empty));

        var oportunidadesQuery = db.CrmOpportunities.AsNoTracking().Where(o => !o.Arquivado);
        if (visiveis is not null) oportunidadesQuery = oportunidadesQuery.Where(o => visiveis.Contains(o.ResponsavelId));

        var atividadesQuery = db.CrmActivities.AsNoTracking().Where(a => !a.Arquivado);
        if (visiveis is not null) atividadesQuery = atividadesQuery.Where(a => visiveis.Contains(a.ResponsavelId));

        var inicioHoje = HorarioBrasilia.Inicio(hoje);
        var fimHoje = HorarioBrasilia.Fim(hoje);

        var novosLeads = await leadsQuery.CountAsync(l => l.CriadoEm >= inicioUtc && l.CriadoEm <= fimUtc, ct);
        // "Sem contato" = leads do período que ainda não saíram de "Sem etapa" e são de um consultor ativo
        // (antes contava todos os leads da base sem data de último contato: dezenas de milhares).
        var leadsSemContato = await leadsQuery.CountAsync(l => l.EtapaId == null && l.CriadoEm >= inicioUtc && l.CriadoEm <= fimUtc
            && l.Responsavel != null && l.Responsavel.Ativo, ct);
        // Card "Novos leads": só os do tráfego pago que ainda estão sem etapa (a conversão segue usando todos os leads do período).
        var novosLeadsTrafegoSemEtapa = await leadsQuery.Where(OrigemLead.VeioDoTrafegoPago)
            .CountAsync(l => l.EtapaId == null && l.CriadoEm >= inicioUtc && l.CriadoEm <= fimUtc, ct);
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
        var valorAdesao = await ganhas.SumAsync(o => (decimal?)o.PagamentoAdesao, ct) ?? 0m;
        var taxaConversao = ContagensPorVendedor.TaxaConversaoLeads(qtdGanhas, novosLeads);
        var ticketMedio = qtdGanhas == 0 ? 0m : Math.Round(valorGanho / qtdGanhas, 2);

        var indicadores = new DashboardIndicadoresDto(
            novosLeads, leadsSemContato, contatosHoje, atividadesAtrasadas,
            oportunidadesAbertas, valorPipeline, taxaConversao, ticketMedio, valorGanho, qtdGanhas, valorAdesao, novosLeadsTrafegoSemEtapa);

        // Meta do mês (ver CalcularMetaDoMesAsync: a meta da regional vale no lugar das individuais, não soma com elas).
        var mesReferencia = HorarioBrasilia.PrimeiroDiaDoMes(hoje);
        var (metaValor, metaQuantidade, realizadoMes, realizadoQuantidadeMes) =
            await CalcularMetaDoMesAsync(visiveis, filtro.VendedorId.HasValue, regionalFiltrada, oportunidadesQuery, mesReferencia, ct);
        var meta = new MetaResultadoDto(
            metaValor, realizadoMes,
            metaQuantidade == 0 ? 0m : Math.Round(100m * realizadoQuantidadeMes / metaQuantidade, 1),
            metaQuantidade, realizadoQuantidadeMes);

        var funilBruto = await oportunidadesQuery
            .Where(o => o.Etapa.Tipo == TipoEtapaPipeline.Aberta)
            .GroupBy(o => new { o.Etapa.Nome, o.Etapa.Ordem })
            .Select(g => new { g.Key.Nome, g.Key.Ordem, Quantidade = g.Count(), Valor = g.Sum(o => o.ValorEstimado) })
            .OrderBy(g => g.Ordem)
            .ToListAsync(ct);
        var funil = funilBruto.Select(f => new FunilEtapaDto(f.Nome, f.Quantidade, f.Valor)).ToList();

        var evolucao = await ObterEvolucaoVendasAsync(oportunidadesQuery, hoje, ct);

        // "Origem dos leads" aqui significa a origem comercial do cadastro — Leads (automático, tráfego
        // pago/site) vs Indicação (cadastro manual) — e não o campo livre `Origem`, que serve a outros
        // filtros (ver LeadKanbanFilterRequest.Origem).
        var origemBruto = await leadsQuery
            .GroupBy(l => l.CriadoManualmente)
            .Select(g => new { g.Key, Quantidade = g.Count() })
            .ToListAsync(ct);
        var origens = origemBruto
            .Select(o => new OrigemLeadDto(o.Key ? "Indicação" : "Leads", o.Quantidade))
            .OrderByDescending(o => o.Quantidade)
            .ToList();

        var desempenho = await ObterDesempenhoPorVendedorAsync(visiveis, inicioUtc, fimUtc, ct);
        var funilLeads = await ObterFunilDeLeadsAsync(leadsQuery, inicioUtc, fimUtc, ct);
        var resumoMensal = await ObterResumoMensalAsync(leadsQuery, oportunidadesQuery, hoje, ct);

        var atividadesDoDiaPagina = await activityService.ListarAsync(
            new ActivityFilterRequest { Visao = VisaoAtividade.Hoje, TamanhoPagina = 20 }, ct);

        var (leadsParados, leadsParadosTotal) = await ObterLeadsParadosAsync(leadsQuery, agora, ct);

        return new DashboardDto(indicadores, meta, funil, evolucao, origens, desempenho, atividadesDoDiaPagina.Itens, leadsParados,
            funilLeads, resumoMensal, leadsParadosTotal);
    }

    /// <summary>
    /// Meta e realizado do mês corrente. A meta que o administrador define para a <b>regional</b> vale <b>no lugar</b> das metas individuais dos
    /// consultores, não soma com elas: as individuais detalham quem entrega o quê dentro da regional, e somar as duas contava a mesma meta duas
    /// vezes (500 da regional + 745 dos consultores + 200 de outra regional = 1445).
    /// <list type="bullet">
    /// <item>Consultor ou gestor (com regional): a meta da própria regional e as vendas da regional inteira no mês. Sem meta cadastrada para a
    /// regional (ou sem regional), vale a soma das metas individuais que ele enxerga, contra as vendas que ele enxerga.</item>
    /// <item>Administrador (visão total): para cada regional que enxerga, a meta dela ou, se não tiver, a soma das individuais dos seus consultores;
    /// consultores sem regional entram pelas individuais.</item>
    /// <item>Um consultor escolhido no filtro: a meta individual dele.</item>
    /// </list>
    /// Cada medida (valor e quantidade) cai para a soma das individuais quando a regional só definiu a outra. Meta de valor = adesão recebida (ver GoalService).
    /// </summary>
    private async Task<(decimal MetaValor, int MetaQuantidade, decimal Realizado, int RealizadoQuantidade)> CalcularMetaDoMesAsync(
        List<Guid>? visiveis, bool umConsultor, Guid? regionalFiltrada, IQueryable<CrmOpportunity> oportunidadesQuery, DateOnly mes, CancellationToken ct)
    {
        var inicioMes = HorarioBrasilia.Inicio(mes);
        var individuais = db.CrmSalesGoals.AsNoTracking().Where(g => g.MesReferencia == mes);
        if (visiveis is not null) individuais = individuais.Where(g => visiveis.Contains(g.VendedorId));
        var regionais = db.CrmRegionalGoals.AsNoTracking().Where(g => g.MesReferencia == mes);

        async Task<(decimal Valor, int Quantidade)> RealizadoAsync(IQueryable<CrmOpportunity> consulta)
        {
            var ganhas = consulta.Where(o => o.Etapa.Tipo == TipoEtapaPipeline.Ganho && o.DataEfetivaFechamento >= inicioMes);
            return (await ganhas.SumAsync(o => o.PagamentoAdesao, ct) ?? 0m, await ganhas.CountAsync(ct));
        }

        async Task<(decimal MetaValor, int MetaQuantidade, decimal Realizado, int RealizadoQuantidade)> SoIndividuaisAsync()
        {
            var valor = await individuais.SumAsync(g => (decimal?)g.MetaValor, ct) ?? 0m;
            var quantidade = await individuais.SumAsync(g => (int?)g.MetaQuantidadeVendas, ct) ?? 0;
            var (realizado, realizadoQuantidade) = await RealizadoAsync(oportunidadesQuery);
            return (valor, quantidade, realizado, realizadoQuantidade);
        }

        if (umConsultor) return await SoIndividuaisAsync();

        // Administrador que escolheu uma regional: a meta é a dela (ou a soma das individuais, se ela não tiver), contra as vendas dela
        // (as oportunidades já vêm só dos consultores da regional).
        if (regionalFiltrada is { } escolhida)
        {
            var daRegional = await regionais.Where(g => g.RegionalId == escolhida).Select(g => new { g.MetaValor, g.MetaQuantidadeVendas }).FirstOrDefaultAsync(ct);
            var valorDaEscolhida = await individuais.SumAsync(g => (decimal?)g.MetaValor, ct) ?? 0m;
            var quantidadeDaEscolhida = await individuais.SumAsync(g => (int?)g.MetaQuantidadeVendas, ct) ?? 0;
            var (realizadoDaEscolhida, realizadoQuantidadeDaEscolhida) = await RealizadoAsync(oportunidadesQuery);
            return (
                daRegional?.MetaValor is > 0 ? daRegional.MetaValor.Value : valorDaEscolhida,
                daRegional is { MetaQuantidadeVendas: > 0 } ? daRegional.MetaQuantidadeVendas : quantidadeDaEscolhida,
                realizadoDaEscolhida,
                realizadoQuantidadeDaEscolhida);
        }

        if (currentUser.TemVisaoTotal)
        {
            // Regionais ocultas do administrador ficam de fora.
            var ocultas = await EscopoRegional.OcultasAsync(db, currentUser, ct);
            var porRegional = await individuais.GroupBy(g => g.Vendedor.RegionalId)
                .Select(g => new { RegionalId = g.Key, Valor = g.Sum(x => (decimal?)x.MetaValor) ?? 0m, Quantidade = g.Sum(x => (int?)x.MetaQuantidadeVendas) ?? 0 })
                .ToListAsync(ct);
            var daRegional = await regionais.Where(g => !ocultas.Contains(g.RegionalId))
                .Select(g => new { g.RegionalId, g.MetaValor, g.MetaQuantidadeVendas })
                .ToListAsync(ct);

            decimal metaValor = 0m;
            var metaQuantidade = 0;
            foreach (var r in porRegional.Where(p => p.RegionalId is null || !ocultas.Contains(p.RegionalId.Value)))
            {
                var meta = r.RegionalId is { } id ? daRegional.FirstOrDefault(m => m.RegionalId == id) : null;
                metaValor += meta?.MetaValor is > 0 ? meta.MetaValor.Value : r.Valor;
                metaQuantidade += meta is { MetaQuantidadeVendas: > 0 } ? meta.MetaQuantidadeVendas : r.Quantidade;
            }

            // Regional com meta cadastrada e nenhum consultor com meta individual: a meta dela também conta.
            foreach (var m in daRegional.Where(m => porRegional.All(p => p.RegionalId != m.RegionalId)))
            {
                metaValor += m.MetaValor ?? 0m;
                metaQuantidade += m.MetaQuantidadeVendas;
            }

            var (realizado, realizadoQuantidade) = await RealizadoAsync(oportunidadesQuery);
            return (metaValor, metaQuantidade, realizado, realizadoQuantidade);
        }

        var minhaRegionalId = await db.Users.AsNoTracking().Where(u => u.Id == currentUser.UserId).Select(u => u.RegionalId).FirstOrDefaultAsync(ct);
        var minha = minhaRegionalId is null
            ? null
            : await regionais.Where(g => g.RegionalId == minhaRegionalId).Select(g => new { g.MetaValor, g.MetaQuantidadeVendas }).FirstOrDefaultAsync(ct);
        if (minha is null || (minha.MetaValor is not > 0 && minha.MetaQuantidadeVendas <= 0)) return await SoIndividuaisAsync();

        var valorIndividuais = await individuais.SumAsync(g => (decimal?)g.MetaValor, ct) ?? 0m;
        var quantidadeIndividuais = await individuais.SumAsync(g => (int?)g.MetaQuantidadeVendas, ct) ?? 0;
        // As vendas contadas contra a meta da regional são as da regional inteira, não só as de quem está vendo.
        var vendasDaRegional = db.CrmOpportunities.AsNoTracking().Where(o => !o.Arquivado && o.Responsavel.RegionalId == minhaRegionalId);
        var (realizadoRegional, realizadoQuantidadeRegional) = await RealizadoAsync(vendasDaRegional);
        return (
            minha.MetaValor is > 0 ? minha.MetaValor.Value : valorIndividuais,
            minha.MetaQuantidadeVendas > 0 ? minha.MetaQuantidadeVendas : quantidadeIndividuais,
            realizadoRegional,
            realizadoQuantidadeRegional);
    }

    private static async Task<List<EvolucaoVendasDto>> ObterEvolucaoVendasAsync(IQueryable<CrmOpportunity> oportunidadesQuery, DateOnly hoje, CancellationToken ct)
    {
        var inicioJanela = new DateOnly(hoje.Year, hoje.Month, 1).AddMonths(-5);
        var inicioJanelaUtc = HorarioBrasilia.Inicio(inicioJanela);

        var fechamentos = await oportunidadesQuery
            .Where(o => o.Etapa.Tipo == TipoEtapaPipeline.Ganho && o.DataEfetivaFechamento >= inicioJanelaUtc)
            .Select(o => new { o.DataEfetivaFechamento, Valor = o.ValorFinal ?? o.ValorEstimado, o.PagamentoAdesao })
            .ToListAsync(ct);

        var resultado = new List<EvolucaoVendasDto>();
        for (var i = 5; i >= 0; i--)
        {
            var mes = new DateOnly(hoje.Year, hoje.Month, 1).AddMonths(-i);
            var doMes = fechamentos.Where(f => HorarioBrasilia.Dia(f.DataEfetivaFechamento!.Value) is var dia && dia.Year == mes.Year && dia.Month == mes.Month).ToList();
            resultado.Add(new EvolucaoVendasDto(mes.ToString("MM/yyyy"), doMes.Sum(f => f.Valor), doMes.Count, doMes.Sum(f => f.PagamentoAdesao ?? 0m)));
        }

        return resultado;
    }

    private async Task<List<DesempenhoVendedorDto>> ObterDesempenhoPorVendedorAsync(
        List<Guid>? visiveis, DateTimeOffset inicioUtc, DateTimeOffset fimUtc, CancellationToken ct)
    {
        var vendedoresQuery = db.Users.AsNoTracking().AsQueryable();
        if (visiveis is not null) vendedoresQuery = vendedoresQuery.Where(u => visiveis.Contains(u.Id));

        var vendedores = await vendedoresQuery.Select(u => new { u.Id, u.NomeCompleto, u.Ativo }).ToListAsync(ct);

        // Tudo agrupado por vendedor no banco — antes eram 7 consultas por vendedor (~560 por tela).
        var leadsQuery = db.CrmLeads.AsNoTracking().Where(l => !l.Arquivado);
        var oportunidades = db.CrmOpportunities.AsNoTracking().Where(o => !o.Arquivado);
        if (visiveis is not null)
        {
            leadsQuery = leadsQuery.Where(l => visiveis.Contains(l.ResponsavelId ?? Guid.Empty));
            oportunidades = oportunidades.Where(o => visiveis.Contains(o.ResponsavelId));
        }
        // Leads e conversão do período: leads que chegaram para o vendedor no período e vendas fechadas nele.
        var leads = await ContagensPorVendedor.ContarLeadsAsync(leadsQuery.Where(l => l.CriadoEm >= inicioUtc && l.CriadoEm <= fimUtc), ct);
        var abertas = await ContagensPorVendedor.AbertasAsync(oportunidades, ct);
        var fechadas = await ContagensPorVendedor.FechadasAsync(
            oportunidades.Where(o => o.DataEfetivaFechamento >= inicioUtc && o.DataEfetivaFechamento <= fimUtc), ct);

        return vendedores
            // Sem a lista de dezenas de contas inativas zeradas: só quem é ativo, recebeu lead ou vendeu no período.
            .Where(v => v.Ativo || leads.GetValueOrDefault(v.Id) > 0 || (fechadas.TryGetValue(v.Id, out var f) && f.Ganhas > 0))
            .Select(v =>
            {
                abertas.TryGetValue(v.Id, out var aberta);
                fechadas.TryGetValue(v.Id, out var fechada);
                var leadsDoVendedor = leads.GetValueOrDefault(v.Id);
                return new DesempenhoVendedorDto(v.Id, v.NomeCompleto, leadsDoVendedor, aberta.Quantidade, aberta.Valor,
                    fechada?.Ganhas ?? 0, fechada?.ValorGanho ?? 0m, ContagensPorVendedor.TaxaConversaoLeads(fechada?.Ganhas ?? 0, leadsDoVendedor),
                    fechada?.ValorAdesao ?? 0m);
            })
            // Ranking: maior conversão primeiro; quem não recebeu lead no período (conversão não se aplica) vai para o fim.
            .OrderByDescending(d => d.LeadsAtribuidos > 0).ThenByDescending(d => d.TaxaConversao)
            .ThenByDescending(d => d.VendasGanhas).ThenByDescending(d => d.ValorGanho)
            .ToList();
    }

    /// <summary>
    /// Leads parados do tráfego pago (não os do Notion): em coluna aberta, de consultor ativo, que chegaram nos últimos 60 dias e não tiveram
    /// contato, atualização nem troca de responsável há mais de 5 dias. Antes entrava qualquer lead sem
    /// "último contato" — todo o histórico do Notion (leads de 2023 apareciam com 1.300 dias parados).
    /// </summary>
    private static async Task<(List<AlertaLeadParadoDto> Itens, int Total)> ObterLeadsParadosAsync(
        IQueryable<CrmLead> leadsQuery, DateTimeOffset agora, CancellationToken ct)
    {
        var limite = agora.AddDays(-DiasSemContatoAlerta);
        var janela = agora.AddDays(-DiasJanelaLeadsParados);

        var candidatos = leadsQuery
            // Só leads que vieram dos anúncios (Meta Lead Ads e formulário do site): os cards do Notion são trabalhados lá.
            .Where(OrigemLead.VeioDoTrafegoPago)
            .Where(l => l.Etapa == null || !l.Etapa.Fechada)
            .Where(l => l.Responsavel != null && l.Responsavel.Ativo)
            .Where(l => l.CriadoEm >= janela && l.CriadoEm < limite)
            .Where(l => l.UltimoContatoEm == null || l.UltimoContatoEm < limite)
            .Where(l => l.AtualizadoEm == null || l.AtualizadoEm < limite)
            .Where(l => l.ResponsavelAtribuidoEm == null || l.ResponsavelAtribuidoEm < limite);

        var total = await candidatos.CountAsync(ct);
        var parados = await candidatos
            .OrderBy(l => l.CriadoEm)
            .Take(15)
            .Select(l => new
            {
                l.Id, l.NomeOuRazaoSocial, ResponsavelNome = l.Responsavel!.NomeCompleto,
                EtapaNome = l.Etapa != null ? l.Etapa.Nome : null,
                l.UltimoContatoEm, l.AtualizadoEm, l.ResponsavelAtribuidoEm, l.CriadoEm,
            })
            .ToListAsync(ct);

        var itens = parados.Select(l =>
        {
            var ultimoMovimento = new[] { l.UltimoContatoEm, l.AtualizadoEm, l.ResponsavelAtribuidoEm, l.CriadoEm }
                .Where(d => d.HasValue).Max()!.Value;
            return new AlertaLeadParadoDto(l.Id, l.NomeOuRazaoSocial, l.ResponsavelNome, (int)(agora - ultimoMovimento).TotalDays, l.EtapaNome ?? "Sem etapa");
        }).ToList();
        return (itens, total);
    }

    /// <summary>Resumo do quadro de leads: leads do período em cada coluna (etapa atual), na ordem do quadro.</summary>
    private static async Task<List<EtapaLeadResumoDto>> ObterFunilDeLeadsAsync(
        IQueryable<CrmLead> leadsQuery, DateTimeOffset inicioUtc, DateTimeOffset fimUtc, CancellationToken ct)
    {
        var grupos = await leadsQuery
            .Where(l => l.CriadoEm >= inicioUtc && l.CriadoEm <= fimUtc)
            .GroupBy(l => new { Nome = l.Etapa != null ? l.Etapa.Nome : null, Cor = l.Etapa != null ? l.Etapa.Cor : null, Ordem = l.Etapa != null ? l.Etapa.Ordem : -1 })
            .Select(g => new { g.Key.Nome, g.Key.Cor, g.Key.Ordem, Quantidade = g.Count() })
            .ToListAsync(ct);

        return grupos.OrderBy(g => g.Ordem)
            .Select(g => new EtapaLeadResumoDto(g.Nome ?? "Sem etapa", g.Cor, g.Quantidade))
            .ToList();
    }

    /// <summary>Últimos 12 meses (horário de Brasília): leads que chegaram, perdidos, vendas, valor, adesão e conversão.</summary>
    private static async Task<List<ResumoMensalDto>> ObterResumoMensalAsync(
        IQueryable<CrmLead> leadsQuery, IQueryable<CrmOpportunity> oportunidadesQuery, DateOnly hoje, CancellationToken ct)
    {
        var primeiroMes = HorarioBrasilia.PrimeiroDiaDoMes(hoje).AddMonths(-11);
        var inicioUtc = HorarioBrasilia.Inicio(primeiroMes);

        var leads = await leadsQuery
            .Where(l => l.CriadoEm >= inicioUtc)
            .Select(l => new { l.CriadoEm, Perdido = l.Etapa != null && l.Etapa.Nome == NotionEtapaLead.Perdido })
            .ToListAsync(ct);
        var vendas = await oportunidadesQuery
            .Where(o => o.Etapa.Tipo == TipoEtapaPipeline.Ganho && o.DataEfetivaFechamento >= inicioUtc)
            .Select(o => new { Data = o.DataEfetivaFechamento!.Value, Valor = o.ValorFinal ?? o.ValorEstimado, Adesao = o.PagamentoAdesao })
            .ToListAsync(ct);

        var leadsPorMes = leads.ToLookup(l => HorarioBrasilia.PrimeiroDiaDoMes(HorarioBrasilia.Dia(l.CriadoEm)));
        var vendasPorMes = vendas.ToLookup(v => HorarioBrasilia.PrimeiroDiaDoMes(HorarioBrasilia.Dia(v.Data)));

        return Enumerable.Range(0, 12).Select(i =>
        {
            var mes = primeiroMes.AddMonths(i);
            var l = leadsPorMes[mes].ToList();
            var v = vendasPorMes[mes].ToList();
            return new ResumoMensalDto(mes.ToString("MM/yyyy"), l.Count, l.Count(x => x.Perdido), v.Count,
                v.Sum(x => x.Valor), v.Sum(x => x.Adesao ?? 0m), ContagensPorVendedor.TaxaConversaoLeads(v.Count, l.Count));
        }).ToList();
    }
}
