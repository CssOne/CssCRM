namespace CssVision.Web.Api.Contracts.Crm;

/// <summary>
/// Filtros extras do relatório (os do Notion): datas próprias de chegada e de venda, "Indicação?" e tipo de indicação.
/// Datas vazias usam o período principal; <c>Indicacao</c> nulo = todos.
/// </summary>
public record RelatorioFiltroExtra(
    DateOnly? ChegadaInicio = null,
    DateOnly? ChegadaFim = null,
    DateOnly? VendaInicio = null,
    DateOnly? VendaFim = null,
    bool? Indicacao = null,
    IReadOnlyCollection<string>? TiposIndicacao = null,
    /// <summary>Só os consultores desta regional (a regional do cadastro do consultor); nulo = todas.</summary>
    Guid? RegionalId = null);

/// <summary>Relatório comercial: os gráficos e tabelas dos "Relatórios" do Notion, calculados com os dados do CRM.</summary>
public record RelatorioComercialDto(
    DateOnly DataInicio,
    DateOnly DataFim,
    RelatorioTotaisDto Totais,
    IReadOnlyList<RelatorioMesDto> PorMes,
    IReadOnlyList<RelatorioSemanaDto> PorSemana,
    IReadOnlyList<RelatorioOrigemDto> PorOrigem,
    IReadOnlyList<RelatorioVendedorDto> PorVendedor,
    IReadOnlyList<RelatorioEstadoDto> PorEstado,
    IReadOnlyList<RelatorioFaixaFipeDto> FaixasFipe,
    IReadOnlyList<RelatorioProdutoDto> PorProduto,
    IReadOnlyList<RelatorioMarketingMesDto> MarketingNotion,
    IReadOnlyList<RelatorioEtapaDto> PorEtapa);

public record RelatorioTotaisDto(
    int Leads,
    int LeadsPerdidos,
    int Vendas,
    decimal TaxaConversao,
    decimal Adesao,
    decimal Mensalidade,
    decimal TicketMensalidade,
    decimal Rastreador,
    decimal Vistoria,
    decimal Indicacao,
    int VendasIndicacao);

/// <summary>Um mês (yyyy-MM): vendas pela data da venda, leads pela data de chegada.</summary>
public record RelatorioMesDto(
    string Mes,
    int Leads,
    int LeadsPerdidos,
    int Vendas,
    decimal Adesao,
    decimal Mensalidade,
    decimal Rastreador,
    decimal Vistoria,
    decimal Indicacao);

/// <summary>Uma semana (começa na segunda-feira).</summary>
public record RelatorioSemanaDto(DateOnly Inicio, int Leads, int LeadsPerdidos, int Vendas, decimal Adesao);

public record RelatorioOrigemDto(string Origem, int Leads, int Vendas, decimal Adesao);

public record RelatorioVendedorDto(Guid? VendedorId, string Vendedor, int Leads, int Vendas, decimal Adesao, decimal Mensalidade, decimal TaxaConversao);

public record RelatorioEstadoDto(string Estado, int Leads, int Vendas);

public record RelatorioFaixaFipeDto(string Faixa, int Vendas);

/// <summary>Leads do período por etapa atual do quadro de leads (EtapaId nulo = "Sem etapa").</summary>
public record RelatorioEtapaDto(Guid? EtapaId, string Etapa, int Leads);

public record RelatorioProdutoDto(string Produto, int Leads, int Vendas, decimal Adesao);

/// <summary>Controle mensal de marketing lançado no Notion (investimento, leads, faturamento e meta).</summary>
public record RelatorioMarketingMesDto(
    string Mes,
    int LeadsGerados,
    int Vendas,
    decimal FacebookAds,
    decimal GoogleAds,
    decimal Ferramentas,
    decimal Backlinks,
    decimal TotalGastos,
    decimal Faturamento,
    decimal MetaFaturamento,
    decimal? CustoPorLead,
    decimal? Roas,
    decimal? TaxaConversao);
