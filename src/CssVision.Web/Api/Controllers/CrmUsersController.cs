using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Authorization;
using CssVision.Web.Services.Crm;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CssVision.Web.Api.Controllers;

/// <summary>
/// Gestão de usuários (consultores, gestores comerciais e administradores). Aberto a qualquer
/// papel de gestão comercial — o escopo fino (Admin/GestorMaster veem tudo; GestorComercial só a
/// própria regional, só papel Comercial) é aplicado dentro do <see cref="IUserManagementService"/>.
/// </summary>
[ApiController]
[Route("api/crm/users")]
[Authorize(Policy = PolicyNames.GestaoComercial)]
public class CrmUsersController(IUserManagementService userService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult> Listar([FromQuery] UserFilterRequest filtro, CancellationToken ct) =>
        Ok(await userService.ListarAsync(filtro, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<UserSummaryDto>> ObterPorId(Guid id, CancellationToken ct) =>
        Ok(await userService.ObterPorIdAsync(id, ct));

    [HttpPost]
    public async Task<ActionResult<UserSummaryDto>> Criar(UserCreateRequest request, CancellationToken ct)
    {
        var resultado = await userService.CriarAsync(request, ct);
        return CreatedAtAction(nameof(ObterPorId), new { id = resultado.Id }, resultado);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<UserSummaryDto>> Atualizar(Guid id, UserUpdateRequest request, CancellationToken ct) =>
        Ok(await userService.AtualizarAsync(id, request, ct));

    [HttpPost("{id:guid}/reset-password")]
    public async Task<IActionResult> RedefinirSenha(Guid id, ResetPasswordRequest request, CancellationToken ct)
    {
        await userService.RedefinirSenhaAsync(id, request, ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Excluir(Guid id, CancellationToken ct)
    {
        await userService.ExcluirAsync(id, ct);
        return NoContent();
    }
}
