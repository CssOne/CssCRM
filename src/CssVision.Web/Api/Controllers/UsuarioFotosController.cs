using CssVision.Web.Authorization;
using CssVision.Web.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CssVision.Web.Api.Controllers;

/// <summary>
/// Fotos de perfil dos usuários, por id. As telas mostram a foto no lugar do círculo com as iniciais; em vez de cada
/// resposta carregar a foto, o front baixa este mapa uma vez (só quem tem foto cadastrada entra).
/// </summary>
[ApiController]
[Route("api/crm/usuarios/fotos")]
[Authorize(Policy = PolicyNames.AreaComercial)]
public class UsuarioFotosController(ApplicationDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<Dictionary<Guid, string>>> Listar(CancellationToken ct) =>
        Ok(await db.Users.AsNoTracking()
            .Where(u => u.FotoUrl != null && u.FotoUrl != "")
            .ToDictionaryAsync(u => u.Id, u => u.FotoUrl!, ct));
}
