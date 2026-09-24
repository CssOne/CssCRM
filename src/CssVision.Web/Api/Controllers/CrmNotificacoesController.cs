using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Authorization;
using CssVision.Web.Services.Crm;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CssVision.Web.Api.Controllers;

[ApiController]
[Route("api/crm/notificacoes")]
[Authorize(Policy = PolicyNames.AreaComercial)]
public class CrmNotificacoesController(ILeadAssignmentService distribuicao, ICurrentUserService currentUser) : ControllerBase
{
    /// <summary>
    /// Leads que passaram a ser do usuário logado depois de <paramref name="desde"/>. Sem <c>desde</c>,
    /// só devolve o horário do servidor, que a tela guarda como ponto de partida das próximas consultas.
    /// </summary>
    [HttpGet("novos-leads")]
    public async Task<ActionResult<NovosLeadsDto>> NovosLeads([FromQuery] DateTimeOffset? desde, CancellationToken ct) =>
        Ok(await distribuicao.NovosLeadsAsync(currentUser.UserId, desde, ct));
}
