using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Authorization;
using CssVision.Web.Services.Crm;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CssVision.Web.Api.Controllers;

[ApiController]
[Route("api/crm/goals")]
[Authorize(Policy = PolicyNames.AreaComercial)]
public class CrmGoalsController(IGoalService goalService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SalesGoalDto>>> Listar([FromQuery] DateOnly? mesReferencia, CancellationToken ct) =>
        Ok(await goalService.ListarAsync(mesReferencia, ct));

    [HttpPut]
    [Authorize(Policy = PolicyNames.GestaoComercial)]
    public async Task<ActionResult<SalesGoalDto>> Definir(SalesGoalUpsertRequest request, CancellationToken ct) =>
        Ok(await goalService.DefinirMetaAsync(request, ct));
}
