using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Authorization;
using CssVision.Web.Services.Crm;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CssVision.Web.Api.Controllers;

[ApiController]
[Route("api/crm/pipeline")]
[Authorize(Policy = PolicyNames.AreaComercial)]
public class CrmPipelineController(IPipelineService pipelineService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PipelineBoardDto>> ObterBoard([FromQuery] PipelineFilterRequest filtro, CancellationToken ct) =>
        Ok(await pipelineService.ObterBoardAsync(filtro, ct));
}
