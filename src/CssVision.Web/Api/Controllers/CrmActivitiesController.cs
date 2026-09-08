using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Authorization;
using CssVision.Web.Services.Crm;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CssVision.Web.Api.Controllers;

[ApiController]
[Route("api/crm/activities")]
[Authorize(Policy = PolicyNames.AreaComercial)]
public class CrmActivitiesController(IActivityService activityService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult> Listar([FromQuery] ActivityFilterRequest filtro, CancellationToken ct) =>
        Ok(await activityService.ListarAsync(filtro, ct));

    [HttpPost]
    public async Task<ActionResult<ActivityDto>> Criar(ActivityCreateRequest request, CancellationToken ct) =>
        Ok(await activityService.CriarAsync(request, ct));

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ActivityDto>> Atualizar(Guid id, ActivityUpdateRequest request, CancellationToken ct) =>
        Ok(await activityService.AtualizarAsync(id, request, ct));

    [HttpPost("{id:guid}/complete")]
    public async Task<ActionResult<ActivityDto>> Concluir(Guid id, ActivityCompleteRequest request, CancellationToken ct) =>
        Ok(await activityService.ConcluirAsync(id, request, ct));
}
