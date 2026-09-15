using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Authorization;
using CssVision.Web.Services.Crm;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;

namespace CssVision.Web.Api.Controllers;

/// <summary>
/// Recebe leads dos formulários públicos do site (fora do CRM autenticado). Sem cookie de
/// sessão — CORS liberado só aqui, não pro resto da API.
/// </summary>
[ApiController]
[Route("api/public/leads")]
[AllowAnonymous]
[EnableCors(CorsPolicies.PublicLeadIntake)]
public class PublicLeadsController(IPublicLeadIntakeService intake) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<PublicLeadResultDto>> Criar(PublicLeadCreateRequest request, CancellationToken ct) =>
        Ok(await intake.CriarAsync(request, ct));
}
