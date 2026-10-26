using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Authorization;
using CssVision.Web.Domain.Identity;
using CssVision.Web.Services.Crm;
using CssVision.Web.Services.Email;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace CssVision.Web.Api.Controllers;

public sealed record EmailSistemaTesteDto(bool Enviado, string Destinatario);

/// <summary>E-mail do sistema (usado na redefinição de senha): configurar, testar e ver se está funcionando. Só administradores.</summary>
[ApiController]
[Route("api/crm/email-sistema")]
[Authorize(Roles = $"{Roles.Admin},{Roles.GestorMaster}")]
public class EmailDoSistemaController(IEmailDoSistemaService emailDoSistema, IEmailSender emailSender, UserManager<ApplicationUser> userManager) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<EmailSistemaDto>> Obter(CancellationToken ct) => Ok(await emailDoSistema.ObterAsync(ct));

    /// <summary>Testa o login no servidor SMTP antes de guardar: o administrador descobre na hora se host, porta ou senha estão errados.</summary>
    [HttpPut]
    public async Task<ActionResult<EmailSistemaDto>> Salvar(EmailSistemaRequest request, CancellationToken ct)
    {
        var senha = string.IsNullOrWhiteSpace(request.Senha) ? await emailDoSistema.SenhaGuardadaAsync(ct) : request.Senha.Trim();
        if (string.IsNullOrEmpty(senha)) throw new CrmBusinessException("Informe a senha (no Gmail ou Outlook, uma senha de app).", "senha_obrigatoria");
        try
        {
            await emailSender.ValidarContaAsync(new ContaSmtp(request.Host.Trim(), request.Port, request.UserName.Trim(), senha, request.FromEmail.Trim()), ct);
        }
        catch (EmailContaException ex)
        {
            throw new CrmBusinessException(ex.Message, "conta_invalida");
        }
        return Ok(await emailDoSistema.SalvarAsync(request, ct));
    }

    [HttpDelete]
    public async Task<IActionResult> Remover(CancellationToken ct)
    {
        await emailDoSistema.RemoverAsync(ct);
        return NoContent();
    }

    /// <summary>Manda um e-mail de teste para o próprio administrador que clicou, pela configuração em uso.</summary>
    [HttpPost("teste")]
    public async Task<ActionResult<EmailSistemaTesteDto>> Testar(CancellationToken ct)
    {
        var usuario = await userManager.GetUserAsync(User);
        if (usuario?.Email is null) throw new CrmBusinessException("Sua conta não tem e-mail cadastrado para receber o teste.", "sem_email");
        var enviado = await emailSender.EnviarAsync(usuario.Email, usuario.NomeCompleto, "Teste do e-mail do sistema — CSS Brasil CRM",
            "<p>Se você recebeu este e-mail, o e-mail do sistema está funcionando: o link de \"Esqueci a senha\" vai chegar.</p>", ct);
        return Ok(new EmailSistemaTesteDto(enviado, usuario.Email));
    }
}
