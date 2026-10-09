using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Services.Discord;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CssVision.Web.Api.Controllers;

/// <summary>Vínculo da conta do Discord do usuário logado e envio dos avisos do CRM para o Discord (celular).</summary>
[ApiController]
[Route("api/crm/discord")]
[Authorize]
public class CrmDiscordController(IDiscordService discord, ICurrentUserService currentUser, IOptions<DiscordOptions> options) : ControllerBase
{
    [HttpGet("status")]
    public async Task<ActionResult<DiscordStatusDto>> Status(CancellationToken ct) =>
        Ok(await discord.ObterStatusAsync(currentUser.UserId, ct));

    /// <summary>Devolve o endereço do Discord onde o usuário autoriza o vínculo.</summary>
    [HttpPost("vinculo/iniciar")]
    public async Task<ActionResult<DiscordIniciarDto>> Iniciar(CancellationToken ct) =>
        Ok(new DiscordIniciarDto(await discord.IniciarVinculoAsync(currentUser.UserId, EnderecoDeRetorno(), ct)));

    /// <summary>
    /// Volta do Discord depois da autorização. Quem identifica o usuário é o <c>state</c> (aleatório, de uso único, guardado no servidor),
    /// e não o cookie da sessão — por isso aceita a chamada sem login e sempre termina redirecionando para a tela do Discord.
    /// </summary>
    [HttpGet("callback")]
    [AllowAnonymous]
    public async Task<IActionResult> Retorno([FromQuery] string? code, [FromQuery] string? state, [FromQuery] string? error, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(error) || string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
        {
            return Redirect("/app/discord?resultado=cancelado");
        }

        try
        {
            await discord.ConcluirVinculoAsync(code, state, EnderecoDeRetorno(), ct);
            return Redirect("/app/discord?resultado=ok");
        }
        catch (CrmBusinessException ex)
        {
            return Redirect($"/app/discord?resultado=erro&motivo={Uri.EscapeDataString(ex.Message)}");
        }
    }

    [HttpDelete("vinculo")]
    public async Task<IActionResult> Desvincular(CancellationToken ct)
    {
        await discord.DesvincularAsync(currentUser.UserId, ct);
        return NoContent();
    }

    [HttpPut("avisos")]
    public async Task<ActionResult<DiscordStatusDto>> DefinirAvisos(DiscordAvisosRequest request, CancellationToken ct)
    {
        await discord.DefinirAvisosAsync(currentUser.UserId, request.Ativos, ct);
        return Ok(await discord.ObterStatusAsync(currentUser.UserId, ct));
    }

    /// <summary>Manda um aviso de teste para o Discord do próprio usuário.</summary>
    [HttpPost("teste")]
    public async Task<ActionResult<DiscordTesteDto>> Testar(CancellationToken ct)
    {
        var status = await discord.ObterStatusAsync(currentUser.UserId, ct);
        if (!status.Configurado) return Ok(new DiscordTesteDto(false, "A integração com o Discord não foi ativada no servidor."));
        if (!status.Vinculado) return Ok(new DiscordTesteDto(false, "Vincule sua conta do Discord primeiro."));
        if (!status.AvisosAtivos) return Ok(new DiscordTesteDto(false, "Os avisos pelo Discord estão desligados. Ligue-os e tente de novo."));

        var enviado = await discord.EnviarAvisoAsync(currentUser.UserId, "Discord conectado ✅", "Você vai receber aqui os avisos do CRM: leads novos, pagamentos em aberto e outros.", "/app/discord", ct);
        return Ok(new DiscordTesteDto(enviado, enviado
            ? "Aviso de teste enviado. Confira o Discord no celular."
            : "Não foi possível entregar. Confira se você aceita mensagens diretas de membros do servidor (Discord → Configurações → Privacidade)."));
    }

    /// <summary>Endereço registrado no Discord como "Redirect" do OAuth2 — o mesmo na ida e na volta.</summary>
    private string EnderecoDeRetorno()
    {
        var baseUrl = !string.IsNullOrWhiteSpace(options.Value.UrlPublica) ? options.Value.UrlPublica.TrimEnd('/') : $"{Request.Scheme}://{Request.Host}";
        return $"{baseUrl}/api/crm/discord/callback";
    }
}
