namespace CssVision.Web.Api.Contracts.Crm;

/// <summary>
/// Filtros da aba Tráfego pago. Os de lista aceitam vários valores (?oQue=AGV&amp;oQue=AGV TRUCK = um OU outro);
/// lista vazia = sem filtro.
/// </summary>
public record MarketingFilterRequest
{
    public DateOnly? DataInicio { get; init; }
    public DateOnly? DataFim { get; init; }

    /// <summary>
    /// "trafego" (padrão): leads de anúncio, do sistema novo e do Notion; "novo": só os que chegaram
    /// direto dos anúncios pelo sistema novo; "todos": todos os leads, inclusive indicações e cadastros.
    /// </summary>
    public string? Fonte { get; init; }

    /// <summary>"O que?" do lead: AGV, AGV ELÉTRICO, AGV TRUCK... (sem diferenciar acentos/maiúsculas).</summary>
    public string[]? OQue { get; init; }
    public string[]? Origem { get; init; }
    public string[]? Campanha { get; init; }
    /// <summary>Canal por onde o lead chegou — ver <see cref="MarketingCanais"/>.</summary>
    public string[]? Canal { get; init; }
    public Guid[]? ResponsavelId { get; init; }
    public string[]? Estado { get; init; }
    /// <summary>Etapa atual no quadro de leads (nome; "Sem etapa" para os ainda não trabalhados).</summary>
    public string[]? Etapa { get; init; }
}

public static class MarketingCanais
{
    public const string MetaFormulario = "Meta – formulário instantâneo";
    public const string MetaSite = "Meta – site";
    public const string GoogleSite = "Google Ads – site";
    public const string Site = "Site – outros";
    public const string Notion = "Notion";
    public const string Manual = "Cadastro manual";
}

public record MarketingIndicadoresDto(
    int TotalLeads,
    int LeadsSemEtapa,
    int LeadsGanhos,
    int LeadsPerdidos,
    decimal TaxaConversao,
    int LeadsSemContato,
    int LeadsEmAndamento = 0,
    int LeadsNaoFazemos = 0,
    int LeadsSemResponsavel = 0,
    /// <summary>Vendas sobre o total de leads do período (a outra taxa é sobre os já decididos).</summary>
    decimal TaxaConversaoGeral = 0,
    /// <summary>Soma do pagamento de adesão das vendas desses leads.</summary>
    decimal ValorAdesao = 0,
    /// <summary>Mensalidade média das vendas desses leads.</summary>
    decimal MensalidadeMedia = 0,
    /// <summary>Tempo médio (horas) entre o lead chegar ao consultor e o primeiro contato; null sem dados.</summary>
    double? TempoMedioPrimeiroContatoHoras = null,
    /// <summary>Leads no período anterior de mesmo tamanho, com os mesmos filtros (para comparar).</summary>
    int TotalLeadsPeriodoAnterior = 0,
    int LeadsGanhosPeriodoAnterior = 0,
    decimal MediaLeadsPorDia = 0);

/// <summary>Linha genérica de agrupamento (por origem, "O que?", estado, canal...).</summary>
public record MarketingGrupoDto(string Nome, int TotalLeads, int Ganhos, int Perdidos, decimal TaxaConversao, int SemEtapa = 0);

public record MarketingOrigemDto(string Origem, int TotalLeads, int Ganhos, decimal TaxaConversao);

public record MarketingCampanhaDto(
    string Campanha, string? Origem, int TotalLeads, int Ganhos, decimal TaxaConversao, DateTimeOffset UltimoLeadEm,
    int Perdidos = 0, int SemEtapa = 0, decimal ValorAdesao = 0);

public record MarketingConsultorDto(
    Guid? Id, string Nome, int TotalLeads, int SemEtapa, int EmAndamento, int Ganhos, int Perdidos, decimal TaxaConversao,
    int SemContato, double? TempoMedioPrimeiroContatoHoras);

/// <summary>Leads e vendas de um consultor num mês ("2026-09", mês da chegada em Brasília).</summary>
public record MarketingConsultorMesDto(Guid? Id, string Nome, string Mes, int Leads, int Ganhos);

public record MarketingEvolucaoDto(string Data, int Quantidade, int Ganhos = 0);

/// <summary>Série do gráfico de leads por dia, uma por "O que?" (valores alinhados com Evolucao).</summary>
public record MarketingSerieDto(string Nome, IReadOnlyList<int> Valores);

public record MarketingFunilDto(string Etapa, int Quantidade, string? Cor);

/// <summary>Leads por dia da semana (0 = domingo) e hora (horário de Brasília).</summary>
public record MarketingHorarioDto(int DiaSemana, int Hora, int Quantidade);

public record MarketingMotivoPerdaDto(string Motivo, int Quantidade);

public record MarketingConsultorOpcaoDto(Guid Id, string Nome);

public record MarketingOpcoesDto(
    IReadOnlyList<string> OQue,
    IReadOnlyList<string> Origens,
    IReadOnlyList<string> Campanhas,
    IReadOnlyList<string> Canais,
    IReadOnlyList<MarketingConsultorOpcaoDto> Consultores,
    IReadOnlyList<string> Estados,
    IReadOnlyList<string> Etapas);

public record MarketingLeadItemDto(
    Guid Id,
    string NomeOuRazaoSocial,
    string? Telefone,
    string? Origem,
    string? Campanha,
    string? UtmSource,
    string? UtmMedium,
    string? EtapaNome,
    string? ResponsavelNome,
    DateTimeOffset CriadoEm,
    string? OQue = null,
    string? Estado = null,
    string? Canal = null);

public record MarketingDashboardDto(
    MarketingIndicadoresDto Indicadores,
    IReadOnlyList<MarketingOrigemDto> PorOrigem,
    IReadOnlyList<MarketingCampanhaDto> PorCampanha,
    IReadOnlyList<MarketingEvolucaoDto> Evolucao,
    IReadOnlyList<string> OrigensDisponiveis,
    IReadOnlyList<MarketingLeadItemDto> Leads,
    IReadOnlyList<MarketingSerieDto>? EvolucaoPorOQue = null,
    IReadOnlyList<MarketingGrupoDto>? PorOQue = null,
    IReadOnlyList<MarketingGrupoDto>? PorCanal = null,
    IReadOnlyList<MarketingGrupoDto>? PorEstado = null,
    IReadOnlyList<MarketingConsultorDto>? PorConsultor = null,
    IReadOnlyList<MarketingFunilDto>? Funil = null,
    IReadOnlyList<MarketingHorarioDto>? PorHorario = null,
    IReadOnlyList<MarketingMotivoPerdaDto>? MotivosPerda = null,
    MarketingOpcoesDto? Opcoes = null,
    string? PeriodoInicio = null,
    string? PeriodoFim = null,
    IReadOnlyList<MarketingConsultorMesDto>? PorConsultorMensal = null,
    /// <summary>Total de leads do período (a lista em si vem paginada por /api/marketing/leads).</summary>
    int TotalLeadsLista = 0);
