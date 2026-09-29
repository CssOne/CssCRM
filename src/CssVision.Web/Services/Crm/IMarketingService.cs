using CssVision.Web.Api.Contracts.Crm;

namespace CssVision.Web.Services.Crm;

public interface IMarketingService
{
    Task<MarketingDashboardDto> ObterAsync(MarketingFilterRequest filtro, CancellationToken ct);
    Task<Api.Contracts.Common.PagedResult<MarketingLeadItemDto>> ListarLeadsAsync(MarketingFilterRequest filtro, int pagina, int tamanhoPagina, CancellationToken ct);
}
