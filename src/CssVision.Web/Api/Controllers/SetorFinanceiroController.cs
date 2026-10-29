using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Authorization;
using CssVision.Web.Services.Crm;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CssVision.Web.Api.Controllers;

/// <summary>
/// Setor Financeiro: o que a CSS cobra de cada consultor PJ (notebook em parcelas, custo de lead, tráfego pago) e se foi pago. A gestão e o
/// financeiro acompanham e registram; o consultor só vê as próprias cobranças.
/// </summary>
[ApiController]
[Route("api/crm/setor-financeiro")]
public class SetorFinanceiroController(ISetorFinanceiroService setor, ICurrentUserService currentUser) : ControllerBase
{
    [HttpGet("resumo")]
    [Authorize(Policy = PolicyNames.GestaoFinanceira)]
    public async Task<ActionResult<IReadOnlyList<SetorFinanceiroResumoDto>>> Resumo(CancellationToken ct) => Ok(await setor.ResumoAsync(ct));

    /// <param name="consultorId">Vazio = o próprio usuário.</param>
    [HttpGet("cobrancas")]
    [Authorize(Policy = PolicyNames.ContratoFinanceiro)]
    public async Task<ActionResult<SetorFinanceiroConsultorDto>> Cobrancas([FromQuery] Guid? consultorId, CancellationToken ct) =>
        Ok(await setor.ObterAsync(consultorId ?? currentUser.UserId, ct));

    [HttpPut("{consultorId:guid}/pagamentos")]
    [Authorize(Policy = PolicyNames.GestaoFinanceira)]
    [EnableRateLimiting(RateLimitPolicies.FinanceiroEscrita)]
    public async Task<IActionResult> DefinirPagamento(Guid consultorId, CobrancaPagamentoRequest req, CancellationToken ct)
    {
        await setor.DefinirPagamentoAsync(consultorId, req, ct);
        return NoContent();
    }

    /// <summary>O consultor avisa que pagou uma cobrança dele: data, observação e (opcional) o comprovante. O financeiro confere e confirma.</summary>
    [HttpPost("cobrancas/informar")]
    [Authorize(Policy = PolicyNames.ContratoFinanceiro)]
    [EnableRateLimiting(RateLimitPolicies.FinanceiroEscrita)]
    [RequestSizeLimit(12 * 1024 * 1024)]
    public async Task<IActionResult> InformarPagamento([FromForm] InformarPagamentoRequest req, IFormFile? arquivo, CancellationToken ct)
    {
        await setor.InformarPagamentoAsync(req, arquivo, ct);
        return NoContent();
    }

    /// <summary>"Pagar depois": o consultor escolhe o dia em que quer ser lembrado.</summary>
    [HttpPut("cobrancas/lembrete")]
    [Authorize(Policy = PolicyNames.ContratoFinanceiro)]
    [EnableRateLimiting(RateLimitPolicies.FinanceiroEscrita)]
    public async Task<IActionResult> AgendarLembrete(LembreteCobrancaRequest req, CancellationToken ct)
    {
        await setor.AgendarLembreteAsync(req, ct);
        return NoContent();
    }

    /// <summary>O financeiro não confirmou o pagamento informado: devolve a cobrança ao consultor com o motivo.</summary>
    [HttpPut("{consultorId:guid}/pagamentos/recusar")]
    [Authorize(Policy = PolicyNames.GestaoFinanceira)]
    [EnableRateLimiting(RateLimitPolicies.FinanceiroEscrita)]
    public async Task<IActionResult> RecusarPagamento(Guid consultorId, RecusarPagamentoRequest req, CancellationToken ct)
    {
        await setor.RecusarPagamentoAsync(consultorId, req, ct);
        return NoContent();
    }

    /// <summary>Acerta uma cobrança de um mês: troca o valor, remove (ou restaura). O valor calculado pelo contrato fica guardado.</summary>
    [HttpPut("{consultorId:guid}/cobrancas/ajuste")]
    [Authorize(Policy = PolicyNames.GestaoFinanceira)]
    [EnableRateLimiting(RateLimitPolicies.FinanceiroEscrita)]
    public async Task<IActionResult> AjustarCobranca(Guid consultorId, CobrancaAjusteRequest req, CancellationToken ct)
    {
        await setor.AjustarCobrancaAsync(consultorId, req, ct);
        return NoContent();
    }

    /// <summary>O financeiro lança um custo para o consultor (notebook, celular...), pago em parcelas mensais.</summary>
    [HttpPost("{consultorId:guid}/custos")]
    [Authorize(Policy = PolicyNames.GestaoFinanceira)]
    [EnableRateLimiting(RateLimitPolicies.FinanceiroEscrita)]
    public async Task<ActionResult<CustoConsultorDto>> AdicionarCusto(Guid consultorId, CustoConsultorRequest req, CancellationToken ct) =>
        Ok(await setor.AdicionarCustoAsync(consultorId, req, ct));

    /// <summary>Edita um custo já lançado (tipo, descrição, valor, parcelas e vencimento da primeira).</summary>
    [HttpPut("{consultorId:guid}/custos/{custoId:guid}")]
    [Authorize(Policy = PolicyNames.GestaoFinanceira)]
    [EnableRateLimiting(RateLimitPolicies.FinanceiroEscrita)]
    public async Task<ActionResult<CustoConsultorDto>> AtualizarCusto(Guid consultorId, Guid custoId, CustoConsultorRequest req, CancellationToken ct) =>
        Ok(await setor.AtualizarCustoAsync(consultorId, custoId, req, ct));

    [HttpDelete("{consultorId:guid}/custos/{custoId:guid}")]
    [Authorize(Policy = PolicyNames.GestaoFinanceira)]
    [EnableRateLimiting(RateLimitPolicies.FinanceiroEscrita)]
    public async Task<IActionResult> RemoverCusto(Guid consultorId, Guid custoId, CancellationToken ct)
    {
        await setor.RemoverCustoAsync(consultorId, custoId, ct);
        return NoContent();
    }
}
