namespace CssVision.Web.Api.Contracts.Crm;

public record MarketingFilterRequest(DateOnly? DataInicio, DateOnly? DataFim, string? Origem, string? Campanha);

public record MarketingIndicadoresDto(
    int TotalLeads,
    int LeadsSemEtapa,
    int LeadsGanhos,
    int LeadsPerdidos,
    decimal TaxaConversao,
    int LeadsSemContato);

public record MarketingOrigemDto(string Origem, int TotalLeads, int Ganhos, decimal TaxaConversao);

public record MarketingCampanhaDto(string Campanha, string? Origem, int TotalLeads, int Ganhos, decimal TaxaConversao, DateTimeOffset UltimoLeadEm);

public record MarketingEvolucaoDto(string Data, int Quantidade);

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
    DateTimeOffset CriadoEm);

public record MarketingDashboardDto(
    MarketingIndicadoresDto Indicadores,
    IReadOnlyList<MarketingOrigemDto> PorOrigem,
    IReadOnlyList<MarketingCampanhaDto> PorCampanha,
    IReadOnlyList<MarketingEvolucaoDto> Evolucao,
    IReadOnlyList<string> OrigensDisponiveis,
    IReadOnlyList<MarketingLeadItemDto> Leads);
