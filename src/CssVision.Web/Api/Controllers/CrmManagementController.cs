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
    public async Task<ActionResult<IReadOnlyList<VendedorResumoDto>>> ObterVendedores([FromQuery] bool incluirInativos, CancellationToken ct) =>
        Ok(await managementService.ObterVendedoresAsync(ct, incluirInativos));

    [HttpPut("vendedores/{id:guid}/recebe-leads")]
    public async Task<IActionResult> AtualizarRecebeLeads(Guid id, AtualizarRecebeLeadsRequest request, CancellationToken ct)
    {
        await managementService.AtualizarRecebeLeadsAsync(id, request, ct);
        return NoContent();
    }

    [HttpPut("vendedores/{id:guid}/limite")]
    public async Task<IActionResult> AtualizarLimiteMensal(Guid id, AtualizarLimiteMensalRequest request, CancellationToken ct)
    {
        await managementService.AtualizarLimiteMensalAsync(id, request, ct);
        return NoContent();
    }

    [HttpPut("vendedores/{id:guid}/limite-diario")]
    public async Task<IActionResult> AtualizarLimiteDiario(Guid id, AtualizarLimiteDiarioRequest request, CancellationToken ct)
    {
        await managementService.AtualizarLimiteDiarioAsync(id, request, ct);
        return NoContent();
    }

    [HttpGet("consultores")]
    public async Task<ActionResult<IReadOnlyList<ConsultorDesempenhoDto>>> ObterDesempenhoConsultores(
        [FromQuery] DateOnly? mesReferencia, CancellationToken ct) =>
        Ok(await managementService.ObterDesempenhoConsultoresAsync(mesReferencia, ct));

    [HttpGet("redistribuicoes")]
    public async Task<ActionResult<IReadOnlyList<RedistribuicaoHistoricoDto>>> ObterHistorico(CancellationToken ct) =>
        Ok(await managementService.ObterHistoricoRedistribuicoesAsync(ct));
}
