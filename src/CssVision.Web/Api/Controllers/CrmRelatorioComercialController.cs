using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Authorization;
using CssVision.Web.Services.Crm;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CssVision.Web.Api.Controllers;

[ApiController]
[Route("api/crm/relatorio-comercial")]
[Authorize(Policy = PolicyNames.GestaoComercial)]
public class CrmRelatorioComercialController(IRelatorioComercialService relatorio) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<RelatorioComercialDto>> Obter(
        [FromQuery] DateOnly? dataInicio, [FromQuery] DateOnly? dataFim,
        [FromQuery] Guid? consultorId, [FromQuery] Guid[]? etapaId,
        [FromQuery] DateOnly? chegadaInicio, [FromQuery] DateOnly? chegadaFim,
        [FromQuery] DateOnly? vendaInicio, [FromQuery] DateOnly? vendaFim,
        [FromQuery] bool? indicacao, [FromQuery] string[]? tipoIndicacao, CancellationToken ct) =>
        Ok(await relatorio.ObterAsync(dataInicio, dataFim, consultorId, etapaId,
            new RelatorioFiltroExtra(chegadaInicio, chegadaFim, vendaInicio, vendaFim, indicacao, tipoIndicacao), ct));
}
