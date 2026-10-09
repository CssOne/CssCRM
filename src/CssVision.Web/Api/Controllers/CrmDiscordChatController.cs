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

    /// <summary>Pessoas com quem dá para iniciar uma conversa 1:1 (quem vinculou o Discord e já está no servidor).</summary>
    [HttpGet("contatos")]
    public async Task<ActionResult<IReadOnlyList<DiscordChatContatoDto>>> Contatos([FromQuery] string? busca, CancellationToken ct) =>
        Ok(await chat.ListarContatosAsync(currentUser.UserId, busca, ct));

    /// <summary>Abre (criando, se for a primeira vez) a conversa 1:1 com a pessoa.</summary>
    [HttpPost("conversas")]
    public async Task<ActionResult<DiscordChatCanalDto>> IniciarConversa(DiscordChatIniciarRequest request, CancellationToken ct) =>
        Ok(await chat.IniciarConversaAsync(currentUser.UserId, request.UsuarioId, ct));

    [HttpGet("canais/{chave}/mensagens")]
    public async Task<ActionResult<DiscordChatMensagensDto>> Mensagens(string chave, [FromQuery] string? antes, CancellationToken ct) =>
        Ok(await chat.ListarMensagensAsync(currentUser.UserId, chave, antes, ct));

    [HttpPost("canais/{chave}/mensagens")]
    public async Task<ActionResult<DiscordChatMensagemDto>> Enviar(string chave, DiscordChatEnviarRequest request, CancellationToken ct) =>
        Ok(await chat.EnviarAsync(currentUser.UserId, chave, request.Texto, ct));
}
