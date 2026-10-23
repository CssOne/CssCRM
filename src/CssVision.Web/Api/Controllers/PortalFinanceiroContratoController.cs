using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Authorization;
using CssVision.Web.Services.Crm;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CssVision.Web.Api.Controllers;

/// <summary>
/// Contrato financeiro do consultor (PJ/CLT, bônus por placa, recorrência, salário e custos fixos) e o histórico de valores mês a mês.
/// Consultar: o próprio consultor ou a gestão do escopo dele. Definir/alterar: só a gestão (administrador, gestor ou financeiro) — a
/// regra fica em <see cref="IFinanceiroContratoService"/>.
/// </summary>
[ApiController]
[Route("api/portal/financeiro/contrato")]
[Authorize(Policy = PolicyNames.ContratoFinanceiro)]
public class PortalFinanceiroContratoController(IFinanceiroContratoService contrato, ICurrentUserService currentUser) : ControllerBase
{
    /// <param name="consultor">Vazio = o próprio usuário.</param>
    [HttpGet]
    public async Task<ActionResult<FinanceiroContratoDto>> Obter([FromQuery] Guid? consultor, [FromQuery] int? ano, CancellationToken ct) =>
        Ok(await contrato.ObterAsync(consultor ?? currentUser.UserId, ano, ct));

    [HttpPut("{consultorId:guid}/perfil")]
    [EnableRateLimiting(RateLimitPolicies.FinanceiroEscrita)]
    public async Task<IActionResult> SalvarPerfil(Guid consultorId, FinanceiroPerfilRequest req, CancellationToken ct)
    {
        await contrato.SalvarPerfilAsync(consultorId, req, ct);
        return NoContent();
    }

    [HttpPut("{consultorId:guid}/valores")]
    [EnableRateLimiting(RateLimitPolicies.FinanceiroEscrita)]
    public async Task<IActionResult> DefinirValor(Guid consultorId, FinanceiroValorRequest req, CancellationToken ct)
    {
        await contrato.DefinirValorAsync(consultorId, req, ct);
        return NoContent();
    }

    /// <summary>Prazo do consultor para começar a pagar um custo fixo (nulo volta ao padrão).</summary>
    [HttpPut("{consultorId:guid}/prazos")]
    [EnableRateLimiting(RateLimitPolicies.FinanceiroEscrita)]
    public async Task<IActionResult> DefinirPrazo(Guid consultorId, FinanceiroPrazoRequest req, CancellationToken ct)
    {
        await contrato.DefinirPrazoAsync(consultorId, req, ct);
        return NoContent();
    }

    /// <summary>"Deixar começar": a gestão confirma o aviso de que a cobrança vai começar e ele some.</summary>
    [HttpPost("{consultorId:guid}/prazos/{parametro}/confirmar")]
    [EnableRateLimiting(RateLimitPolicies.FinanceiroEscrita)]
    public async Task<IActionResult> ConfirmarInicio(Guid consultorId, string parametro, CancellationToken ct)
    {
        await contrato.ConfirmarInicioAsync(consultorId, parametro, ct);
        return NoContent();
    }

    /// <summary>Avisos de custos fixos que começam a ser cobrados em breve: do próprio consultor e, para a gestão, dos consultores do escopo dela.</summary>
    [HttpGet("alertas")]
    public async Task<ActionResult<IReadOnlyList<FinanceiroAlertaCobrancaDto>>> Alertas(CancellationToken ct) =>
        Ok(await contrato.ListarAlertasAsync(ct));

    /// <summary>Valores e prazos padrão dos custos fixos de todos os consultores PJ.</summary>
    [HttpGet("padroes")]
    public async Task<ActionResult<FinanceiroPadroesDto>> Padroes(CancellationToken ct) => Ok(await contrato.ObterPadroesAsync(ct));

    [HttpPut("padroes")]
    [EnableRateLimiting(RateLimitPolicies.FinanceiroEscrita)]
    public async Task<IActionResult> DefinirPadrao(FinanceiroPadraoRequest req, CancellationToken ct)
    {
        await contrato.DefinirPadraoAsync(req, ct);
        return NoContent();
    }

    [HttpDelete("{consultorId:guid}/valores/{valorId:guid}")]
    [EnableRateLimiting(RateLimitPolicies.FinanceiroEscrita)]
    public async Task<IActionResult> RemoverValor(Guid consultorId, Guid valorId, CancellationToken ct)
    {
        await contrato.RemoverValorAsync(consultorId, valorId, ct);
        return NoContent();
    }
}
