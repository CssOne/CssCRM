using System.Globalization;
using System.Text;
using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace CssVision.Web.Services.Crm;

/// <summary>
/// Painel de tráfego pago pro papel Marketing (e Admin) — visão sem escopo de carteira/equipe,
/// já que aqui o interesse é a base inteira por origem/campanha, não "meus leads".
/// <para>
/// O banco filtra só pelo período (este e o anterior, para comparar) e pela fonte; o resto dos
/// filtros e todos os agrupamentos são feitos em memória numa projeção enxuta — um período de
/// tráfego são poucos milhares de linhas, e assim "O que?" compara sem acento/maiúsculas
/// ("AGV ELETRICO" do Notion = "AGV ELÉTRICO").
/// </para>
/// </summary>
public sealed class MarketingService(ApplicationDbContext db, ICurrentUserService? currentUser = null, IMemoryCache? cache = null, ICrmEventHub? eventos = null) : IMarketingService
{
    private const string SemRegional = "Sem regional";

    /// <summary>
    /// Administrador com regionais ocultas não vê os leads delas: o filtro passa a levar as regionais ocultas (e entra na chave do cache),
    /// qualquer que seja a requisição. Quem não tem regional oculta não muda nada (e o campo vindo da requisição é ignorado).
    /// </summary>
    private async Task<MarketingFilterRequest> AplicarEscopoAsync(MarketingFilterRequest filtro, CancellationToken ct)
    {
        string[]? ocultas = null;
        if (currentUser is not null && await EscopoRegional.OcultasAsync(db, currentUser, ct) is { Count: > 0 } ids)
        {
            ocultas = (await db.CrmRegionais.AsNoTracking().Where(r => ids.Contains(r.Id)).Select(r => r.Nome).ToListAsync(ct)).Order().ToArray();
        }
        return filtro with { RegionaisOcultas = ocultas };
    }

    /// <summary>Nome (prefixo) das colunas de venda ganha do quadro de leads — ver LeadsKanban.tsx/CrmSeeder.cs.</summary>
    private const string EtapaVendaConcluida = "Venda concluída";
    private const string EtapaPerdido = "Perdido";
    private const string EtapaNaoFazemos = "Não fazemos";
    private const string SemEtapa = "Sem etapa";
    private const string NaoInformado = "Não informado";

    /// <summary>Opções do campo "O que?" (mesmas de opcoesLead.ts), para exibir sempre com a mesma grafia.</summary>
    private static readonly string[] OQueConhecidos = ["AGV", "AGV ELÉTRICO", "AGV TRUCK"];

    private static readonly TimeSpan Brasilia = TimeSpan.FromHours(-3);

    public async Task<MarketingDashboardDto> ObterAsync(MarketingFilterRequest filtro, CancellationToken ct)
    {
        filtro = await AplicarEscopoAsync(filtro, ct);
        return await RespostaEmCache.ObterAsync(cache, eventos, "trafego", filtro, () => CalcularAsync(filtro, ct));
    }

    /// <summary>Lista "Últimos leads" paginada (mais recentes primeiro), com os mesmos filtros do painel.</summary>
    public async Task<PagedResult<MarketingLeadItemDto>> ListarLeadsAsync(MarketingFilterRequest filtro, int pagina, int tamanhoPagina, CancellationToken ct)
    {
        pagina = Math.Max(1, pagina);
        tamanhoPagina = Math.Clamp(tamanhoPagina, 5, 100);
        var carga = await CarregarAsync(await AplicarEscopoAsync(filtro, ct), ct);
        return new PagedResult<MarketingLeadItemDto>
        {
            Itens = carga.Leads
                .OrderByDescending(l => l.CriadoEm)
                .Skip((pagina - 1) * tamanhoPagina)
                .Take(tamanhoPagina)
                .Select(l => new MarketingLeadItemDto(l.Id, l.Nome, l.Telefone, l.Origem, l.Campanha, l.UtmSource, l.UtmMedium,
                    l.SemEtapaMarcada ? null : l.EtapaNome, l.ResponsavelNome, l.CriadoEm, l.OQue, l.Estado, l.Canal))
                .ToList(),
            Pagina = pagina,
            TamanhoPagina = tamanhoPagina,
            TotalRegistros = carga.Leads.Count,
        };
    }

