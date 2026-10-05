namespace CssVision.Web.Api.Contracts.Crm;

public record DashboardFilterRequest(DateOnly? DataInicio, DateOnly? DataFim, Guid? VendedorId);

public record DashboardIndicadoresDto(
    int NovosLeads,
    int LeadsSemContato,
    int ContatosHoje,
    int AtividadesAtrasadas,
    int OportunidadesAbertas,
    decimal ValorPipeline,
    decimal TaxaConversao,
    decimal TicketMedio,
    decimal VendasGanhasValor,
    int VendasGanhasQuantidade,
    decimal VendasGanhasAdesaoValor);

public record MetaResultadoDto(
    decimal MetaValor,
    decimal RealizadoValor,
    decimal PercentualAtingido,
    int MetaQuantidade,
    int RealizadoQuantidade);

public record FunilEtapaDto(string Etapa, int Quantidade, decimal ValorTotal);

public record EvolucaoVendasDto(string Periodo, decimal ValorGanho, int Quantidade);

public record OrigemLeadDto(string Origem, int Quantidade);

public record DesempenhoVendedorDto(
    Guid VendedorId,
    string VendedorNome,
    int LeadsAtribuidos,
    int OportunidadesAbertas,
    decimal ValorPipeline,
    int VendasGanhas,
    decimal ValorGanho,
    decimal TaxaConversao,
    decimal ValorAdesao);

public record AlertaLeadParadoDto(Guid LeadId, string LeadNome, string? ResponsavelNome, int DiasSemContato, string? EtapaNome = null);

public record DashboardDto(
    DashboardIndicadoresDto Indicadores,
    MetaResultadoDto Meta,
    IReadOnlyList<FunilEtapaDto> Funil,
    IReadOnlyList<EvolucaoVendasDto> EvolucaoVendas,
    IReadOnlyList<OrigemLeadDto> OrigemLeads,
    IReadOnlyList<DesempenhoVendedorDto> DesempenhoPorVendedor,
    IReadOnlyList<ActivityDto> AtividadesDoDia,
    IReadOnlyList<AlertaLeadParadoDto> LeadsParados,
    /// <summary>Resumo do quadro de leads: quantos leads do período estão em cada coluna.</summary>
    IReadOnlyList<EtapaLeadResumoDto>? FunilLeads = null,
    /// <summary>Últimos 12 meses (mês atual por último): leads, vendas, valor, adesão e conversão.</summary>
    IReadOnlyList<ResumoMensalDto>? ResumoMensal = null,
    /// <summary>Total de leads parados (a lista traz só os 15 mais antigos).</summary>
    int LeadsParadosTotal = 0);

public record EtapaLeadResumoDto(string Etapa, string? Cor, int Quantidade);

public record ResumoMensalDto(string Mes, int Leads, int Perdidos, int Vendas, decimal ValorGanho, decimal Adesao, decimal Conversao);
