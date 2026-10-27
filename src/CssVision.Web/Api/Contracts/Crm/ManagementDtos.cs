namespace CssVision.Web.Api.Contracts.Crm;

public record VendedorResumoDto(
    Guid Id,
    string Nome,
    int LeadsAtivos,
    int OportunidadesAbertas,
    int? LimiteMensalLeads,
    int LeadsRecebidosNoMes,
    int? LimiteDiarioLeads = null,
    int LeadsRecebidosHoje = 0,
    bool Ativo = true,
    bool RecebeLeads = true,
    /// <summary>Leads de tráfego pago (Notion + sistema novo) que chegaram para o vendedor no mês.</summary>
    int LeadsTrafegoNoMes = 0,
    /// <summary>Faixa de horário (Brasília, "HH:mm") em que recebe leads; nulo = o dia todo.</summary>
    string? HorarioInicioLeads = null,
    string? HorarioFimLeads = null,
    /// <summary>Dias em que recebe leads (0 = domingo … 6 = sábado); nulo = todos.</summary>
    int[]? DiasSemanaLeads = null,
    /// <summary>Tipos de lead ("O que?") que o consultor recebe; nulo = qualquer tipo.</summary>
    IReadOnlyList<string>? RecebeSomenteOQue = null,
    /// <summary>Leads de tráfego pago (Notion + sistema novo) que chegaram para o vendedor no mês anterior.</summary>
    int LeadsTrafegoMesAnterior = 0,
    /// <summary>Regional do cadastro do consultor (filtro da Gestão comercial).</summary>
    string? RegionalNome = null);

public record AtualizarTiposLeadRequest(IReadOnlyList<string>? OQue);

/// <summary>Estado da distribuição automática: leads parados porque todos os consultores bateram o limite diário/mensal.</summary>
public record AlertaDistribuicaoDto(
    bool Bloqueada,
    int LeadsSemResponsavel,
    int LeadsBloqueadosPorLimite,
    int Consultores,
    int NoLimiteDiario,
    int NoLimiteMensal,
    /// <summary>Se um gestor mandou continuar a distribuição ignorando os limites, até quando (fim do dia, Brasília).</summary>
    DateTimeOffset? ContinuarAteEm,
    /// <summary>Leads sem responsável que ficam parados por limite e/ou horário (cada lead conta uma vez).</summary>
    int LeadsBloqueados = 0,
    /// <summary>Leads parados porque os consultores aptos estão fora do dia/horário de recebimento.</summary>
    int LeadsBloqueadosPorHorario = 0,
    /// <summary>Consultores ativos que recebem leads mas estão fora do dia/horário agora.</summary>
    int ConsultoresForaDoHorario = 0);

public record ResumoSincronizacaoDto(string Nome, string Resumo);

public record ContinuarDistribuicaoRequest(bool Continuar);

public record AtualizarJanelaRecebimentoRequest(string? HorarioInicio, string? HorarioFim, int[]? DiasSemana);

public record AtualizarLimiteMensalRequest(int? Limite);

public record AtualizarLimiteDiarioRequest(int? Limite);

public record AtualizarRecebeLeadsRequest(bool RecebeLeads);

/// <summary>Ficha de desempenho de um consultor (papel Comercial) para a tela de gestão de consultores.</summary>
public record ConsultorDesempenhoDto(
    Guid Id,
    string Nome,
    string Email,
    string? Telefone,
    string? RegionalNome,
    bool Ativo,
    int LeadsAtivos,
    int OportunidadesAbertas,
    decimal ValorPipeline,
    int VendasGanhas,
    decimal ValorGanho,
    decimal TaxaConversao,
    int? LimiteMensalLeads,
    int LeadsRecebidosNoMes,
    decimal MetaValor,
    decimal RealizadoValor,
    decimal PercentualMeta,
    int LeadsTrafegoNoMes = 0);

public record RankingComercialDto(
    Guid VendedorId,
    string VendedorNome,
    int Posicao,
    decimal ValorGanho,
    int VendasGanhas,
    decimal TaxaConversao,
    /// <summary>Soma do pagamento de adesão das vendas do período (a coluna "Valor" do ranking).</summary>
    decimal ValorAdesao = 0m);

public record TempoMedioEtapaDto(string Etapa, double DiasMedios);

public record MotivoPerdaResumoDto(string Motivo, int Quantidade, decimal ValorPerdido);

public record OportunidadeParadaDto(
    Guid OpportunityId,
    string LeadNome,
    string EtapaNome,
    string ResponsavelNome,
    int DiasSemMovimentacao,
    decimal ValorEstimado);

public record RedistribuicaoHistoricoDto(
    Guid LeadId,
    string LeadNome,
    string? ResponsavelAnteriorNome,
    string ResponsavelNovoNome,
    string? AlteradoPorNome,
    string? Motivo,
    DateTimeOffset AlteradoEm);

/// <summary>
/// Quem tirou (ou devolveu) cada consultor da distribuição de leads. <c>Evento</c>: "desativou" / "ativou" (o interruptor "Recebe leads") e
/// "conta_inativada" / "conta_reativada" (a conta do usuário, que também tira o consultor da distribuição).
/// </summary>
public record RecebeLeadsHistoricoDto(
    Guid ConsultorId,
    string ConsultorNome,
    string Evento,
    string? AlteradoPorNome,
    DateTimeOffset OcorridoEm);

public record GestaoComercialResumoDto(
    double TempoMedioPrimeiroContatoHoras,
    IReadOnlyList<TempoMedioEtapaDto> TempoMedioPorEtapa,
    IReadOnlyList<OportunidadeParadaDto> OportunidadesSemMovimentacao,
    IReadOnlyList<RankingComercialDto> Ranking,
    IReadOnlyList<MotivoPerdaResumoDto> MotivosPerda,
    /// <summary>Quantos leads entraram na média de primeiro contato (os que já tiveram contato no período).</summary>
    int LeadsComPrimeiroContato = 0,
    IReadOnlyList<PrimeiroContatoVendedorDto>? PrimeiroContatoPorVendedor = null);

/// <summary>Tempo médio até o primeiro contato de um vendedor.</summary>
public record PrimeiroContatoVendedorDto(Guid VendedorId, string VendedorNome, double Horas, int Leads);

/// <summary>Um consultor no painel da espera por leads do tráfego pago em "Sem etapa" (Gestão comercial).</summary>
public record EsperaConsultorDto(
    Guid ConsultorId, string Nome, string? RegionalNome,
    /// <summary>Leads do tráfego pago com ele, em "Sem etapa", agora.</summary>
    int EsperandoAgora,
    /// <summary>Desde quando o mais antigo deles espera.</summary>
    DateTimeOffset? MaisAntigoDesde,
    /// <summary>Nos últimos dias: leads que passaram do primeiro prazo sem ele pegar.</summary>
    int PassaramDoPrimeiroPrazo,
    /// <summary>Nos últimos dias: leads que passaram do prazo forte (1 h) — o que conta para "demora com frequência".</summary>
    int PassaramDoPrazoForte,
    bool Reincidente,
    /// <summary>Leads dele nos últimos dias: quando chegaram e quando foram atendidos (nulo = ainda esperando).</summary>
    IReadOnlyList<EsperaLeadDto> Leads);

public record EsperaLeadDto(Guid LeadId, string Nome, DateTimeOffset ChegouEm, DateTimeOffset? AtendidoEm);

public record EsperaTrafegoPainelDto(int MinutosPrimeiroPrazo, int MinutosPrazoForte, int LeadsParaReincidencia, int Dias, IReadOnlyList<EsperaConsultorDto> Consultores);