    /// <summary>Leads do período (já filtrados), do período anterior e as opções dos filtros.</summary>
    private sealed record Carga(
        DateOnly Inicio, DateOnly Fim, int Dias, List<Linha> Leads, List<Linha> Anteriores, MarketingOpcoesDto Opcoes, List<Linha> LeadsTodasRegionais);

    /// <summary>Compartilhada entre o painel e a lista paginada — trocar de página não recarrega a base.</summary>
    private Task<Carga> CarregarAsync(MarketingFilterRequest filtro, CancellationToken ct) =>
        RespostaEmCache.ObterAsync(cache, eventos, "trafego-base", filtro, () => CarregarSemCacheAsync(filtro, ct));

    private sealed record Linha(
        Guid Id, string Nome, string? Telefone, bool SemContato, string? Origem, string? Campanha, string? UtmSource, string? UtmMedium,
        string OQue, string? Estado, string Canal, string EtapaNome, string? EtapaCor, int EtapaOrdem, Guid? ResponsavelId, string? ResponsavelNome,
        DateTimeOffset CriadoEm, DateTimeOffset Chegada, string? MotivoPerda, string Regional = SemRegional)
    {
        public bool Ganho => EtapaNome.StartsWith(EtapaVendaConcluida, StringComparison.Ordinal);
        public bool Perdido => EtapaNome == EtapaPerdido;
        public bool NaoFazemos => EtapaNome == EtapaNaoFazemos;
        public bool SemEtapaMarcada => EtapaNome == SemEtapa;
        public bool EmAndamento => !Ganho && !Perdido && !NaoFazemos && !SemEtapaMarcada;
    }

