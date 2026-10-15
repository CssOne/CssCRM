using System.Globalization;
using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Notion;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace CssVision.Web.Services.Crm;

public interface IRelatorioComercialService
{
    /// <param name="consultorId">Só leads e vendas deste consultor (responsável).</param>
    /// <param name="etapaIds">Só leads nestas etapas do quadro de leads (Guid.Empty = "Sem etapa") e as vendas deles.</param>
    Task<RelatorioComercialDto> ObterAsync(DateOnly? dataInicio, DateOnly? dataFim, Guid? consultorId, IReadOnlyCollection<Guid>? etapaIds, CancellationToken ct);

    /// <param name="extras">Datas próprias de chegada/venda, "Indicação?" e tipo de indicação.</param>
    Task<RelatorioComercialDto> ObterAsync(DateOnly? dataInicio, DateOnly? dataFim, Guid? consultorId, IReadOnlyCollection<Guid>? etapaIds, RelatorioFiltroExtra? extras, CancellationToken ct);

    /// <summary>A lista de clientes por trás de um card do relatório (mesmos filtros do relatório). <paramref name="tipo"/>: leads, perdidos, vendas, adesao, mensalidade,
    /// rastreador, vistoria, indicacao, trafego-leads, trafego-vendas, indicacao-leads, indicacao-vendas, sem-origem-leads, sem-origem-vendas.</summary>
    Task<RelatorioDetalheDto> ObterDetalheAsync(
        string tipo, DateOnly? dataInicio, DateOnly? dataFim, Guid? consultorId, IReadOnlyCollection<Guid>? etapaIds, RelatorioFiltroExtra? extras, CancellationToken ct);
}

