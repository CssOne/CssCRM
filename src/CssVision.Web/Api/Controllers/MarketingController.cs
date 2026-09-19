using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Authorization;
using CssVision.Web.Services.Crm;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CssVision.Web.Api.Controllers;

[ApiController]
[Route("api/marketing")]
[Authorize(Policy = PolicyNames.AreaMarketing)]
public class MarketingController(IMarketingService marketingService) : ControllerBase
{
    [HttpGet("dashboard")]
    public async Task<ActionResult<MarketingDashboardDto>> ObterDashboard(
        [FromQuery] DateOnly? dataInicio, [FromQuery] DateOnly? dataFim, [FromQuery] string? origem, [FromQuery] string? campanha,
        CancellationToken ct) =>
        Ok(await marketingService.ObterAsync(new MarketingFilterRequest(dataInicio, dataFim, origem, campanha), ct));
}