    private async Task<Carga> CarregarSemCacheAsync(MarketingFilterRequest filtro, CancellationToken ct)
    {
        var hoje = DateOnly.FromDateTime(DateTime.UtcNow.Add(Brasilia));
        var inicioPeriodo = filtro.DataInicio ?? hoje.AddDays(-29);
        var fimPeriodo = filtro.DataFim ?? hoje;
        if (fimPeriodo < inicioPeriodo) (inicioPeriodo, fimPeriodo) = (fimPeriodo, inicioPeriodo);
        var dias = fimPeriodo.DayNumber - inicioPeriodo.DayNumber + 1;
        var inicioAnterior = inicioPeriodo.AddDays(-dias);

        // Dias no horário de Brasília.
        var inicioUtc = InicioDoDia(inicioPeriodo);
        var fimUtc = InicioDoDia(fimPeriodo.AddDays(1));
        var inicioAnteriorUtc = InicioDoDia(inicioAnterior);

        var query = db.CrmLeads.AsNoTracking().Where(l => !l.Arquivado && l.CriadoEm >= inicioAnteriorUtc && l.CriadoEm < fimUtc);
        query = (filtro.Fonte ?? "trafego").ToLowerInvariant() switch
        {
            "todos" => query,
            "novo" => query.Where(OrigemLead.VeioDoTrafegoPago),
            _ => query.Where(OrigemLead.DeTrafegoPagoInclusiveNotion),
        };

        var brutas = await query
            .Select(l => new
            {
                l.Id, l.NomeOuRazaoSocial, l.Telefone, l.Telefone2, l.Origem, l.Campanha, l.UtmSource, l.UtmMedium,
                l.ProdutoInteresse, l.Estado, l.MetaLeadId, l.Gclid, l.MetaClickId, l.ConsentimentoOrigem,
                EtapaNome = l.Etapa != null ? l.Etapa.Nome : null,
                EtapaCor = l.Etapa != null ? l.Etapa.Cor : null,
                EtapaOrdem = l.Etapa != null ? l.Etapa.Ordem : -1,
                l.ResponsavelId,
                ResponsavelNome = l.Responsavel != null ? l.Responsavel.NomeCompleto : null,
                l.CriadoEm, l.ResponsavelAtribuidoEm,
                MotivoPerda = l.MotivoPerda != null ? l.MotivoPerda.Descricao : null,
                RegionalDoLead = l.Regional,
                RegionalDoResponsavel = l.Responsavel != null && l.Responsavel.Regional != null ? l.Responsavel.Regional.Nome : null,
            })
            .ToListAsync(ct);

        var todas = brutas.Select(l => new Linha(
                l.Id, l.NomeOuRazaoSocial, l.Telefone ?? l.Telefone2,
                string.IsNullOrWhiteSpace(l.Telefone) && string.IsNullOrWhiteSpace(l.Telefone2),
                Vazio(l.Origem), Vazio(l.Campanha), l.UtmSource, l.UtmMedium,
                NormalizarOQue(l.ProdutoInteresse), Vazio(l.Estado)?.ToUpperInvariant(),
                Canal(l.MetaLeadId, l.Gclid, l.MetaClickId, l.ConsentimentoOrigem),
                l.EtapaNome ?? SemEtapa, l.EtapaCor, l.EtapaOrdem, l.ResponsavelId, l.ResponsavelNome,
                l.CriadoEm, l.ResponsavelAtribuidoEm ?? l.CriadoEm, l.MotivoPerda,
                // Regional do próprio lead (MG132, MG134...); sem ela, a do consultor responsável.
                Vazio(l.RegionalDoLead)?.ToUpperInvariant() ?? Vazio(l.RegionalDoResponsavel)?.ToUpperInvariant() ?? SemRegional))
            .ToList();

        // Administrador com regionais ocultas: o resto da tela (opções, números, lista) não conhece os leads delas.
        if (Lista(filtro.RegionaisOcultas)?.Select(r => r.ToUpperInvariant()).ToHashSet() is { } ocultas)
        {
            todas = todas.Where(l => !ocultas.Contains(l.Regional)).ToList();
        }

        var doPeriodo = todas.Where(l => l.CriadoEm >= inicioUtc).ToList();
        // Opções dos filtros: tudo o que existe no período (antes dos demais filtros), para o
        // usuário poder trocar de opção sem ter que limpar os outros filtros.
        var opcoes = MontarOpcoes(doPeriodo);

        var filtradas = Filtrar(todas, filtro);
        return new Carga(inicioPeriodo, fimPeriodo, dias,
            filtradas.Where(l => l.CriadoEm >= inicioUtc).ToList(),
            filtradas.Where(l => l.CriadoEm < inicioUtc).ToList(),
            opcoes,
            // Para comparar as regionais lado a lado: todos os filtros, menos o de regional.
            Filtrar(todas, filtro with { Regional = null }).Where(l => l.CriadoEm >= inicioUtc).ToList());
    }

