using Microsoft.EntityFrameworkCore;
using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Authorization;
using CssVision.Web.Services.Crm;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CssVision.Web.Api.Controllers;

[ApiController]
[Route("api/crm/management")]
[Authorize(Policy = PolicyNames.GestaoComercial)]
public class CrmManagementController(IManagementService managementService, ILeadAssignmentService distribuicao) : ControllerBase
{
    [HttpGet("summary")]
    public async Task<ActionResult<GestaoComercialResumoDto>> ObterResumo(
        [FromQuery] DateOnly? dataInicio, [FromQuery] DateOnly? dataFim, CancellationToken ct) =>
        Ok(await managementService.ObterResumoAsync(dataInicio, dataFim, ct));

    [HttpGet("vendedores")]
    public async Task<ActionResult<IReadOnlyList<VendedorResumoDto>>> ObterVendedores([FromQuery] bool incluirInativos, CancellationToken ct) =>
        Ok(await managementService.ObterVendedoresAsync(ct, incluirInativos));

    [HttpPut("vendedores/{id:guid}/recebe-leads")]
    public async Task<IActionResult> AtualizarRecebeLeads(Guid id, AtualizarRecebeLeadsRequest request, CancellationToken ct)
    {
        await managementService.AtualizarRecebeLeadsAsync(id, request, ct);
        return NoContent();
    }

    [HttpPut("vendedores/{id:guid}/limite")]
    public async Task<IActionResult> AtualizarLimiteMensal(Guid id, AtualizarLimiteMensalRequest request, CancellationToken ct)
    {
        await managementService.AtualizarLimiteMensalAsync(id, request, ct);
        return NoContent();
    }

    [HttpPut("vendedores/{id:guid}/limite-diario")]
    public async Task<IActionResult> AtualizarLimiteDiario(Guid id, AtualizarLimiteDiarioRequest request, CancellationToken ct)
    {
        await managementService.AtualizarLimiteDiarioAsync(id, request, ct);
        return NoContent();
    }

    [HttpPut("vendedores/{id:guid}/janela-recebimento")]
    public async Task<IActionResult> AtualizarJanelaRecebimento(Guid id, AtualizarJanelaRecebimentoRequest request, CancellationToken ct)
    {
        await managementService.AtualizarJanelaRecebimentoAsync(id, request, ct);
        return NoContent();
    }

    [HttpPut("vendedores/{id:guid}/tipos-lead")]
    public async Task<IActionResult> AtualizarTiposLead(Guid id, AtualizarTiposLeadRequest request, CancellationToken ct)
    {
        await managementService.AtualizarTiposLeadAsync(id, request, ct);
        return NoContent();
    }

    /// <summary>Leads parados porque todos os consultores bateram o limite (e a decisão de continuar mesmo assim).</summary>
    [HttpGet("alerta-distribuicao")]
    public async Task<ActionResult<AlertaDistribuicaoDto>> ObterAlertaDistribuicao(CancellationToken ct) =>
        Ok(await distribuicao.ObterEstadoDistribuicaoAsync(ct));

    [HttpPut("continuar-distribuicao")]
    public async Task<ActionResult<AlertaDistribuicaoDto>> ContinuarDistribuicao(ContinuarDistribuicaoRequest request, CancellationToken ct)
    {
        await distribuicao.DefinirContinuarAposLimiteAsync(request.Continuar, ct);
        return Ok(await distribuicao.ObterEstadoDistribuicaoAsync(ct));
    }

    /// <summary>Resumos guardados da sincronização com o Notion: último ciclo e importação completa por base.</summary>
    [HttpGet("sincronizacao-notion")]
    public async Task<ActionResult<IReadOnlyList<ResumoSincronizacaoDto>>> ObterResumosSincronizacao([FromServices] Data.ApplicationDbContext db, CancellationToken ct) =>
        Ok(await db.CrmParametros.AsNoTracking().Where(p => p.Chave.StartsWith("notion:")).OrderBy(p => p.Chave)
            .Select(p => new ResumoSincronizacaoDto(p.Chave.Substring(7), p.Valor)).ToListAsync(ct));

    [HttpGet("consultores")]
    public async Task<ActionResult<IReadOnlyList<ConsultorDesempenhoDto>>> ObterDesempenhoConsultores(
        [FromQuery] DateOnly? mesReferencia, CancellationToken ct) =>
        Ok(await managementService.ObterDesempenhoConsultoresAsync(mesReferencia, ct));

    [HttpGet("redistribuicoes")]
    public async Task<ActionResult<IReadOnlyList<RedistribuicaoHistoricoDto>>> ObterHistorico(CancellationToken ct) =>
        Ok(await managementService.ObterHistoricoRedistribuicoesAsync(ct));
}