/// <summary>
/// Relatório comercial: reúne o que os relatórios do Notion mostravam (vendas por mês/semana/estado/
/// vendedor, origem dos leads, perdidos, FIPE, adesão, mensalidade, rastreador, vistoria, indicação)
/// calculado com os leads e vendas que já estão no CRM — o Notion é sincronizado para cá — e o controle
/// mensal de marketing (gasto em mídia) que só existe no Notion.
/// </summary>
public sealed class RelatorioComercialService(
    ApplicationDbContext db,
    ICurrentUserService currentUser,
    IEquipeComercialService equipe,
    IMemoryCache? cache = null,
    ICrmEventHub? eventos = null) : IRelatorioComercialService
{
    private static readonly DateOnly InicioPadrao = new(2025, 1, 1);

    /// <summary>Limites (R$) das faixas de FIPE do gráfico "Fipe" do Notion.</summary>
    private static readonly (decimal Ate, string Rotulo)[] FaixasFipe =
    [
        (30_000m, "Até R$ 30 mil"),
        (50_000m, "R$ 30 a 50 mil"),
        (80_000m, "R$ 50 a 80 mil"),
        (120_000m, "R$ 80 a 120 mil"),
        (200_000m, "R$ 120 a 200 mil"),
        (decimal.MaxValue, "Acima de R$ 200 mil"),
    ];

    public Task<RelatorioComercialDto> ObterAsync(DateOnly? dataInicio, DateOnly? dataFim, Guid? consultorId, IReadOnlyCollection<Guid>? etapaIds, CancellationToken ct) =>
        ObterAsync(dataInicio, dataFim, consultorId, etapaIds, null, ct);

    public async Task<RelatorioComercialDto> ObterAsync(
        DateOnly? dataInicio, DateOnly? dataFim, Guid? consultorId, IReadOnlyCollection<Guid>? etapaIds, RelatorioFiltroExtra? extras, CancellationToken ct)
    {
        // Aberto a todos da área comercial: cada usuário só enxerga o que o escopo dele permite (EquipeComercialService.ObterVendedoresVisiveisAsync —
        // o consultor, só os próprios resultados), e a origem dos leads só vai para quem tem visão total.

        var hoje = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-3));
        var inicio = dataInicio ?? InicioPadrao;
        var fim = dataFim ?? hoje;
        if (fim < inicio)
        {
            throw new CrmBusinessException("A data final não pode ser anterior à inicial.", "periodo_invalido");
        }

        var (visiveis, regionalFixa) = await VisiveisDoRelatorioAsync(ct);
        if (extras?.RegionalId is { } regionalId)
        {
            // Aba de uma regional: só os consultores dela, dentro do que quem consulta já enxerga.
            var daRegional = await db.Users.AsNoTracking().Where(u => u.RegionalId == regionalId).Select(u => u.Id).ToListAsync(ct);
            visiveis = visiveis is null ? daRegional : visiveis.Intersect(daRegional).ToList();
        }
        var etapas = etapaIds is { Count: > 0 } ? etapaIds.Distinct().Order().ToList() : null;
        // Tipos de indicação sem maiúsculas/minúsculas (o dado veio de épocas com grafias diferentes), ordenados para a chave do cache.
        var tipos = (extras?.TiposIndicacao ?? []).Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim().ToLower()).SelectMany(t => t == "pessoal" ? new[] { "pessoal", "indicação pessoal" } : new[] { t }).Distinct().Order().ToList();
        var filtroExtra = new RelatorioFiltroExtra(extras?.ChegadaInicio, extras?.ChegadaFim, extras?.VendaInicio, extras?.VendaFim, extras?.Indicacao, tipos, extras?.RegionalId);
        var relatorio = await RespostaEmCache.ObterAsync(cache, eventos, "relatorio-comercial",
            new { escopo = RespostaEmCache.Escopo(visiveis), inicio, fim, consultorId, etapas, extras = new { filtroExtra.ChegadaInicio, filtroExtra.ChegadaFim, filtroExtra.VendaInicio, filtroExtra.VendaFim, filtroExtra.Indicacao, tipos, filtroExtra.RegionalId } },
            () => CalcularAsync(inicio, fim, visiveis, consultorId, etapas, filtroExtra, tipos, ct));

        // O consultor que abre o relatório vê só os próprios números (totais, período, etapa, estado, FIPE). Não recebe o que é da gestão: a origem dos leads
        // (como no quadro de leads), o produto, o desempenho por vendedor e o controle de marketing do Notion (gasto em anúncios e faturamento da empresa, que
        // não é filtrado por consultor). Aplicado depois do cache, porque a resposta guardada é a mesma para usuários com o mesmo escopo e permissões diferentes.
        return currentUser.PodeGerirComercial
            ? relatorio with { RegionalFixaId = regionalFixa }
            : relatorio with { PorOrigem = [], PorProduto = [], PorVendedor = [], MarketingNotion = [] };
    }

    private sealed record LeadLinha(DateTimeOffset Chegada, string? Origem, string? Estado, string? Produto, Guid? ResponsavelId, string? Responsavel, bool Perdido,
        Guid? EtapaId = null, string? Etapa = null, int EtapaOrdem = int.MaxValue);

    private sealed record VendaLinha(
        DateTimeOffset Data, Guid ResponsavelId, string Responsavel, string? Origem, string? Estado, string? Produto,
        decimal Adesao, decimal Mensalidade, decimal Rastreador, decimal Vistoria, decimal Indicacao, bool Indicada, decimal? Fipe);

    private const int MaximoDoDetalhe = 500;

    private static IQueryable<CrmOpportunity> ComLeadEm(IQueryable<CrmOpportunity> vendas, IQueryable<Guid> idsDeLeads) => vendas.Where(o => idsDeLeads.Contains(o.LeadId));

    public async Task<RelatorioDetalheDto> ObterDetalheAsync(
        string tipo, DateOnly? dataInicio, DateOnly? dataFim, Guid? consultorId, IReadOnlyCollection<Guid>? etapaIds, RelatorioFiltroExtra? extras, CancellationToken ct)
    {
        var hoje = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-3));
        var inicio = dataInicio ?? InicioPadrao;
        var fim = dataFim ?? hoje;
        if (fim < inicio) throw new CrmBusinessException("A data final não pode ser anterior à inicial.", "periodo_invalido");

        var (visiveis, _) = await VisiveisDoRelatorioAsync(ct);
        if (extras?.RegionalId is { } regionalId)
        {
            var daRegional = await db.Users.AsNoTracking().Where(u => u.RegionalId == regionalId).Select(u => u.Id).ToListAsync(ct);
            visiveis = visiveis is null ? daRegional : visiveis.Intersect(daRegional).ToList();
        }
        var etapas = etapaIds is { Count: > 0 } ? etapaIds.Distinct().Order().ToList() : null;
        var tipos = (extras?.TiposIndicacao ?? []).Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim().ToLower())
            .SelectMany(t => t == "pessoal" ? new[] { "pessoal", "indicação pessoal" } : new[] { t }).Distinct().Order().ToList();
        var filtroExtra = new RelatorioFiltroExtra(extras?.ChegadaInicio, extras?.ChegadaFim, extras?.VendaInicio, extras?.VendaFim, extras?.Indicacao, tipos, extras?.RegionalId);
        var (leadsQuery, vendasQuery) = MontarConsultas(inicio, fim, visiveis, consultorId, etapas, filtroExtra, tipos);

        // Mesmos grupos dos cards de conversão (tráfego pago, indicação, sem origem identificada): ver CalcularAsync.
        IQueryable<CrmLead> Grupo(IQueryable<CrmLead> q, string grupo) => grupo switch
        {
            "trafego" => q.Where(OrigemLead.DeTrafegoPagoInclusiveNotion).Where(l => !db.CrmLeads.Where(OrigemLead.CardDeIndicacao).Select(x => x.Id).Contains(l.Id)),
            "indicacao" => q.Where(OrigemLead.CardDeIndicacao),
            _ => q.Where(l => !db.CrmLeads.Where(OrigemLead.DeTrafegoPagoInclusiveNotion).Select(x => x.Id).Contains(l.Id)
                && !db.CrmLeads.Where(OrigemLead.CardDeIndicacao).Select(x => x.Id).Contains(l.Id)),
        };
        IQueryable<Guid> IdsDoGrupo(string grupo) => Grupo(db.CrmLeads.AsNoTracking(), grupo).Select(l => l.Id);

        if (tipo is "leads" or "perdidos" or "trafego-leads" or "indicacao-leads" or "sem-origem-leads")
        {
            var consulta = tipo switch
            {
                "perdidos" => leadsQuery.Where(OrigemLead.DeTrafegoPagoInclusiveNotion).Where(l => l.Etapa != null && l.Etapa.Nome == NotionEtapaLead.Perdido),
                "trafego-leads" => Grupo(leadsQuery, "trafego"),
                "indicacao-leads" => Grupo(leadsQuery, "indicacao"),
                "sem-origem-leads" => Grupo(leadsQuery, "sem-origem"),
                _ => leadsQuery.Where(OrigemLead.DeTrafegoPagoInclusiveNotion),
            };
            var total = await consulta.CountAsync(ct);
            var linhas = await consulta.OrderByDescending(l => l.CriadoEm).Take(MaximoDoDetalhe)
                .Select(l => new RelatorioDetalheItemDto(l.Id, l.NomeOuRazaoSocial, l.Responsavel != null ? l.Responsavel.NomeCompleto : null, l.CriadoEm,
                    l.Etapa != null ? l.Etapa.Nome : null, l.TipoIndicacao, null, l.Placa, l.Telefone))
                .ToListAsync(ct);
            var titulo = tipo switch
            {
                "perdidos" => "Leads perdidos", "trafego-leads" => "Leads de tráfego pago", "indicacao-leads" => "Cards de indicação",
                "sem-origem-leads" => "Leads sem origem de anúncio identificada", _ => "Leads recebidos (tráfego pago)",
            };
            return new RelatorioDetalheDto(titulo, total, "Chegada", null, linhas);
        }

        // Vendas (e os valores que somam nos cards de adesão, mensalidade, rastreador, vistoria e indicação).
        var vendas = tipo switch
        {
            "adesao" => vendasQuery.Where(o => o.PagamentoAdesao > 0),
            "mensalidade" => vendasQuery.Where(o => o.Mensalidade > 0),
            "rastreador" => vendasQuery.Where(o => o.Veiculo != null && o.Veiculo.Rastreador > 0),
            "vistoria" => vendasQuery.Where(o => o.Veiculo != null && o.Veiculo.ValorVistoria > 0),
            "indicacao" => vendasQuery.Where(o => o.Indicacao == true || o.ValorIndicacao > 0),
            "trafego-vendas" => ComLeadEm(vendasQuery, IdsDoGrupo("trafego")),
            "indicacao-vendas" => ComLeadEm(vendasQuery, IdsDoGrupo("indicacao")),
            "sem-origem-vendas" => ComLeadEm(vendasQuery, IdsDoGrupo("sem-origem")),
            _ => vendasQuery,
        };
        var (tituloVendas, rotuloValor) = tipo switch
        {
            "adesao" => ("Vendas com adesão", "Adesão"),
            "mensalidade" => ("Vendas com mensalidade", "Mensalidade"),
            "rastreador" => ("Vendas com rastreador", "Rastreador"),
            "vistoria" => ("Vendas com vistoria", "Vistoria"),
            "indicacao" => ("Vendas com indicação", "Valor da indicação"),
            "trafego-vendas" => ("Vendas de tráfego pago", "Adesão"),
            "indicacao-vendas" => ("Vendas de indicação", "Adesão"),
            "sem-origem-vendas" => ("Vendas sem origem de anúncio identificada", "Adesão"),
            _ => ("Vendas concluídas", "Adesão"),
        };
        var totalVendas = await vendas.CountAsync(ct);
        var brutas = await vendas.OrderByDescending(o => o.AtivoEm ?? o.DataEfetivaFechamento).Take(MaximoDoDetalhe)
            .Select(o => new
            {
                o.LeadId, Cliente = o.Lead.NomeOuRazaoSocial, Consultor = o.Responsavel.NomeCompleto, o.AtivoEm, o.DataEfetivaFechamento,
                Etapa = o.Lead.Etapa != null ? o.Lead.Etapa.Nome : null, Canal = o.TipoIndicacao ?? o.Lead.TipoIndicacao,
                o.PagamentoAdesao, o.Mensalidade, o.ValorIndicacao, Rastreador = o.Veiculo != null ? o.Veiculo.Rastreador : null,
                Vistoria = o.Veiculo != null ? o.Veiculo.ValorVistoria : null, o.Lead.Placa, o.Lead.Telefone,
            })
            .ToListAsync(ct);
        var itens = brutas.Select(o => new RelatorioDetalheItemDto(
                o.LeadId, o.Cliente, o.Consultor, DataDaVenda(o.AtivoEm, o.DataEfetivaFechamento), o.Etapa, o.Canal,
                tipo switch
                {
                    "mensalidade" => o.Mensalidade, "rastreador" => o.Rastreador, "vistoria" => o.Vistoria, "indicacao" => o.ValorIndicacao, _ => o.PagamentoAdesao,
                },
                o.Placa, o.Telefone))
            .ToList();
        return new RelatorioDetalheDto(tituloVendas, totalVendas, "Venda", rotuloValor, itens);
    }

    /// <summary>
    /// As consultas de leads e de vendas do relatório com todos os filtros aplicados. É a mesma base dos totais e do detalhe dos cards (ObterDetalheAsync): o número do card
    /// e a lista que abre ao clicar nele saem da mesma consulta.
    /// </summary>
    private (IQueryable<CrmLead> Leads, IQueryable<CrmOpportunity> Vendas) MontarConsultas(
        DateOnly inicio, DateOnly fim, List<Guid>? visiveis, Guid? consultorId, List<Guid>? etapaIds, RelatorioFiltroExtra extras, List<string> tiposIndicacao)
    {
        // Datas do relatório em horário de Brasília (UTC-3), como o resto do CRM.
        static DateTimeOffset InicioDoDia(DateOnly d) => new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddHours(3);
        // Chegada e venda têm datas próprias (como no Notion); sem elas valem o período principal.
        var chegadaDe = InicioDoDia(extras.ChegadaInicio ?? inicio);
        var chegadaAte = InicioDoDia((extras.ChegadaFim ?? fim).AddDays(1));
        // A venda conta no dia em que foi ATIVADA ("Ativo em", um dia gravado à meia-noite UTC); sem ativação, vale a data da venda em
        // Brasília — a mesma regra do painel da TV e do filtro de venda do quadro de leads.
        var vendaDe = InicioDoDia(extras.VendaInicio ?? inicio);
        var vendaAte = InicioDoDia((extras.VendaFim ?? fim).AddDays(1));
        var ativacaoDe = new DateTimeOffset((extras.VendaInicio ?? inicio).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var ativacaoAte = new DateTimeOffset((extras.VendaFim ?? fim).AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        var leadsQuery = db.CrmLeads.AsNoTracking()
            // Veículo adicional repete o cliente: não é um lead a mais.
            .Where(l => !l.Arquivado && l.VeiculoAdicionalDeLeadId == null).Where(OrigemLead.ChegouNoPeriodo(chegadaDe, chegadaAte));
        var vendasQuery = db.CrmOpportunities.AsNoTracking()
            .Where(o => !o.Arquivado && o.Etapa.Tipo == TipoEtapaPipeline.Ganho
                && ((o.AtivoEm != null && o.AtivoEm >= ativacaoDe && o.AtivoEm < ativacaoAte)
                    || (o.AtivoEm == null && o.DataEfetivaFechamento >= vendaDe && o.DataEfetivaFechamento < vendaAte)));
        // Se o gestor escolheu a data de chegada, as vendas também são só dos leads que chegaram nela.
        if (extras.ChegadaInicio is not null || extras.ChegadaFim is not null)
        {
            vendasQuery = vendasQuery.Where(o => db.CrmLeads.Where(OrigemLead.ChegouNoPeriodo(chegadaDe, chegadaAte)).Select(l => l.Id).Contains(o.LeadId));
        }
        if (visiveis is not null)
        {
            leadsQuery = leadsQuery.Where(l => visiveis.Contains(l.ResponsavelId ?? Guid.Empty));
            vendasQuery = vendasQuery.Where(o => visiveis.Contains(o.ResponsavelId));
        }

        if (consultorId is { } consultor)
        {
            leadsQuery = leadsQuery.Where(l => l.ResponsavelId == consultor);
            vendasQuery = vendasQuery.Where(o => o.ResponsavelId == consultor);
        }
        if (etapaIds is not null)
        {
            var semEtapa = etapaIds.Contains(Guid.Empty);
            var ids = etapaIds.Where(id => id != Guid.Empty).Select(id => (Guid?)id).ToList();
            leadsQuery = leadsQuery.Where(l => (l.EtapaId != null && ids.Contains(l.EtapaId)) || (semEtapa && l.EtapaId == null));
            vendasQuery = vendasQuery.Where(o => (o.Lead.EtapaId != null && ids.Contains(o.Lead.EtapaId)) || (semEtapa && o.Lead.EtapaId == null));
        }

        if (extras.Indicacao is { } indicacao)
        {
            // Mesma regra do quadro de leads: indicação = qualquer tipo que não seja "Lead". Na venda, também vale o campo da própria venda.
            // (cadastro manual sem tipo também é indicação, como no quadro: ver NotionEtapaLead.EhIndicacao)
            leadsQuery = indicacao
                ? leadsQuery.Where(l => (l.TipoIndicacao == null || l.TipoIndicacao.ToLower() != "lead") && (l.CriadoManualmente || (l.TipoIndicacao != null && l.TipoIndicacao != "")))
                : leadsQuery.Where(l => (l.TipoIndicacao != null && l.TipoIndicacao.ToLower() == "lead") || (!l.CriadoManualmente && (l.TipoIndicacao == null || l.TipoIndicacao == "")));
            vendasQuery = indicacao
                ? vendasQuery.Where(o => o.Indicacao == true || o.ValorIndicacao > 0)
                : vendasQuery.Where(o => o.Indicacao != true && !(o.ValorIndicacao > 0));
        }
        if (tiposIndicacao.Count > 0)
        {
            leadsQuery = leadsQuery.Where(l => l.TipoIndicacao != null && tiposIndicacao.Contains(l.TipoIndicacao.ToLower()));
            vendasQuery = vendasQuery.Where(o => (o.TipoIndicacao != null && tiposIndicacao.Contains(o.TipoIndicacao.ToLower()))
                || (o.Lead.TipoIndicacao != null && tiposIndicacao.Contains(o.Lead.TipoIndicacao.ToLower())));
        }

        return (leadsQuery, vendasQuery);
    }

    /// <summary>
    /// Quem o relatório mostra: o que o usuário já enxerga e, se o cadastro dele tem "ver outras regionais no Relatório comercial" desligado (só vale para visão
    /// total), apenas os consultores da regional do próprio cadastro. Sem regional no cadastro, não há o que restringir.
    /// </summary>
    private async Task<(List<Guid>? Visiveis, Guid? RegionalFixa)> VisiveisDoRelatorioAsync(CancellationToken ct)
    {
        var visiveis = await equipe.ObterVendedoresVisiveisAsync(ct);
        if (!currentUser.TemVisaoTotal) return (visiveis, null);
        var eu = await db.Users.AsNoTracking().Where(u => u.Id == currentUser.UserId).Select(u => new { u.VerOutrasRegionaisNoRelatorio, u.RegionalId }).FirstOrDefaultAsync(ct);
        if (eu is null || eu.VerOutrasRegionaisNoRelatorio || eu.RegionalId is not { } minha) return (visiveis, null);
        var daMinha = await db.Users.AsNoTracking().Where(u => u.RegionalId == minha).Select(u => u.Id).ToListAsync(ct);
        return (visiveis is null ? daMinha : visiveis.Intersect(daMinha).ToList(), minha);
    }

    private async Task<RelatorioComercialDto> CalcularAsync(DateOnly inicio, DateOnly fim, List<Guid>? visiveis, Guid? consultorId, List<Guid>? etapaIds,
        RelatorioFiltroExtra extras, List<string> tiposIndicacao, CancellationToken ct)
    {
        var (leadsQuery, vendasQuery) = MontarConsultas(inicio, fim, visiveis, consultorId, etapaIds, extras, tiposIndicacao);

        var totalDeCards = await leadsQuery.CountAsync(ct);
        // "Leads recebidos" = só o que é lead de tráfego pago (CRM e Notion), a mesma regra dos leads "pegos" do painel da TV; indicação, cadastro manual e
        // cards sem sinal de anúncio não entram nos totais, gráficos e desempenho por vendedor (aparecem em "Conversão — indicação" e "sem origem").
        var leads = await leadsQuery.Where(OrigemLead.DeTrafegoPagoInclusiveNotion)
            // Mesma data de OrigemLead.ChegouNoPeriodo: lead do tráfego pago do sistema vale pela atribuição ao responsável atual; os demais, pela chegada.
            .Select(l => new LeadLinha(
                l.ConsentimentoOrigem != OrigemLead.MarcadorMigracaoNotion && l.ConsentimentoOrigem != OrigemLead.MarcadorSincronizacaoNotion
                    && (l.MetaLeadId != null || l.ConsentimentoOrigem == OrigemLead.MarcadorFormularioSite)
                    ? (l.ResponsavelAtribuidoEm ?? l.CriadoEm)
                    : l.CriadoEm,
                l.Origem, l.Estado, l.ProdutoInteresse, l.ResponsavelId,
                l.Responsavel != null ? l.Responsavel.NomeCompleto : null, l.Etapa != null && l.Etapa.Nome == NotionEtapaLead.Perdido,
                l.EtapaId, l.Etapa != null ? l.Etapa.Nome : null, l.Etapa != null ? l.Etapa.Ordem : int.MaxValue))
            .ToListAsync(ct);

        var vendas = (await vendasQuery
            .Select(o => new
            {
                o.AtivoEm,
                o.DataEfetivaFechamento,
                o.ResponsavelId,
                Responsavel = o.Responsavel.NomeCompleto,
                Origem = o.Lead.Origem,
                Estado = o.Estado ?? o.Lead.Estado,
                Produto = o.ProdutoOuServico,
                o.PagamentoAdesao,
                o.Mensalidade,
                Rastreador = o.Veiculo != null ? o.Veiculo.Rastreador : null,
                Vistoria = o.Veiculo != null ? o.Veiculo.ValorVistoria : null,
                Fipe = o.Veiculo != null ? o.Veiculo.Fipe : null,
                o.ValorIndicacao,
                o.Indicacao,
            })
            .ToListAsync(ct))
            .Select(o => new VendaLinha(DataDaVenda(o.AtivoEm, o.DataEfetivaFechamento), o.ResponsavelId, o.Responsavel, o.Origem, o.Estado, o.Produto,
                o.PagamentoAdesao ?? 0m, o.Mensalidade ?? 0m, o.Rastreador ?? 0m, o.Vistoria ?? 0m, o.ValorIndicacao ?? 0m,
                o.Indicacao == true || o.ValorIndicacao > 0, o.Fipe))
            .ToList();

        // Conversão por origem: tráfego pago (CRM e Notion, sem o anúncio que fechou como indicação) e indicação (card de indicação). Leads = os que chegaram
        // no período; vendas = as do período desses mesmos grupos de clientes. Mesma classificação da Visão geral.
        var leadsDeAnuncio = await leadsQuery.CountAsync(OrigemLead.DeTrafegoPagoInclusiveNotion, ct);
        var leadsDeAnuncioQueSaoIndicacao = await leadsQuery.Where(OrigemLead.DeTrafegoPagoInclusiveNotion).CountAsync(OrigemLead.CardDeIndicacao, ct);
        var leadsIndicacao = await leadsQuery.CountAsync(OrigemLead.CardDeIndicacao, ct);
        var vendasDeAnuncio = await vendasQuery.CountAsync(o => db.CrmLeads.Where(OrigemLead.DeTrafegoPagoInclusiveNotion).Select(l => l.Id).Contains(o.LeadId), ct);
        var vendasDeAnuncioQueSaoIndicacao = await vendasQuery.CountAsync(o => db.CrmLeads.Where(OrigemLead.DeTrafegoPagoInclusiveNotion)
            .Where(OrigemLead.CardDeIndicacao).Select(l => l.Id).Contains(o.LeadId), ct);
        var vendasIndicacaoCard = await vendasQuery.CountAsync(o => db.CrmLeads.Where(OrigemLead.CardDeIndicacao).Select(l => l.Id).Contains(o.LeadId), ct);
        var leadsTrafego = leadsDeAnuncio - leadsDeAnuncioQueSaoIndicacao;
        var vendasTrafego = vendasDeAnuncio - vendasDeAnuncioQueSaoIndicacao;
        var totais = Totais(leads, vendas) with
        {
            LeadsTrafegoPago = leadsTrafego, VendasTrafegoPago = vendasTrafego, TaxaConversaoTrafegoPago = Taxa(vendasTrafego, leadsTrafego),
            LeadsIndicacaoCard = leadsIndicacao, VendasIndicacaoCard = vendasIndicacaoCard, TaxaConversaoIndicacaoCard = Taxa(vendasIndicacaoCard, leadsIndicacao),
            LeadsSemOrigemIdentificada = totalDeCards - leadsTrafego - leadsIndicacao,
            VendasSemOrigemIdentificada = vendas.Count - vendasTrafego - vendasIndicacaoCard,
        };

        return new RelatorioComercialDto(
            inicio, fim,
            totais,
            PorMes(leads, vendas),
            PorSemana(leads, vendas),
            PorOrigem(leads, vendas),
            PorVendedor(leads, vendas),
            PorEstado(leads, vendas),
            PorFaixaFipe(vendas),
            PorProduto(leads, vendas),
            await MarketingDoNotionAsync(inicio, fim, ct),
            PorEtapa(leads));
    }

    /// <summary>Instante que põe a venda no dia certo: "Ativo em" à meia-noite UTC vira meio-dia do mesmo dia; sem ativação, a data da venda.</summary>
    private static DateTimeOffset DataDaVenda(DateTimeOffset? ativoEm, DateTimeOffset? dataDaVenda) =>
        ativoEm is { } a ? (a.UtcDateTime.TimeOfDay == TimeSpan.Zero ? a.AddHours(12) : a) : dataDaVenda!.Value;

    private static DateTime Brasilia(DateTimeOffset data) => data.UtcDateTime.AddHours(-3);

    private static decimal Taxa(int vendas, int leads) => leads == 0 ? 0m : Math.Round(100m * vendas / leads, 1);

    private static string Rotulo(string? valor, string padrao = "Não informado") => string.IsNullOrWhiteSpace(valor) ? padrao : valor.Trim();

    private static RelatorioTotaisDto Totais(List<LeadLinha> leads, List<VendaLinha> vendas)
    {
        var mensalidade = vendas.Sum(v => v.Mensalidade);
        var comMensalidade = vendas.Count(v => v.Mensalidade > 0);
        return new RelatorioTotaisDto(
            leads.Count, leads.Count(l => l.Perdido), vendas.Count, Taxa(vendas.Count, leads.Count),
            vendas.Sum(v => v.Adesao), mensalidade, comMensalidade == 0 ? 0m : Math.Round(mensalidade / comMensalidade, 2),
            vendas.Sum(v => v.Rastreador), vendas.Sum(v => v.Vistoria), vendas.Sum(v => v.Indicacao), vendas.Count(v => v.Indicada));
    }

    private static List<RelatorioMesDto> PorMes(List<LeadLinha> leads, List<VendaLinha> vendas)
    {
        var leadsPorMes = leads.ToLookup(l => Brasilia(l.Chegada).ToString("yyyy-MM", CultureInfo.InvariantCulture));
        var vendasPorMes = vendas.ToLookup(v => Brasilia(v.Data).ToString("yyyy-MM", CultureInfo.InvariantCulture));
        return leadsPorMes.Select(g => g.Key).Union(vendasPorMes.Select(g => g.Key)).Order()
            .Select(mes =>
            {
                var l = leadsPorMes[mes].ToList();
                var v = vendasPorMes[mes].ToList();
                return new RelatorioMesDto(mes, l.Count, l.Count(x => x.Perdido), v.Count,
                    v.Sum(x => x.Adesao), v.Sum(x => x.Mensalidade), v.Sum(x => x.Rastreador), v.Sum(x => x.Vistoria), v.Sum(x => x.Indicacao));
            })
            .ToList();
    }

    private static DateOnly SegundaDaSemana(DateTimeOffset data)
    {
        var dia = DateOnly.FromDateTime(Brasilia(data));
        return dia.AddDays(-(((int)dia.DayOfWeek + 6) % 7));
    }

    private static List<RelatorioSemanaDto> PorSemana(List<LeadLinha> leads, List<VendaLinha> vendas)
    {
        var leadsPorSemana = leads.ToLookup(l => SegundaDaSemana(l.Chegada));
        var vendasPorSemana = vendas.ToLookup(v => SegundaDaSemana(v.Data));
        return leadsPorSemana.Select(g => g.Key).Union(vendasPorSemana.Select(g => g.Key)).Order()
            .Select(semana => new RelatorioSemanaDto(semana, leadsPorSemana[semana].Count(), leadsPorSemana[semana].Count(l => l.Perdido),
                vendasPorSemana[semana].Count(), vendasPorSemana[semana].Sum(v => v.Adesao)))
            .ToList();
    }

    private static List<RelatorioOrigemDto> PorOrigem(List<LeadLinha> leads, List<VendaLinha> vendas)
    {
        var l = leads.ToLookup(x => Rotulo(x.Origem));
        var v = vendas.ToLookup(x => Rotulo(x.Origem));
        return l.Select(g => g.Key).Union(v.Select(g => g.Key))
            .Select(o => new RelatorioOrigemDto(o, l[o].Count(), v[o].Count(), v[o].Sum(x => x.Adesao)))
            .OrderByDescending(o => o.Leads + o.Vendas)
            .ToList();
    }

    private static List<RelatorioVendedorDto> PorVendedor(List<LeadLinha> leads, List<VendaLinha> vendas)
    {
        var l = leads.Where(x => x.ResponsavelId != null).ToLookup(x => x.ResponsavelId!.Value);
        var v = vendas.ToLookup(x => x.ResponsavelId);
        var nomes = leads.Where(x => x.ResponsavelId != null).GroupBy(x => x.ResponsavelId!.Value).ToDictionary(g => g.Key, g => g.First().Responsavel);
        foreach (var venda in vendas) nomes[venda.ResponsavelId] = venda.Responsavel;

        return l.Select(g => g.Key).Union(v.Select(g => g.Key))
            .Select(id => new RelatorioVendedorDto(id, Rotulo(nomes.GetValueOrDefault(id), "Sem nome"), l[id].Count(), v[id].Count(),
                v[id].Sum(x => x.Adesao), v[id].Sum(x => x.Mensalidade), Taxa(v[id].Count(), l[id].Count())))
            .OrderByDescending(x => x.Vendas).ThenByDescending(x => x.Leads)
            .ToList();
    }

    private static List<RelatorioEstadoDto> PorEstado(List<LeadLinha> leads, List<VendaLinha> vendas)
    {
        static string Uf(string? estado) => string.IsNullOrWhiteSpace(estado) ? "—" : estado.Trim().ToUpperInvariant();
        var l = leads.ToLookup(x => Uf(x.Estado));
        var v = vendas.ToLookup(x => Uf(x.Estado));
        return l.Select(g => g.Key).Union(v.Select(g => g.Key))
            .Select(uf => new RelatorioEstadoDto(uf, l[uf].Count(), v[uf].Count()))
            .OrderByDescending(e => e.Vendas).ThenByDescending(e => e.Leads)
            .ToList();
    }

    private static List<RelatorioFaixaFipeDto> PorFaixaFipe(List<VendaLinha> vendas)
    {
        var comFipe = vendas.Where(v => v.Fipe is > 0).Select(v => v.Fipe!.Value).ToList();
        return FaixasFipe
            .Select((faixa, i) =>
            {
                var piso = i == 0 ? 0m : FaixasFipe[i - 1].Ate;
                return new RelatorioFaixaFipeDto(faixa.Rotulo, comFipe.Count(f => f > piso && f <= faixa.Ate));
            })
            .ToList();
    }

    private static List<RelatorioEtapaDto> PorEtapa(List<LeadLinha> leads) =>
        leads.GroupBy(l => new { l.EtapaId, Nome = l.Etapa ?? "Sem etapa", l.EtapaOrdem })
            .OrderBy(g => g.Key.EtapaOrdem).ThenBy(g => g.Key.Nome)
            .Select(g => new RelatorioEtapaDto(g.Key.EtapaId, g.Key.Nome, g.Count()))
            .ToList();

    private static List<RelatorioProdutoDto> PorProduto(List<LeadLinha> leads, List<VendaLinha> vendas)
    {
        var l = leads.ToLookup(x => Rotulo(x.Produto));
        var v = vendas.ToLookup(x => Rotulo(x.Produto));
        return l.Select(g => g.Key).Union(v.Select(g => g.Key))
            .Select(p => new RelatorioProdutoDto(p, l[p].Count(), v[p].Count(), v[p].Sum(x => x.Adesao)))
            .OrderByDescending(p => p.Leads + p.Vendas)
            .ToList();
    }

    private async Task<List<RelatorioMarketingMesDto>> MarketingDoNotionAsync(DateOnly inicio, DateOnly fim, CancellationToken ct)
    {
        var primeiroMes = new DateOnly(inicio.Year, inicio.Month, 1);
        var meses = await db.CrmControleMarketingMeses.AsNoTracking()
            .Where(m => m.Mes >= primeiroMes && m.Mes <= fim)
            .OrderBy(m => m.Mes)
            .ToListAsync(ct);

        return meses.Select(m =>
        {
            var gastos = m.FacebookAds + m.GoogleAds + m.FerramentasMarketing + m.Backlinks;
            return new RelatorioMarketingMesDto(
                m.Mes.ToString("yyyy-MM", CultureInfo.InvariantCulture), m.LeadsGerados, m.VendasQuantidade,
                m.FacebookAds, m.GoogleAds, m.FerramentasMarketing, m.Backlinks, gastos, m.FaturamentoTotal, m.MetaFaturamento,
                m.LeadsGerados > 0 ? Math.Round(gastos / m.LeadsGerados, 2) : null,
                gastos > 0 ? Math.Round(m.FaturamentoTotal / gastos, 2) : null,
                m.LeadsGerados > 0 ? Taxa(m.VendasQuantidade, m.LeadsGerados) : null);
        }).ToList();
    }
}
