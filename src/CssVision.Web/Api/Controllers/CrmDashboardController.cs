using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Authorization;
using CssVision.Web.Services.Crm;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CssVision.Web.Api.Controllers;

[ApiController]
[Route("api/crm/dashboard")]
[Authorize(Policy = PolicyNames.AreaComercial)]
public class CrmDashboardController(IDashboardService dashboardService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<DashboardDto>> Obter(
        [FromQuery] DateOnly? dataInicio, [FromQuery] DateOnly? dataFim, [FromQuery] Guid? vendedorId, CancellationToken ct)
    {
        var resultado = await dashboardService.ObterAsync(new DashboardFilterRequest(dataInicio, dataFim, vendedorId), ct);
        return Ok(resultado);
    }
}
