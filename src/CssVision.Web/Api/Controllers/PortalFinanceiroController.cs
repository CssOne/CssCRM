using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Authorization;
using CssVision.Web.Services.Crm;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CssVision.Web.Api.Controllers;

/// <summary>
/// Controle financeiro do consultor (portal). Só recebe a requisição e devolve a resposta: validação, regras de acesso,
/// auditoria e gravação ficam em <see cref="IFinanceiroService"/>; erros de negócio viram respostas padronizadas pelo
/// CrmExceptionHandler. As gravações têm limite de requisições por usuário.
/// </summary>
[ApiController]
[Route("api/portal/financeiro")]
[Authorize(Policy = PolicyNames.AreaComercial)]
public class PortalFinanceiroController(IFinanceiroService financeiro) : ControllerBase
{
    /// <summary>Consultores para a gestão escolher de quem ver os números (só o papel de consultor, sem administradores).</summary>
    [HttpGet("consultores")]
    public async Task<ActionResult<IReadOnlyList<ConsultorFinanceiroDto>>> Consultores(CancellationToken ct) => Ok(await financeiro.ListarConsultoresAsync(ct));

    [HttpGet]
    public async Task<ActionResult<FinanceiroAnoDto>> Obter([FromQuery] int? ano, [FromQuery] string? consultor, CancellationToken ct) =>
        Ok(await financeiro.ObterAnoAsync(ano, consultor, ct));

    [HttpPut("config")]
    [EnableRateLimiting(RateLimitPolicies.FinanceiroEscrita)]
    public async Task<IActionResult> SalvarConfig([FromQuery] int ano, FinanceiroConfigDto req, CancellationToken ct)
    {
        await financeiro.SalvarConfigAsync(ano, req, ct);
        return NoContent();
    }

    [HttpPut("metas/{ano:int}")]
    [EnableRateLimiting(RateLimitPolicies.FinanceiroEscrita)]
    public async Task<IActionResult> SalvarMeta(int ano, FinanceiroMetaDto req, CancellationToken ct)
    {
        await financeiro.SalvarMetaAsync(ano, req, ct);
        return NoContent();
    }

    [HttpPost("vendas")]
    [EnableRateLimiting(RateLimitPolicies.FinanceiroEscrita)]
    public async Task<ActionResult<FinanceiroVendaDto>> CriarVenda(FinanceiroVendaDto req, CancellationToken ct) =>
        Ok(await financeiro.SalvarVendaAsync(null, req, ct));

    [HttpPut("vendas/{id:guid}")]
    [EnableRateLimiting(RateLimitPolicies.FinanceiroEscrita)]
    public async Task<ActionResult<FinanceiroVendaDto>> EditarVenda(Guid id, FinanceiroVendaDto req, CancellationToken ct) =>
        Ok(await financeiro.SalvarVendaAsync(id, req, ct));

    [HttpDelete("vendas/{id:guid}")]
    [EnableRateLimiting(RateLimitPolicies.FinanceiroEscrita)]
    public async Task<IActionResult> ExcluirVenda(Guid id, CancellationToken ct)
    {
        await financeiro.ExcluirVendaAsync(id, ct);
        return NoContent();
    }

    [HttpPost("custos")]
    [EnableRateLimiting(RateLimitPolicies.FinanceiroEscrita)]
    public async Task<ActionResult<FinanceiroCustoDto>> CriarCusto(FinanceiroCustoDto req, CancellationToken ct) =>
        Ok(await financeiro.SalvarCustoAsync(null, req, ct));

    [HttpPut("custos/{id:guid}")]
    [EnableRateLimiting(RateLimitPolicies.FinanceiroEscrita)]
    public async Task<ActionResult<FinanceiroCustoDto>> EditarCusto(Guid id, FinanceiroCustoDto req, CancellationToken ct) =>
        Ok(await financeiro.SalvarCustoAsync(id, req, ct));

    [HttpDelete("custos/{id:guid}")]
    [EnableRateLimiting(RateLimitPolicies.FinanceiroEscrita)]
    public async Task<IActionResult> ExcluirCusto(Guid id, CancellationToken ct)
    {
        await financeiro.ExcluirCustoAsync(id, ct);
        return NoContent();
    }
}
