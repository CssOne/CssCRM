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
        [FromQuery] DateOnly? dataInicio, [FromQuery] DateOnly? dataFim, [FromQuery] Guid? vendedorId, [FromQuery] Guid? regionalId, CancellationToken ct)
    {
        var resultado = await dashboardService.ObterAsync(new DashboardFilterRequest(dataInicio, dataFim, vendedorId, regionalId), ct);
        return Ok(resultado);
    }

    /// <summary>As vendas por trás de uma fatia do gráfico "Origem das vendas" da Visão geral.</summary>
    [HttpGet("origem-vendas")]
    public async Task<ActionResult<RelatorioDetalheDto>> ObterVendasDaOrigem(
        [FromQuery] string origem, [FromQuery] DateOnly? dataInicio, [FromQuery] DateOnly? dataFim, [FromQuery] Guid? vendedorId, [FromQuery] Guid? regionalId, CancellationToken ct) =>
        Ok(await dashboardService.ObterVendasDaOrigemAsync(new DashboardFilterRequest(dataInicio, dataFim, vendedorId, regionalId), origem, ct));
}
