using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Authorization;
using CssVision.Web.Services.Crm;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CssVision.Web.Api.Controllers;

/// <summary>
/// Contrato dos grupos (dentro das regionais): o que vale para todo consultor do grupo, com vigência mensal. Consultar: a gestão financeira.
/// Definir: administrador, gestor master e supervisor — a regra fica em <see cref="IContratoGrupoService"/>.
/// </summary>
[ApiController]
[Route("api/portal/financeiro/grupos")]
[Authorize(Policy = PolicyNames.ContratoFinanceiro)]
public class PortalFinanceiroGruposController(IContratoGrupoService grupos) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<GrupoContratoResumoDto>>> Listar(CancellationToken ct) => Ok(await grupos.ListarAsync(ct));

    [HttpGet("{grupoId:guid}")]
    public async Task<ActionResult<GrupoContratoDto>> Obter(Guid grupoId, CancellationToken ct) => Ok(await grupos.ObterAsync(grupoId, ct));

    [HttpPut("{grupoId:guid}/perfil")]
    [EnableRateLimiting(RateLimitPolicies.FinanceiroEscrita)]
    public async Task<IActionResult> SalvarPerfil(Guid grupoId, GrupoPerfilRequest req, CancellationToken ct)
    {
        await grupos.SalvarPerfilAsync(grupoId, req, ct);
        return NoContent();
    }

    [HttpPut("{grupoId:guid}/valores")]
    [EnableRateLimiting(RateLimitPolicies.FinanceiroEscrita)]
    public async Task<IActionResult> DefinirValor(Guid grupoId, GrupoValorRequest req, CancellationToken ct)
    {
        await grupos.DefinirValorAsync(grupoId, req, ct);
        return NoContent();
    }

    [HttpDelete("{grupoId:guid}/valores/{valorId:guid}")]
    [EnableRateLimiting(RateLimitPolicies.FinanceiroEscrita)]
    public async Task<IActionResult> RemoverValor(Guid grupoId, Guid valorId, CancellationToken ct)
    {
        await grupos.RemoverValorAsync(grupoId, valorId, ct);
        return NoContent();
    }

    [HttpPut("{grupoId:guid}/prazos")]
    [EnableRateLimiting(RateLimitPolicies.FinanceiroEscrita)]
    public async Task<IActionResult> DefinirPrazo(Guid grupoId, GrupoPrazoRequest req, CancellationToken ct)
    {
        await grupos.DefinirPrazoAsync(grupoId, req, ct);
        return NoContent();
    }
}
