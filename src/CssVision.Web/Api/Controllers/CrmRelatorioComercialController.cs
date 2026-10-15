using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Authorization;
using CssVision.Web.Services.Crm;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CssVision.Web.Api.Controllers;

[ApiController]
[Route("api/crm/relatorio-comercial")]
// Toda a área comercial usa o relatório; o serviço restringe os dados ao escopo de cada usuário (o consultor só vê os próprios resultados).
[Authorize(Policy = PolicyNames.AreaComercial)]
public class CrmRelatorioComercialController(IRelatorioComercialService relatorio) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<RelatorioComercialDto>> Obter(
        [FromQuery] DateOnly? dataInicio, [FromQuery] DateOnly? dataFim,
        [FromQuery] Guid? consultorId, [FromQuery] Guid[]? etapaId,
        [FromQuery] DateOnly? chegadaInicio, [FromQuery] DateOnly? chegadaFim,
        [FromQuery] DateOnly? vendaInicio, [FromQuery] DateOnly? vendaFim,
        [FromQuery] bool? indicacao, [FromQuery] string[]? tipoIndicacao, [FromQuery] Guid? regionalId, CancellationToken ct) =>
        Ok(await relatorio.ObterAsync(dataInicio, dataFim, consultorId, etapaId,
            new RelatorioFiltroExtra(chegadaInicio, chegadaFim, vendaInicio, vendaFim, indicacao, tipoIndicacao, regionalId), ct));

    /// <summary>Os clientes por trás de um card do relatório, com os mesmos filtros do relatório.</summary>
    [HttpGet("detalhe")]
    public async Task<ActionResult<RelatorioDetalheDto>> ObterDetalhe(
        [FromQuery] string tipo,
        [FromQuery] DateOnly? dataInicio, [FromQuery] DateOnly? dataFim,
        [FromQuery] Guid? consultorId, [FromQuery] Guid[]? etapaId,
        [FromQuery] DateOnly? chegadaInicio, [FromQuery] DateOnly? chegadaFim,
        [FromQuery] DateOnly? vendaInicio, [FromQuery] DateOnly? vendaFim,
        [FromQuery] bool? indicacao, [FromQuery] string[]? tipoIndicacao, [FromQuery] Guid? regionalId, CancellationToken ct) =>
        Ok(await relatorio.ObterDetalheAsync(tipo, dataInicio, dataFim, consultorId, etapaId,
            new RelatorioFiltroExtra(chegadaInicio, chegadaFim, vendaInicio, vendaFim, indicacao, tipoIndicacao, regionalId), ct));
}
