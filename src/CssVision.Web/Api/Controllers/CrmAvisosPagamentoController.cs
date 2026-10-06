using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Authorization;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CssVision.Web.Api.Controllers;

/// <summary>Avisos de pagamento em aberto: gestão e financeiro enviam e acompanham; o consultor vê os seus e dá o "ciente".</summary>
[ApiController]
[Route("api/crm/avisos-pagamento")]
public class CrmAvisosPagamentoController(IAvisosPagamentoService avisos) : ControllerBase
{
    [HttpPost]
    [Authorize(Policy = PolicyNames.GestaoFinanceira)]
    public async Task<ActionResult<IReadOnlyList<AvisoPagamentoDto>>> Enviar(AvisoPagamentoCreateRequest request, CancellationToken ct) =>
        Ok(await avisos.EnviarAsync(request, ct));

    [HttpGet]
    [Authorize(Policy = PolicyNames.GestaoFinanceira)]
    public async Task<ActionResult<IReadOnlyList<AvisoPagamentoDto>>> Listar(
        [FromQuery] StatusAvisoConsultor? status, [FromQuery] Guid? consultorId, CancellationToken ct) =>
        Ok(await avisos.ListarAsync(status, consultorId, ct));

    [HttpPut("{id:guid}/resolver")]
    [Authorize(Policy = PolicyNames.GestaoFinanceira)]
    public async Task<ActionResult<AvisoPagamentoDto>> Resolver(Guid id, CancellationToken ct) => Ok(await avisos.ResolverAsync(id, ct));

    [HttpPut("{id:guid}/reenviar")]
    [Authorize(Policy = PolicyNames.GestaoFinanceira)]
    public async Task<ActionResult<AvisoPagamentoDto>> Reenviar(Guid id, CancellationToken ct) => Ok(await avisos.ReenviarAsync(id, ct));

    /// <summary>Quantos avisos em aberto cada consultor tem (aparece na gestão comercial do financeiro).</summary>
    [HttpGet("resumo")]
    [Authorize(Policy = PolicyNames.GestaoFinanceira)]
    public async Task<ActionResult<IReadOnlyList<AvisoResumoConsultorDto>>> Resumo(CancellationToken ct) => Ok(await avisos.ResumoAsync(ct));

    /// <summary>Avisos abertos do consultor logado (notificação e card "Avisos importantes").</summary>
    [HttpGet("meus")]
    [Authorize(Policy = PolicyNames.AreaComercial)]
    public async Task<ActionResult<IReadOnlyList<MeuAvisoDto>>> Meus(CancellationToken ct) => Ok(await avisos.MeusAsync(ct));

    [HttpPut("meus/{id:guid}/ciente")]
    [Authorize(Policy = PolicyNames.AreaComercial)]
    public async Task<IActionResult> Ciente(Guid id, CancellationToken ct)
    {
        await avisos.MarcarCienteAsync(id, ct);
        return NoContent();
    }
}
