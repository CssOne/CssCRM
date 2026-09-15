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

    [HttpGet("lead-stages")]
    public async Task<ActionResult<IReadOnlyList<LeadStageDto>>> ObterEtapasLead(CancellationToken ct) =>
        Ok(await lookupService.ObterEtapasLeadAsync(ct));

    [HttpPost("lead-stages")]
    [Authorize(Policy = PolicyNames.GestaoComercial)]
    public async Task<ActionResult<LeadStageDto>> CriarEtapaLead(CreateLeadStageRequest request, CancellationToken ct) =>
        Ok(await lookupService.CriarEtapaLeadAsync(request, ct));

    [HttpGet("loss-reasons")]
    public async Task<ActionResult<IReadOnlyList<LossReasonDto>>> ObterMotivosPerda(CancellationToken ct) =>
        Ok(await lookupService.ObterMotivosPerdaAsync(ct));

    [HttpPost("loss-reasons")]
    [Authorize(Policy = PolicyNames.GestaoComercial)]
    public async Task<ActionResult<LossReasonDto>> CriarMotivoPerda(CreateLossReasonRequest request, CancellationToken ct) =>
        Ok(await lookupService.CriarMotivoPerdaAsync(request, ct));

    [HttpGet("origins")]
    public async Task<ActionResult<IReadOnlyList<string>>> ObterOrigens(CancellationToken ct) =>
        Ok(await lookupService.ObterOrigensAsync(ct));

    [HttpGet("regionals")]
    public async Task<ActionResult<IReadOnlyList<RegionalDto>>> ObterRegionais(CancellationToken ct) =>
        Ok(await lookupService.ObterRegionaisAsync(ct));

    [HttpPost("regionals")]
    [Authorize(Policy = PolicyNames.VisaoTotalComercial)]
    public async Task<ActionResult<RegionalDto>> CriarRegional(CreateRegionalRequest request, CancellationToken ct) =>
        Ok(await lookupService.CriarRegionalAsync(request, ct));

    [HttpPut("regionals/{id:guid}")]
    [Authorize(Policy = PolicyNames.VisaoTotalComercial)]
    public async Task<ActionResult<RegionalDto>> AtualizarRegional(Guid id, UpdateRegionalRequest request, CancellationToken ct) =>
        Ok(await lookupService.AtualizarRegionalAsync(id, request, ct));

    [HttpGet("groups")]
    [Authorize(Policy = PolicyNames.GestaoComercial)]
    public async Task<ActionResult<IReadOnlyList<GrupoDto>>> ObterGrupos([FromQuery] Guid? regionalId, CancellationToken ct) =>
        Ok(await lookupService.ObterGruposAsync(regionalId, ct));

    [HttpPost("groups")]
    [Authorize(Policy = PolicyNames.GestaoComercial)]
    public async Task<ActionResult<GrupoDto>> CriarGrupo(CreateGrupoRequest request, CancellationToken ct) =>
        Ok(await lookupService.CriarGrupoAsync(request, ct));

    [HttpPut("groups/{id:guid}")]
    [Authorize(Policy = PolicyNames.GestaoComercial)]
    public async Task<ActionResult<GrupoDto>> AtualizarGrupo(Guid id, UpdateGrupoRequest request, CancellationToken ct) =>
        Ok(await lookupService.AtualizarGrupoAsync(id, request, ct));

    [HttpPut("groups/{id:guid}/members")]
    [Authorize(Policy = PolicyNames.GestaoComercial)]
    public async Task<ActionResult<GrupoDto>> AtualizarMembrosGrupo(Guid id, UpdateGrupoMembrosRequest request, CancellationToken ct) =>
        Ok(await lookupService.AtualizarMembrosGrupoAsync(id, request, ct));
}
