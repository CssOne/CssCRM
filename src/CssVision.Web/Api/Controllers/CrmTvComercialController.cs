using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Authorization;
using CssVision.Web.Data;
using CssVision.Web.Services.Crm;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CssVision.Web.Api.Controllers;

/// <summary>
/// Dados do painel comercial da TV (/tv/comercial): quem gerencia vê o escopo dele (Gestor regional, só a regional). O consultor só entra se o gestor liberou
/// a TV para ele em Usuários (<c>VerPainelTv</c>) e então vê o painel inteiro.
/// </summary>
[ApiController]
[Route("api/crm/tv/comercial")]
[Authorize(Policy = PolicyNames.AreaComercial)]
public class CrmTvComercialController(ITvComercialService tv, ICurrentUserService currentUser, ApplicationDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<TvComercialDto>> Obter([FromQuery] int? mes, [FromQuery] int? ano, CancellationToken ct)
    {
        if (mes is < 1 or > 12) return BadRequest("Mês inválido.");
        if (ano is < 2020 or > 2100) return BadRequest("Ano inválido.");
        if (Roles.GestaoComercial.Any(currentUser.IsInRole)) return Ok(await tv.ObterAsync(mes, ano, ct));

        var liberado = await db.Users.AsNoTracking().AnyAsync(u => u.Id == currentUser.UserId && u.VerPainelTv && u.Ativo, ct);
        return liberado ? Ok(await tv.ObterCompletoAsync(mes, ano, ct)) : Forbid();
    }
}
