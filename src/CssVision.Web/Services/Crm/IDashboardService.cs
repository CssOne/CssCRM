using CssVision.Web.Api.Contracts.Crm;

namespace CssVision.Web.Services.Crm;

public interface IDashboardService
{
    Task<DashboardDto> ObterAsync(DashboardFilterRequest filtro, CancellationToken ct);
}
