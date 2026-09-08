using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Authorization;
using CssVision.Web.Services.Crm;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CssVision.Web.Api.Controllers;

[ApiController]
[Route("api/crm/leads")]
[Authorize(Policy = PolicyNames.AreaComercial)]
public class CrmLeadsController(ILeadService leadService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult> Listar([FromQuery] LeadFilterRequest filtro, CancellationToken ct) =>
        Ok(await leadService.ListarAsync(filtro, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<LeadDetailDto>> ObterPorId(Guid id, CancellationToken ct) =>
        Ok(await leadService.ObterPorIdAsync(id, ct));

    [HttpGet("{id:guid}/timeline")]
    public async Task<ActionResult<IReadOnlyList<LeadTimelineItemDto>>> ObterTimeline(Guid id, CancellationToken ct) =>
        Ok(await leadService.ObterTimelineAsync(id, ct));

    [HttpPost]
    public async Task<ActionResult> Criar(LeadCreateRequest request, CancellationToken ct)
    {
        var resultado = await leadService.CriarAsync(request, ct);
        if (resultado.Duplicidade is not null)
        {
            var d = resultado.Duplicidade;
            return Conflict(new ApiError($"Já existe um lead com o mesmo {d.CampoDuplicado} ({d.NomeExistente}).", "duplicidade", d));
        }
        return CreatedAtAction(nameof(ObterPorId), new { id = resultado.Lead!.Id }, resultado.Lead);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<LeadDetailDto>> Atualizar(Guid id, LeadUpdateRequest request, CancellationToken ct) =>
        Ok(await leadService.AtualizarAsync(id, request, ct));

    [HttpPost("{id:guid}/assign")]
    [Authorize(Policy = PolicyNames.GestaoComercial)]
    public async Task<IActionResult> Atribuir(Guid id, LeadAssignRequest request, CancellationToken ct)
    {
        await leadService.AtribuirAsync(id, request, ct);
        return NoContent();
    }

    [HttpPost("bulk-assign")]
    [Authorize(Policy = PolicyNames.GestaoComercial)]
    public async Task<ActionResult> AtribuirEmLote(LeadBulkAssignRequest request, CancellationToken ct) =>
        Ok(new { quantidade = await leadService.AtribuirEmLoteAsync(request, ct) });

    [HttpPost("{id:guid}/notes")]
    public async Task<ActionResult> AdicionarNota(Guid id, AddNoteRequest request, CancellationToken ct) =>
        Ok(new { id = await leadService.AdicionarNotaAsync(id, request.Texto, ct) });

    [HttpPost("import")]
    [Authorize(Policy = PolicyNames.GestaoComercial)]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<ActionResult<LeadImportResultDto>> Importar(IFormFile arquivo, CancellationToken ct)
    {
        await using var stream = arquivo.OpenReadStream();
        return Ok(await leadService.ImportarAsync(stream, ct));
    }

    [HttpGet("export")]
    public async Task<IActionResult> Exportar([FromQuery] LeadFilterRequest filtro, CancellationToken ct)
    {
        var conteudo = await leadService.ExportarAsync(filtro, ct);
        return File(conteudo, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "leads.xlsx");
    }
}
