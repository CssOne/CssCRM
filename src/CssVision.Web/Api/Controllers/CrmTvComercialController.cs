using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Authorization;
using CssVision.Web.Services.Crm;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CssVision.Web.Api.Controllers;

/// <summary>Dados do painel comercial da TV (/tv/comercial): quem gerencia vê o escopo dele (Gestor regional, só a regional).</summary>
[ApiController]
[Route("api/crm/tv/comercial")]
[Authorize(Policy = PolicyNames.GestaoComercial)]
public class CrmTvComercialController(ITvComercialService tv) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<TvComercialDto>> Obter([FromQuery] int? mes, [FromQuery] int? ano, CancellationToken ct)
    {
        if (mes is < 1 or > 12) return BadRequest("Mês inválido.");
        if (ano is < 2020 or > 2100) return BadRequest("Ano inválido.");
        return Ok(await tv.ObterAsync(mes, ano, ct));
    }
}
