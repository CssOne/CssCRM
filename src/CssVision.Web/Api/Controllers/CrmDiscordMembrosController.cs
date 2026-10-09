using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Authorization;
using CssVision.Web.Services.Discord;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CssVision.Web.Api.Controllers;

/// <summary>Membros do servidor do Discord vistos no CRM (só leitura; administradores e gestores).</summary>
[ApiController]
[Route("api/crm/discord/membros")]
[Authorize(Policy = PolicyNames.VisaoTotalComercial)]
public class CrmDiscordMembrosController(IDiscordMembrosService membros) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<DiscordMembrosDto>> Listar(CancellationToken ct) => Ok(await membros.ListarAsync(ct));
}
