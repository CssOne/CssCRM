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
    /// <summary>Painel da aba Tráfego pago — filtros de lista aceitam vários valores (?oQue=AGV&amp;oQue=AGV TRUCK).</summary>
    [HttpGet("dashboard")]
    public async Task<ActionResult<MarketingDashboardDto>> ObterDashboard([FromQuery] MarketingFilterRequest filtro, CancellationToken ct) =>
        Ok(await marketingService.ObterAsync(filtro, ct));

    /// <summary>"Últimos leads" da aba, paginado, com os mesmos filtros do painel.</summary>
    [HttpGet("leads")]
    public async Task<ActionResult> ListarLeads(
        [FromQuery] MarketingFilterRequest filtro, [FromQuery] int pagina = 1, [FromQuery] int tamanhoPagina = 20, CancellationToken ct = default) =>
        Ok(await marketingService.ListarLeadsAsync(filtro, pagina, tamanhoPagina, ct));
}
