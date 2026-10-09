using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Services.Discord;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CssVision.Web.Api.Controllers;

/// <summary>Chat de texto dos grupos da empresa (as mensagens ficam no Discord; o CRM mostra e publica).</summary>
[ApiController]
[Route("api/crm/discord/chat")]
[Authorize]
public class CrmDiscordChatController(IDiscordChatService chat, ICurrentUserService currentUser) : ControllerBase
{
    [HttpGet("canais")]
    public async Task<ActionResult<IReadOnlyList<DiscordChatCanalDto>>> Canais(CancellationToken ct) =>
        Ok(await chat.ListarCanaisAsync(currentUser.UserId, ct));

    [HttpGet("canais/{chave}/mensagens")]
    public async Task<ActionResult<DiscordChatMensagensDto>> Mensagens(string chave, [FromQuery] string? antes, CancellationToken ct) =>
        Ok(await chat.ListarMensagensAsync(currentUser.UserId, chave, antes, ct));

    [HttpPost("canais/{chave}/mensagens")]
    public async Task<ActionResult<DiscordChatMensagemDto>> Enviar(string chave, DiscordChatEnviarRequest request, CancellationToken ct) =>
        Ok(await chat.EnviarAsync(currentUser.UserId, chave, request.Texto, ct));
}
