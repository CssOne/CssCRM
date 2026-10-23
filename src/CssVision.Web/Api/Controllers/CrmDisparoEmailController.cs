using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Authorization;
using CssVision.Web.Services.Crm;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CssVision.Web.Api.Controllers;

[ApiController]
[Route("api/crm/disparo-email")]
[Authorize(Policy = PolicyNames.AreaComercial)]
public class CrmDisparoEmailController(IDisparoEmailService service) : ControllerBase
{
    /// <summary>Quantos clientes do consultor (com e-mail) estão na etapa.</summary>
    [HttpGet("previa")]
    public async Task<ActionResult<DisparoEmailPreviaDto>> Previa([FromQuery] Guid etapaId, CancellationToken ct) =>
        Ok(await service.PreviaAsync(etapaId, ct));

    [HttpPost]
    public async Task<ActionResult<DisparoEmailResultadoDto>> Enviar(DisparoEmailRequest request, CancellationToken ct) =>
        Ok(await service.EnviarAsync(request, ct));
}
