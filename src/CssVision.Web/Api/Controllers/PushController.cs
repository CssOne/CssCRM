using CssVision.Web.Authorization;
using CssVision.Web.Services.Crm;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CssVision.Web.Api.Controllers;

/// <summary>Inscrição do navegador do usuário logado nas notificações push (aviso de lead novo).</summary>
[ApiController]
[Route("api/crm/push")]
[Authorize(Policy = PolicyNames.AreaComercial)]
public class PushController(IPushService push, ICurrentUserService currentUser) : ControllerBase
{
    [HttpGet("chave-publica")]
    public async Task<ActionResult> ChavePublica(CancellationToken ct) => Ok(new { chave = await push.ChavePublicaAsync(ct) });

    [HttpPost("inscricao")]
    public async Task<IActionResult> Inscrever(PushInscricaoRequest request, CancellationToken ct)
    {
        await push.InscreverAsync(currentUser.UserId, request, ct);
        return NoContent();
    }

    [HttpPost("inscricao/remover")]
    public async Task<IActionResult> Remover([FromBody] RemoverInscricaoRequest request, CancellationToken ct)
    {
        await push.RemoverAsync(currentUser.UserId, request.Endpoint, ct);
        return NoContent();
    }

    /// <summary>Manda uma notificação de teste para os navegadores do próprio usuário.</summary>
    [HttpPost("teste")]
    public async Task<ActionResult> Testar(CancellationToken ct) =>
        Ok(new
        {
            enviados = await push.EnviarAsync(currentUser.UserId,
                new PushMensagem("Notificações ativadas ✅", "Você vai ser avisado aqui quando chegar um lead novo.", "/app/crm/leads/kanban", "teste"), ct),
        });

    public record RemoverInscricaoRequest(string Endpoint);
}
