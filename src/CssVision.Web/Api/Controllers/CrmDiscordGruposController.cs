using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Authorization;
using CssVision.Web.Services.Discord;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CssVision.Web.Api.Controllers;

/// <summary>Administração dos grupos da empresa no Discord (canais e cargos espelhando regionais e grupos do CRM).</summary>
[ApiController]
[Route("api/crm/discord/grupos")]
[Authorize(Policy = PolicyNames.VisaoTotalComercial)]
public class CrmDiscordGruposController(IDiscordGruposService grupos, IDiscordAvisosNosCanaisService avisos) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<DiscordCanalDto>>> Listar(CancellationToken ct) => Ok(await grupos.ListarCanaisAsync(ct));

    /// <summary>Cria um canal de texto extra no Discord, visível só para quem está no grupo escolhido.</summary>
    [HttpPost("canais")]
    public async Task<ActionResult<DiscordCanalDto>> CriarCanal(DiscordCriarCanalRequest request, CancellationToken ct) =>
        Ok(await grupos.CriarCanalAsync(request.Nome, request.AcessoChave, request.Topico, ct, request.Voz));

    /// <summary>Apaga um canal extra (irreversível): só com o nome do canal digitado de confirmação.</summary>
    [HttpDelete("canais/{chave}")]
    public async Task<IActionResult> ApagarCanal(string chave, [FromQuery] string confirmarNome, CancellationToken ct)
    {
        await grupos.ApagarCanalAsync(chave, confirmarNome, ct);
        return NoContent();
    }

    /// <summary>Volta a ligar um grupo cujo canal foi apagado (depois, "Sincronizar grupos" recria o canal).</summary>
    [HttpPut("canais/{chave}/religar")]
    public async Task<ActionResult<DiscordCanalDto>> ReligarCanal(string chave, CancellationToken ct) => Ok(await grupos.ReligarCanalAsync(chave, ct));

    /// <summary>Esconde (ou mostra de novo) um canal extra no chat, sem mexer no Discord.</summary>
    [HttpPut("canais/{chave}/arquivado")]
    public async Task<ActionResult<DiscordCanalDto>> ArquivarCanal(string chave, [FromBody] DiscordArquivarCanalRequest request, CancellationToken ct) =>
        Ok(await grupos.ArquivarCanalAsync(chave, request.Arquivar, ct));

    /// <summary>Renomeia o canal no Discord e no CRM.</summary>
    [HttpPut("canais/{chave}")]
    public async Task<ActionResult<DiscordCanalDto>> RenomearCanal(string chave, DiscordRenomearCanalRequest request, CancellationToken ct) =>
        Ok(await grupos.RenomearCanalAsync(chave, request.Nome, ct));

    /// <summary>Quais avisos automáticos estão ligados nos canais das regionais.</summary>
    [HttpGet("avisos")]
    public async Task<ActionResult<DiscordAvisosCanaisDto>> ObterAvisos(CancellationToken ct) => Ok(await avisos.ObterConfiguracaoAsync(ct));

    [HttpPut("avisos")]
    public async Task<ActionResult<DiscordAvisosCanaisDto>> DefinirAvisos(DiscordAvisosCanaisDto configuracao, CancellationToken ct) =>
        Ok(await avisos.DefinirConfiguracaoAsync(configuracao, ct));

    /// <summary>Cria no Discord o que falta e acerta os cargos de cada pessoa. Pode ser repetido à vontade.</summary>
    [HttpPost("sincronizar")]
    public async Task<ActionResult<DiscordSincronizacaoDto>> Sincronizar(CancellationToken ct) => Ok(await grupos.SincronizarAsync(ct));
}