    private async Task<MarketingDashboardDto> CalcularAsync(MarketingFilterRequest filtro, CancellationToken ct)
    {
        var (inicioPeriodo, fimPeriodo, dias, leads, anteriores, opcoes, leadsTodasRegionais) = await CarregarAsync(filtro, ct);

        var ids = leads.Select(l => l.Id).ToList();
        var (tempoMedio, tempoPorConsultor) = await TempoPrimeiroContatoAsync(leads, ct);
        var vendas = await VendasAsync(ids, ct);

        var ganhos = leads.Count(l => l.Ganho);
        var perdidos = leads.Count(l => l.Perdido);
        var indicadores = new MarketingIndicadoresDto(
            leads.Count,
            leads.Count(l => l.SemEtapaMarcada),
            ganhos,
            perdidos,
            Taxa(ganhos, ganhos + perdidos),
            leads.Count(l => l.SemContato),
            leads.Count(l => l.EmAndamento),
            leads.Count(l => l.NaoFazemos),
            leads.Count(l => l.ResponsavelId is null),
            Taxa(ganhos, leads.Count),
            vendas.Sum(v => v.Adesao ?? 0m),
            vendas.Count(v => v.Mensalidade is > 0) == 0 ? 0m : Math.Round(vendas.Where(v => v.Mensalidade is > 0).Average(v => v.Mensalidade!.Value), 2),
            tempoMedio,
            anteriores.Count,
            anteriores.Count(l => l.Ganho),
            Math.Round((decimal)leads.Count / dias, 1));

        // Leads por dia, total e por "O que?" (séries do gráfico empilhado).
        var diasDoPeriodo = Enumerable.Range(0, dias).Select(inicioPeriodo.AddDays).ToList();
        var porDia = leads.GroupBy(l => DiaBrasilia(l.CriadoEm)).ToDictionary(g => g.Key, g => g.ToList());
        var evolucao = diasDoPeriodo
            .Select(d => porDia.TryGetValue(d, out var doDia)
                ? new MarketingEvolucaoDto(d.ToString("dd/MM"), doDia.Count, doDia.Count(l => l.Ganho))
                : new MarketingEvolucaoDto(d.ToString("dd/MM"), 0))
            .ToList();
        var evolucaoPorOQue = leads.Select(l => l.OQue).Distinct().OrderBy(OrdemOQue)
            .Select(oQue => new MarketingSerieDto(oQue, diasDoPeriodo
                .Select(d => porDia.TryGetValue(d, out var doDia) ? doDia.Count(l => l.OQue == oQue) : 0).ToList()))
            .ToList();

        var adesaoPorLead = vendas.GroupBy(v => v.LeadId).ToDictionary(g => g.Key, g => g.Sum(v => v.Adesao ?? 0m));

        var porCampanha = leads
            .Where(l => l.Campanha is not null)
            .GroupBy(l => l.Campanha!)
            .Select(g =>
            {
                var gan = g.Count(l => l.Ganho);
                var per = g.Count(l => l.Perdido);
                return new MarketingCampanhaDto(g.Key, g.GroupBy(l => l.Origem).OrderByDescending(o => o.Count()).First().Key,
                    g.Count(), gan, Taxa(gan, gan + per), g.Max(l => l.CriadoEm), per, g.Count(l => l.SemEtapaMarcada),
                    g.Sum(l => adesaoPorLead.GetValueOrDefault(l.Id)));
            })
            .OrderByDescending(c => c.TotalLeads)
            .Take(100)
            .ToList();

        var porConsultor = leads
            .GroupBy(l => new { l.ResponsavelId, Nome = l.ResponsavelNome ?? "Sem responsável" })
            .Select(g =>
            {
                var gan = g.Count(l => l.Ganho);
                var per = g.Count(l => l.Perdido);
                return new MarketingConsultorDto(g.Key.ResponsavelId, g.Key.Nome, g.Count(), g.Count(l => l.SemEtapaMarcada),
                    g.Count(l => l.EmAndamento), gan, per, Taxa(gan, gan + per), g.Count(l => l.SemContato),
                    g.Key.ResponsavelId is { } id && tempoPorConsultor.TryGetValue(id, out var horas) ? horas : null);
            })
            .OrderByDescending(c => c.TotalLeads)
            .ToList();

        var porConsultorMensal = leads
            .GroupBy(l => new { l.ResponsavelId, Nome = l.ResponsavelNome ?? "Sem responsável", Mes = MesBrasilia(l.CriadoEm) })
            .Select(g => new MarketingConsultorMesDto(g.Key.ResponsavelId, g.Key.Nome, g.Key.Mes, g.Count(), g.Count(l => l.Ganho)))
            .OrderBy(c => c.Mes)
            .ToList();

        var funil = leads
            .GroupBy(l => new { l.EtapaNome, l.EtapaCor, l.EtapaOrdem })
            .OrderBy(g => g.Key.EtapaOrdem)
            .Select(g => new MarketingFunilDto(g.Key.EtapaNome, g.Count(), g.Key.EtapaCor))
            .ToList();

        var porHorario = leads
            .GroupBy(l => { var local = l.CriadoEm.ToOffset(Brasilia); return ((int)local.DayOfWeek, local.Hour); })
            .Select(g => new MarketingHorarioDto(g.Key.Item1, g.Key.Hour, g.Count()))
            .ToList();

        var motivos = leads
            .Where(l => l.Perdido || l.NaoFazemos)
            .GroupBy(l => l.NaoFazemos ? "Não fazemos (veículo)" : l.MotivoPerda ?? NaoInformado)
            .Select(g => new MarketingMotivoPerdaDto(g.Key, g.Count()))
            .OrderByDescending(m => m.Quantidade)
            .Take(12)
            .ToList();

        var porOrigem = Agrupar(leads, l => l.Origem ?? NaoInformado)
            .Select(g => new MarketingOrigemDto(g.Nome, g.TotalLeads, g.Ganhos, g.TaxaConversao))
            .ToList();

        // A lista "Últimos leads" vem paginada por ListarLeadsAsync (/api/marketing/leads).
        IReadOnlyList<MarketingLeadItemDto> ultimos = [];

        return new MarketingDashboardDto(
            indicadores, porOrigem, porCampanha, evolucao, opcoes.Origens, ultimos,
            evolucaoPorOQue,
            Agrupar(leads, l => l.OQue).OrderBy(g => OrdemOQue(g.Nome)).ToList(),
            Agrupar(leads, l => l.Canal),
            Agrupar(leads, l => l.Estado ?? NaoInformado),
            porConsultor, funil, porHorario, motivos, opcoes,
            inicioPeriodo.ToString("yyyy-MM-dd"), fimPeriodo.ToString("yyyy-MM-dd"),
            porConsultorMensal, leads.Count, PorRegional(leadsTodasRegionais));
    }

