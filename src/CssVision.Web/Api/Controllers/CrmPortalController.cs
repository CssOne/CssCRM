using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Authorization;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CssVision.Web.Api.Controllers;

[ApiController]
[Route("api/crm/portal")]
[Authorize(Policy = PolicyNames.AreaComercial)]
public class CrmPortalController(IAnnouncementService announcementService) : ControllerBase
{
    [HttpGet("announcements")]
    public async Task<ActionResult<IReadOnlyList<AnnouncementDto>>> ObterAnuncios([FromQuery] TipoAnuncio? tipo, CancellationToken ct) =>
        Ok(await announcementService.ObterAsync(tipo, ct));
}
