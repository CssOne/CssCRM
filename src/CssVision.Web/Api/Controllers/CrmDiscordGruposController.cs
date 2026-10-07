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
