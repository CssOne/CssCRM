using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Services.Crm;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CssVision.Web.Api.Controllers;

/// <summary>Aba Suporte: qualquer usuário logado (inclusive Marketing) abre e acompanha chamados.</summary>
[ApiController]
[Route("api/crm/suporte")]
[Authorize]
public class CrmSuporteController(ISuporteService suporte) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<SuporteListaDto>> Listar(CancellationToken ct) => Ok(await suporte.ListarAsync(ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SuporteChamadoDto>> Obter(Guid id, CancellationToken ct) => Ok(await suporte.ObterAsync(id, ct));

    [HttpPost]
    public async Task<ActionResult<SuporteChamadoDto>> Criar(SuporteChamadoCreateRequest request, CancellationToken ct) =>
        Ok(await suporte.CriarAsync(request, ct));

    [HttpPost("{id:guid}/mensagens")]
    public async Task<ActionResult<SuporteChamadoDto>> Responder(Guid id, SuporteMensagemRequest request, CancellationToken ct) =>
        Ok(await suporte.ResponderAsync(id, request, ct));

    [HttpPut("{id:guid}/status")]
    public async Task<ActionResult<SuporteChamadoDto>> AlterarStatus(Guid id, SuporteStatusRequest request, CancellationToken ct) =>
        Ok(await suporte.AlterarStatusAsync(id, request.Status, ct));
}
