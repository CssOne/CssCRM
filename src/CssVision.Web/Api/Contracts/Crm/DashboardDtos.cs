namespace CssVision.Web.Api.Contracts.Crm;

/// <param name="RegionalId">Só o administrador (visão total) pode escolher: mostra o painel apenas dessa regional (a meta do mês é a da regional). Para os demais é ignorado.</param>
public record DashboardFilterRequest(DateOnly? DataInicio, DateOnly? DataFim, Guid? VendedorId, Guid? RegionalId = null);

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
    decimal VendasGanhasAdesaoValor,
    /// <summary>Leads do período vindos do tráfego pago que ainda estão em "Sem etapa" (o card "Novos leads").</summary>
    int NovosLeadsTrafegoSemEtapa = 0);

public record MetaResultadoDto(
    decimal MetaValor,
    decimal RealizadoValor,
    decimal PercentualAtingido,
    int MetaQuantidade,
    int RealizadoQuantidade,
    /// <summary>As vendas contadas na meta, por tipo (Lead, Indicação, Indicação Lead, Pessoal...): as cores da barra de meta.</summary>
    IReadOnlyList<VendaPorTipoDto>? PorTipo = null);

/// <summary>As vendas de um tipo no mês: quantas e a adesão recebida delas (a mesma conta da meta de valor).</summary>
public record VendaPorTipoDto(string Tipo, int Quantidade, decimal Adesao = 0m);

public record FunilEtapaDto(string Etapa, int Quantidade, decimal ValorTotal);

public record EvolucaoVendasDto(string Periodo, decimal ValorGanho, int Quantidade, decimal Adesao = 0m);

/// <summary>Vendas do período por origem (Tráfego pago, Indicação...).</summary>
public record OrigemVendaDto(string Origem, int Quantidade);

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
    IReadOnlyList<OrigemVendaDto> OrigemVendas,
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
