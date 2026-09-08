using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Authorization;
using CssVision.Web.Services.Crm;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CssVision.Web.Api.Controllers;

[ApiController]
[Route("api/crm/management")]
[Authorize(Policy = PolicyNames.GestaoComercial)]
public class CrmManagementController(IManagementService managementService) : ControllerBase
{
    [HttpGet("summary")]
    public async Task<ActionResult<GestaoComercialResumoDto>> ObterResumo(
        [FromQuery] DateOnly? dataInicio, [FromQuery] DateOnly? dataFim, CancellationToken ct) =>
        Ok(await managementService.ObterResumoAsync(dataInicio, dataFim, ct));

    [HttpGet("vendedores")]
    public async Task<ActionResult<IReadOnlyList<VendedorResumoDto>>> ObterVendedores(CancellationToken ct) =>
        Ok(await managementService.ObterVendedoresAsync(ct));

    [HttpPut("vendedores/{id:guid}/limite")]
    public async Task<IActionResult> AtualizarLimiteMensal(Guid id, AtualizarLimiteMensalRequest request, CancellationToken ct)
    {
        await managementService.AtualizarLimiteMensalAsync(id, request, ct);
        return NoContent();
    }

    [HttpGet("redistribuicoes")]
    public async Task<ActionResult<IReadOnlyList<RedistribuicaoHistoricoDto>>> ObterHistorico(CancellationToken ct) =>
        Ok(await managementService.ObterHistoricoRedistribuicoesAsync(ct));
}