    /// <summary>MG132, MG134... lado a lado (participação sobre o total de leads de todas as regionais).</summary>
    private static List<MarketingRegionalDto> PorRegional(List<Linha> leads) => leads
        .GroupBy(l => l.Regional)
        .Select(g =>
        {
            var gan = g.Count(l => l.Ganho);
            var per = g.Count(l => l.Perdido);
            return new MarketingRegionalDto(g.Key, g.Count(), g.Count(l => l.SemEtapaMarcada), g.Count(l => l.EmAndamento), gan, per,
                g.Count(l => l.NaoFazemos), Taxa(gan, gan + per), g.Count(l => l.SemContato), g.Count(l => l.ResponsavelId is null),
                leads.Count == 0 ? 0m : Math.Round(100m * g.Count() / leads.Count, 1));
        })
        .OrderBy(r => r.Regional == SemRegional ? 1 : 0).ThenBy(r => r.Regional)
        .ToList();

    private static List<Linha> Filtrar(List<Linha> linhas, MarketingFilterRequest filtro)
    {
        IEnumerable<Linha> q = linhas;
        var oQue = Lista(filtro.OQue)?.Select(NormalizarOQue).ToHashSet();
        if (oQue is not null) q = q.Where(l => oQue.Contains(l.OQue));
        var origens = Lista(filtro.Origem)?.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (origens is not null) q = q.Where(l => origens.Contains(l.Origem ?? NaoInformado));
        var campanhas = Lista(filtro.Campanha)?.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (campanhas is not null) q = q.Where(l => l.Campanha is not null && campanhas.Contains(l.Campanha));
        var canais = Lista(filtro.Canal)?.ToHashSet();
        if (canais is not null) q = q.Where(l => canais.Contains(l.Canal));
        var estados = Lista(filtro.Estado)?.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (estados is not null) q = q.Where(l => estados.Contains(l.Estado ?? NaoInformado));
        var etapas = Lista(filtro.Etapa)?.ToHashSet();
        if (etapas is not null) q = q.Where(l => etapas.Contains(l.EtapaNome));
        var regionais = Lista(filtro.Regional)?.Select(r => r.ToUpperInvariant()).ToHashSet();
        if (regionais is not null) q = q.Where(l => regionais.Contains(l.Regional));
        if (filtro.ResponsavelId is { Length: > 0 } responsaveis)
        {
            var ids = responsaveis.ToHashSet();
            // Guid.Empty = "Sem responsável".
            q = q.Where(l => ids.Contains(l.ResponsavelId ?? Guid.Empty));
        }
        return q.ToList();
    }

