using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Authorization;
using CssVision.Web.Services.Crm;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CssVision.Web.Api.Controllers;

[ApiController]
[Route("api/crm/settings")]
[Authorize(Policy = PolicyNames.AreaComercial)]
public class CrmSettingsController(ILookupService lookupService) : ControllerBase
{
    [HttpGet("stages")]
    public async Task<ActionResult<IReadOnlyList<PipelineStageDto>>> ObterEtapas(CancellationToken ct) =>
        Ok(await lookupService.ObterEtapasAsync(ct));

    [HttpPost("stages")]
    [Authorize(Policy = PolicyNames.GestaoComercial)]
    public async Task<ActionResult<PipelineStageDto>> CriarEtapa(CreateStageRequest request, CancellationToken ct) =>
        Ok(await lookupService.CriarEtapaAsync(request, ct));

    [HttpGet("loss-reasons")]
    public async Task<ActionResult<IReadOnlyList<LossReasonDto>>> ObterMotivosPerda(CancellationToken ct) =>
        Ok(await lookupService.ObterMotivosPerdaAsync(ct));

    [HttpPost("loss-reasons")]
    [Authorize(Policy = PolicyNames.GestaoComercial)]
    public async Task<ActionResult<LossReasonDto>> CriarMotivoPerda(CreateLossReasonRequest request, CancellationToken ct) =>
        Ok(await lookupService.CriarMotivoPerdaAsync(request, ct));
}
