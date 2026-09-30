using CssVision.Web.Authorization;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Backup;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CssVision.Web.Api.Controllers;

/// <summary>
/// Backups do banco. Só administradores: o arquivo é a base inteira (clientes, CPFs, usuários),
/// por isso cada download fica registrado na auditoria.
/// </summary>
[ApiController]
[Route("api/admin/backups")]
[Authorize(Roles = Roles.Admin)]
public class BackupsController(IBackupService backups) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult> Listar(CancellationToken ct) => Ok(await backups.ListarAsync(ct));

    [HttpPost]
    public async Task<ActionResult> GerarAgora(CancellationToken ct) => Ok(await backups.GerarAsync(OrigemBackup.Manual, ct));

    [HttpGet("{id:guid}/download")]
    public async Task<IActionResult> Baixar(Guid id, CancellationToken ct)
    {
        var (conteudo, nome) = await backups.AbrirAsync(id, ct);
        return File(conteudo, "application/octet-stream", nome);
    }
}
