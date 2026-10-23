using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Authorization;
using CssVision.Web.Services.Crm;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CssVision.Web.Api.Controllers;

[ApiController]
[Route("api/crm/disparo-email")]
[Authorize(Policy = PolicyNames.AreaComercial)]
public class CrmDisparoEmailController(IDisparoEmailService service) : ControllerBase
{
    /// <summary>Conta de e-mail do consultor usada nos disparos (204 se ainda não conectou).</summary>
    [HttpGet("conta")]
    public async Task<ActionResult<ContaEmailDto>> ObterConta(CancellationToken ct)
    {
        var conta = await service.ObterContaAsync(ct);
        return conta is null ? NoContent() : Ok(conta);
    }

    /// <summary>Conecta (ou troca) o e-mail do consultor; testa o login antes de guardar.</summary>
    [HttpPut("conta")]
    public async Task<ActionResult<ContaEmailDto>> SalvarConta(ContaEmailRequest request, CancellationToken ct) =>
        Ok(await service.SalvarContaAsync(request, ct));

    [HttpDelete("conta")]
    public async Task<IActionResult> RemoverConta(CancellationToken ct)
    {
        await service.RemoverContaAsync(ct);
        return NoContent();
    }

    /// <summary>Quantos clientes do consultor (com e-mail) estão na etapa.</summary>
    [HttpGet("previa")]
    public async Task<ActionResult<DisparoEmailPreviaDto>> Previa([FromQuery] Guid etapaId, CancellationToken ct) =>
        Ok(await service.PreviaAsync(etapaId, ct));

    [HttpPost]
    public async Task<ActionResult<DisparoEmailResultadoDto>> Enviar(DisparoEmailRequest request, CancellationToken ct) =>
        Ok(await service.EnviarAsync(request, ct));
}