    private static MarketingOpcoesDto MontarOpcoes(List<Linha> linhas) => new(
        linhas.Select(l => l.OQue).Distinct().OrderBy(OrdemOQue).ToList(),
        linhas.Select(l => l.Origem ?? NaoInformado).Distinct().Order().ToList(),
        linhas.Where(l => l.Campanha is not null).Select(l => l.Campanha!).Distinct().Order().ToList(),
        linhas.Select(l => l.Canal).Distinct().Order().ToList(),
        linhas.GroupBy(l => l.ResponsavelId ?? Guid.Empty)
            .Select(g => new MarketingConsultorOpcaoDto(g.Key, g.First().ResponsavelNome ?? "Sem responsável"))
            .OrderBy(c => c.Nome).ToList(),
        linhas.Select(l => l.Estado ?? NaoInformado).Distinct().Order().ToList(),
        linhas.GroupBy(l => l.EtapaNome).OrderBy(g => g.First().EtapaOrdem).Select(g => g.Key).ToList(),
        linhas.Select(l => l.Regional).Distinct().OrderBy(r => r == SemRegional ? 1 : 0).ThenBy(r => r).ToList());

    private static List<MarketingGrupoDto> Agrupar(List<Linha> linhas, Func<Linha, string> chave) => linhas
        .GroupBy(chave)
        .Select(g =>
        {
            var gan = g.Count(l => l.Ganho);
            var per = g.Count(l => l.Perdido);
            return new MarketingGrupoDto(g.Key, g.Count(), gan, per, Taxa(gan, gan + per), g.Count(l => l.SemEtapaMarcada));
        })
        .OrderByDescending(g => g.TotalLeads)
        .ToList();

    private sealed record Venda(Guid LeadId, decimal? Adesao, decimal? Mensalidade);

    /// <summary>Vendas (oportunidades ganhas) desses leads — valor de adesão e mensalidade.</summary>
    private async Task<List<Venda>> VendasAsync(List<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return [];
        return await db.CrmOpportunities.AsNoTracking()
            .Where(o => !o.Arquivado && o.Etapa.Tipo == TipoEtapaPipeline.Ganho && ids.Contains(o.LeadId))
            .Select(o => new Venda(o.LeadId, o.PagamentoAdesao, o.Mensalidade))
            .ToListAsync(ct);
    }

