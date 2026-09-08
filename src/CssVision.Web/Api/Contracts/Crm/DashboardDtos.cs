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
    int VendasGanhasQuantidade);

public record MetaResultadoDto(decimal MetaValor, decimal RealizadoValor, decimal PercentualAtingido);

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
    decimal TaxaConversao);

public record AlertaLeadParadoDto(Guid LeadId, string LeadNome, string? ResponsavelNome, int DiasSemContato);

public record DashboardDto(
    DashboardIndicadoresDto Indicadores,
    MetaResultadoDto Meta,
    IReadOnlyList<FunilEtapaDto> Funil,
    IReadOnlyList<EvolucaoVendasDto> EvolucaoVendas,
    IReadOnlyList<OrigemLeadDto> OrigemLeads,
    IReadOnlyList<DesempenhoVendedorDto> DesempenhoPorVendedor,
    IReadOnlyList<ActivityDto> AtividadesDoDia,
    IReadOnlyList<AlertaLeadParadoDto> LeadsParados);
