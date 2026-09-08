using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Authorization;
using CssVision.Web.Services.Crm;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CssVision.Web.Api.Controllers;

[ApiController]
[Route("api/crm/opportunities")]
[Authorize(Policy = PolicyNames.AreaComercial)]
public class CrmOpportunitiesController(IOpportunityService opportunityService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult> Listar([FromQuery] OpportunityFilterRequest filtro, CancellationToken ct) =>
        Ok(await opportunityService.ListarAsync(filtro, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OpportunityDto>> ObterPorId(Guid id, CancellationToken ct) =>
        Ok(await opportunityService.ObterPorIdAsync(id, ct));

    [HttpPost]
    public async Task<ActionResult<OpportunityDto>> Criar(OpportunityCreateRequest request, CancellationToken ct)
    {
        var resultado = await opportunityService.CriarAsync(request, ct);
        return CreatedAtAction(nameof(ObterPorId), new { id = resultado.Id }, resultado);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<OpportunityDto>> Atualizar(Guid id, OpportunityUpdateRequest request, CancellationToken ct) =>
        Ok(await opportunityService.AtualizarAsync(id, request, ct));

    [HttpPost("{id:guid}/change-stage")]
    public async Task<ActionResult<OpportunityDto>> MudarEtapa(Guid id, ChangeStageRequest request, CancellationToken ct) =>
        Ok(await opportunityService.MudarEtapaAsync(id, request, ct));
}
