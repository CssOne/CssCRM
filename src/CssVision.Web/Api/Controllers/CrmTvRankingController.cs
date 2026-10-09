using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Authorization;
using CssVision.Web.Services.Crm;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CssVision.Web.Api.Controllers;

/// <summary>Ranking de vendas do mês, igual ao do painel da TV, para o Portal do consultor (qualquer perfil comercial, sem o escopo de equipe).</summary>
[ApiController]
[Route("api/crm/tv/ranking")]
[Authorize(Policy = PolicyNames.AreaComercial)]
public class CrmTvRankingController(ITvComercialService tv) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<TvRankingGeralDto>> Obter([FromQuery] int? mes, [FromQuery] int? ano, CancellationToken ct)
    {
        if (mes is < 1 or > 12) return BadRequest("Mês inválido.");
        if (ano is < 2020 or > 2100) return BadRequest("Ano inválido.");
        return Ok(await tv.ObterRankingGeralAsync(mes, ano, ct));
    }
}
