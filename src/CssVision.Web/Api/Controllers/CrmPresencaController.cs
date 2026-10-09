using CssVision.Web.Services.Crm;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CssVision.Web.Api.Controllers;

/// <summary>"Estou com o CRM aberto": a tela chama a cada minuto, e o chat mostra quem está online.</summary>
[ApiController]
[Route("api/crm/presenca")]
[Authorize]
public class CrmPresencaController(IPresencaService presenca, ICurrentUserService currentUser) : ControllerBase
{
    [HttpPost]
    public IActionResult Marcar()
    {
        presenca.Marcar(currentUser.UserId);
        return NoContent();
    }
}