    /// <summary>
    /// Mesma regra da Gestão comercial (ManagementService): primeira mudança de etapa no quadro ou
    /// atividade concluída depois de o lead chegar ao consultor atual.
    /// </summary>
    private async Task<(double? Geral, Dictionary<Guid, double> PorConsultor)> TempoPrimeiroContatoAsync(List<Linha> leads, CancellationToken ct)
    {
        var comResponsavel = leads.Where(l => l.ResponsavelId is not null).ToList();
        if (comResponsavel.Count == 0) return (null, []);

        var ids = comResponsavel.Select(l => l.Id).ToList();
        var idsAnulaveis = ids.Select(id => (Guid?)id).ToList();
        var movimentos = await db.CrmAuditLogs.AsNoTracking()
            .Where(a => a.EntidadeTipo == nameof(CrmLead) && a.Acao == "LeadMudouEtapa" && idsAnulaveis.Contains(a.EntidadeId))
            .Select(a => new { LeadId = a.EntidadeId!.Value, a.OcorridoEm })
            .ToListAsync(ct);
        var atividades = await db.CrmActivities.AsNoTracking()
            .Where(a => ids.Contains(a.LeadId) && a.Status == StatusAtividade.Concluida && a.DataHoraConclusao != null)
            .Select(a => new { a.LeadId, OcorridoEm = a.DataHoraConclusao!.Value })
            .ToListAsync(ct);
        var contatos = movimentos.Concat(atividades).GroupBy(c => c.LeadId).ToDictionary(g => g.Key, g => g.Select(c => c.OcorridoEm).ToList());

        var tempos = comResponsavel
            .Select(l => new
            {
                Responsavel = l.ResponsavelId!.Value,
                Primeiro = contatos.TryGetValue(l.Id, out var lista) ? lista.Where(t => t >= l.Chegada).DefaultIfEmpty().Min() : default,
                l.Chegada,
            })
            .Where(t => t.Primeiro != default)
            .Select(t => new { t.Responsavel, Horas = (t.Primeiro - t.Chegada).TotalHours })
            .ToList();
        if (tempos.Count == 0) return (null, []);

        return (Math.Round(tempos.Average(t => t.Horas), 1),
            tempos.GroupBy(t => t.Responsavel).ToDictionary(g => g.Key, g => Math.Round(g.Average(t => t.Horas), 1)));
    }

    public static string Canal(string? metaLeadId, string? gclid, string? metaClickId, string? consentimentoOrigem) =>
        metaLeadId is not null ? MarketingCanais.MetaFormulario
        : consentimentoOrigem == OrigemLead.MarcadorFormularioSite
            ? gclid is not null ? MarketingCanais.GoogleSite : metaClickId is not null ? MarketingCanais.MetaSite : MarketingCanais.Site
        : consentimentoOrigem is OrigemLead.MarcadorMigracaoNotion or OrigemLead.MarcadorSincronizacaoNotion ? MarketingCanais.Notion
        : gclid is not null ? MarketingCanais.GoogleSite
        : MarketingCanais.Manual;

    /// <summary>"agv eletrico" → "AGV ELÉTRICO"; vazio → "Não informado".</summary>
    public static string NormalizarOQue(string? oQue)
    {
        if (string.IsNullOrWhiteSpace(oQue)) return NaoInformado;
        var chave = SemAcentos(oQue).Trim().ToUpperInvariant();
        if (chave == SemAcentos(NaoInformado).ToUpperInvariant()) return NaoInformado;
        return OQueConhecidos.FirstOrDefault(o => SemAcentos(o) == chave) ?? oQue.Trim().ToUpperInvariant();
    }

    /// <summary>AGV, AGV ELÉTRICO, AGV TRUCK primeiro; depois os outros; "Não informado" por último.</summary>
    private static string OrdemOQue(string oQue)
    {
        var i = Array.IndexOf(OQueConhecidos, oQue);
        return i >= 0 ? $"0{i}" : oQue == NaoInformado ? "2" : $"1{oQue}";
    }

    private static decimal Taxa(int parte, int total) => total == 0 ? 0m : Math.Round(100m * parte / total, 1);

    /// <summary>Meia-noite do dia em Brasília, em UTC (o Npgsql só grava DateTimeOffset com offset zero).</summary>
    private static DateTimeOffset InicioDoDia(DateOnly dia) => new DateTimeOffset(dia.ToDateTime(TimeOnly.MinValue), Brasilia).ToUniversalTime();

    private static DateOnly DiaBrasilia(DateTimeOffset instante) => DateOnly.FromDateTime(instante.ToOffset(Brasilia).DateTime);

    /// <summary>"2026-09" — mês da chegada no horário de Brasília.</summary>
    private static string MesBrasilia(DateTimeOffset instante) => instante.ToOffset(Brasilia).ToString("yyyy-MM");

    private static string? Vazio(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();

    private static List<string>? Lista(string[]? valores)
    {
        var lista = valores?.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).ToList();
        return lista is { Count: > 0 } ? lista : null;
    }

    private static string SemAcentos(string texto) =>
        new(texto.Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
}
