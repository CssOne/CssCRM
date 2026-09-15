using System.Web;
using CssVision.Web.Api.Contracts;
using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Authorization;
using CssVision.Web.Data;
using CssVision.Web.Domain.Identity;
using CssVision.Web.Services.Email;
using CssVision.Web.Services.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CssVision.Web.Api.Controllers;

[ApiController]
[Route("api/account")]
public class AccountController(
    SignInManager<ApplicationUser> signInManager,
    UserManager<ApplicationUser> userManager,
    ApplicationDbContext db,
    IFileStorageService armazenamento,
    IEmailSender emailSender) : ControllerBase
{
    private static readonly string[] ExtensoesFotoPermitidas = [".jpg", ".jpeg", ".png", ".webp"];

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResultDto>> Login(LoginRequest request, CancellationToken ct)
    {
        var usuario = await userManager.FindByEmailAsync(request.Email.Trim());
        if (usuario is null || !usuario.Ativo)
        {
            return Unauthorized(new ApiError("E-mail ou senha inválidos.", "credenciais_invalidas"));
        }

        var resultado = await signInManager.PasswordSignInAsync(usuario, request.Senha, isPersistent: request.ManterConectado, lockoutOnFailure: true);
        if (resultado.RequiresTwoFactor)
        {
            return Ok(new LoginResultDto(true, null));
        }
        if (!resultado.Succeeded)
        {
            return Unauthorized(new ApiError("E-mail ou senha inválidos.", "credenciais_invalidas"));
        }

        return Ok(new LoginResultDto(false, await MontarSessaoAsync(usuario)));
    }

    /// <summary>Segunda etapa do login quando a conta tem verificação em dois fatores ativada.</summary>
    [HttpPost("login/2fa")]
    [AllowAnonymous]
    public async Task<ActionResult<SessionDto>> LoginDoisFatores(TwoFactorLoginRequest request, CancellationToken ct)
    {
        var usuario = await signInManager.GetTwoFactorAuthenticationUserAsync();
        if (usuario is null)
        {
            return Unauthorized(new ApiError("Sessão de login expirada. Entre com seu e-mail e senha novamente.", "sessao_2fa_expirada"));
        }

        var codigo = request.Codigo.Replace(" ", string.Empty).Replace("-", string.Empty);
        var resultado = request.CodigoRecuperacao
            ? await signInManager.TwoFactorRecoveryCodeSignInAsync(codigo)
            : await signInManager.TwoFactorAuthenticatorSignInAsync(codigo, request.ManterConectado, rememberClient: false);

        if (!resultado.Succeeded)
        {
            return Unauthorized(new ApiError("Código inválido.", "codigo_2fa_invalido"));
        }

        return Ok(await MontarSessaoAsync(usuario));
    }

    /// <summary>
    /// Sempre responde 204, exista ou não uma conta com esse e-mail — evita que alguém descubra
    /// quais e-mails têm cadastro testando esse endpoint. Se existir e a conta estiver ativa,
    /// dispara um e-mail com o link de redefinição; falha de envio (SMTP não configurado, fora
    /// do ar etc.) é logada mas não vaza pro cliente, pelo mesmo motivo.
    /// </summary>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    public async Task<IActionResult> EsqueciSenha(ForgotPasswordRequest request, CancellationToken ct)
    {
        var usuario = await userManager.FindByEmailAsync(request.Email.Trim());
        if (usuario is not null && usuario.Ativo)
        {
            var token = await userManager.GeneratePasswordResetTokenAsync(usuario);
            var link = $"{Request.Scheme}://{Request.Host}/reset-password?email={HttpUtility.UrlEncode(usuario.Email)}&token={HttpUtility.UrlEncode(token)}";

            var corpo = $"""
                <p>Olá, {usuario.NomeCompleto}.</p>
                <p>Recebemos um pedido para redefinir a senha da sua conta no CSS Brasil CRM.</p>
                <p><a href="{link}">Clique aqui para escolher uma nova senha</a>.</p>
                <p>Se você não solicitou essa redefinição, pode ignorar este e-mail com segurança — sua senha continua a mesma.</p>
                """;

            await emailSender.EnviarAsync(usuario.Email!, usuario.NomeCompleto, "Redefinição de senha — CSS Brasil CRM", corpo, ct);
        }

        return NoContent();
    }

    [HttpPost("reset-password")]
    [AllowAnonymous]
    public async Task<IActionResult> RedefinirSenhaComToken(ResetPasswordConfirmRequest request, CancellationToken ct)
    {
        var usuario = await userManager.FindByEmailAsync(request.Email.Trim())
            ?? throw new CrmBusinessException("Link inválido ou expirado. Solicite a redefinição novamente.", "token_invalido");

        var resultado = await userManager.ResetPasswordAsync(usuario, request.Token, request.NovaSenha);
        if (!resultado.Succeeded)
        {
            throw new CrmBusinessException(IdentityErrors.Traduzir(resultado), "token_invalido");
        }

        return NoContent();
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await signInManager.SignOutAsync();
        return NoContent();
    }

    [HttpGet("session")]
    public async Task<ActionResult<SessionDto>> Session()
    {
        if (User.Identity?.IsAuthenticated != true) return Unauthorized();

        var usuario = await userManager.GetUserAsync(User);
        if (usuario is null) return Unauthorized();

        return Ok(await MontarSessaoAsync(usuario));
    }

    // --- Perfil da própria conta ---

    [HttpGet("profile")]
    public async Task<ActionResult<ProfileDto>> ObterPerfil(CancellationToken ct)
    {
        var usuario = await ObterUsuarioAtualAsync();
        if (usuario is null) return Unauthorized();

        return Ok(await MontarPerfilAsync(usuario));
    }

    [HttpPut("profile")]
    public async Task<ActionResult<ProfileDto>> AtualizarPerfil(UpdateProfileRequest request, CancellationToken ct)
    {
        var usuario = await ObterUsuarioAtualAsync();
        if (usuario is null) return Unauthorized();

        usuario.NomeCompleto = request.NomeCompleto.Trim();
        await userManager.SetPhoneNumberAsync(usuario, string.IsNullOrWhiteSpace(request.Telefone) ? null : request.Telefone.Trim());

        var novoEmail = request.Email.Trim();
        if (!string.Equals(novoEmail, usuario.Email, StringComparison.OrdinalIgnoreCase))
        {
            var emailResultado = await userManager.SetEmailAsync(usuario, novoEmail);
            if (!emailResultado.Succeeded)
            {
                throw new CrmBusinessException(IdentityErrors.Traduzir(emailResultado), "perfil_invalido");
            }
            await userManager.SetUserNameAsync(usuario, novoEmail);
        }

        var resultado = await userManager.UpdateAsync(usuario);
        if (!resultado.Succeeded)
        {
            throw new CrmBusinessException(IdentityErrors.Traduzir(resultado), "perfil_invalido");
        }

        return Ok(await MontarPerfilAsync(usuario));
    }

    [HttpPost("photo")]
    [RequestSizeLimit(5 * 1024 * 1024)]
    public async Task<ActionResult<ProfileDto>> AtualizarFoto(IFormFile arquivo, CancellationToken ct)
    {
        var usuario = await ObterUsuarioAtualAsync();
        if (usuario is null) return Unauthorized();

        if (arquivo is null || arquivo.Length == 0)
        {
            throw new CrmBusinessException("Selecione uma imagem.", "foto_invalida");
        }
        if (arquivo.Length > 5 * 1024 * 1024)
        {
            throw new CrmBusinessException("A imagem deve ter no máximo 5 MB.", "foto_invalida");
        }

        var extensao = Path.GetExtension(arquivo.FileName).ToLowerInvariant();
        if (!ExtensoesFotoPermitidas.Contains(extensao))
        {
            throw new CrmBusinessException("Formato inválido. Envie uma imagem JPG, PNG ou WEBP.", "foto_invalida");
        }

        await ExcluirArquivoFotoAtualAsync(usuario, ct);

        var nomeArquivo = $"{usuario.Id}{extensao}";
        await using var stream = arquivo.OpenReadStream();
        usuario.FotoUrl = await armazenamento.SalvarAsync("avatars", nomeArquivo, stream, arquivo.ContentType, ct);
        await userManager.UpdateAsync(usuario);

        return Ok(await MontarPerfilAsync(usuario));
    }

    [HttpDelete("photo")]
    public async Task<ActionResult<ProfileDto>> RemoverFoto(CancellationToken ct)
    {
        var usuario = await ObterUsuarioAtualAsync();
        if (usuario is null) return Unauthorized();

        await ExcluirArquivoFotoAtualAsync(usuario, ct);
        usuario.FotoUrl = null;
        await userManager.UpdateAsync(usuario);

        return Ok(await MontarPerfilAsync(usuario));
    }

    [HttpPost("change-password")]
    public async Task<IActionResult> AlterarSenha(ChangePasswordRequest request, CancellationToken ct)
    {
        var usuario = await ObterUsuarioAtualAsync();
        if (usuario is null) return Unauthorized();

        var resultado = await userManager.ChangePasswordAsync(usuario, request.SenhaAtual, request.NovaSenha);
        if (!resultado.Succeeded)
        {
            throw new CrmBusinessException(IdentityErrors.Traduzir(resultado), "senha_invalida");
        }

        // Troca de senha invalida a sessão atual no navegador do Identity — atualiza o cookie pra
        // não deslogar o usuário no meio da própria tela de configurações.
        await signInManager.RefreshSignInAsync(usuario);

        return NoContent();
    }

    // --- Verificação em dois fatores (aplicativo autenticador) ---

    [HttpGet("2fa/setup")]
    public async Task<ActionResult<TwoFactorSetupDto>> ConfigurarDoisFatores(CancellationToken ct)
    {
        var usuario = await ObterUsuarioAtualAsync();
        if (usuario is null) return Unauthorized();

        var chave = await userManager.GetAuthenticatorKeyAsync(usuario);
        if (string.IsNullOrEmpty(chave))
        {
            await userManager.ResetAuthenticatorKeyAsync(usuario);
            chave = await userManager.GetAuthenticatorKeyAsync(usuario);
        }

        return Ok(new TwoFactorSetupDto(FormatarChave(chave!), GerarUriQrCode(usuario.Email!, chave!)));
    }

    [HttpPost("2fa/enable")]
    public async Task<ActionResult<TwoFactorEnableResultDto>> AtivarDoisFatores(TwoFactorEnableRequest request, CancellationToken ct)
    {
        var usuario = await ObterUsuarioAtualAsync();
        if (usuario is null) return Unauthorized();

        var codigo = request.Codigo.Replace(" ", string.Empty);
        var valido = await userManager.VerifyTwoFactorTokenAsync(usuario, TokenOptions.DefaultAuthenticatorProvider, codigo);
        if (!valido)
        {
            throw new CrmBusinessException("Código inválido. Confira o horário do seu celular e tente novamente.", "codigo_2fa_invalido");
        }

        await userManager.SetTwoFactorEnabledAsync(usuario, true);
        var codigosRecuperacao = await userManager.GenerateNewTwoFactorRecoveryCodesAsync(usuario, 8);

        return Ok(new TwoFactorEnableResultDto(codigosRecuperacao?.ToList() ?? []));
    }

    [HttpPost("2fa/disable")]
    public async Task<IActionResult> DesativarDoisFatores(TwoFactorDisableRequest request, CancellationToken ct)
    {
        var usuario = await ObterUsuarioAtualAsync();
        if (usuario is null) return Unauthorized();

        if (!await userManager.CheckPasswordAsync(usuario, request.Senha))
        {
            throw new CrmBusinessException("Senha incorreta.", "senha_invalida");
        }

        await userManager.SetTwoFactorEnabledAsync(usuario, false);
        await userManager.ResetAuthenticatorKeyAsync(usuario);

        return NoContent();
    }

    // --- Exclusão da própria conta (restrita a Admin/GestorMaster) ---

    [HttpPost("delete-account")]
    [Authorize(Policy = PolicyNames.AreaAdministrativa)]
    public async Task<IActionResult> ExcluirPropriaConta(DeleteAccountRequest request, CancellationToken ct)
    {
        var usuario = await ObterUsuarioAtualAsync();
        if (usuario is null) return Unauthorized();

        if (!await userManager.CheckPasswordAsync(usuario, request.Senha))
        {
            throw new CrmBusinessException("Senha incorreta.", "senha_invalida");
        }

        var id = usuario.Id;
        var possuiVinculos =
            await db.CrmLeads.AnyAsync(l => l.ResponsavelId == id, ct) ||
            await db.CrmActivities.AnyAsync(a => a.ResponsavelId == id, ct) ||
            await db.CrmVeiculos.AnyAsync(v => v.VistoriadorId == id, ct) ||
            await db.CrmSalesGoals.AnyAsync(g => g.VendedorId == id, ct) ||
            await db.Users.AnyAsync(u => u.GestorComercialId == id, ct);

        if (possuiVinculos)
        {
            throw new CrmBusinessException(
                "Não é possível excluir sua conta porque existem leads, oportunidades, atividades, metas ou consultores vinculados a ela. Transfira esses vínculos para outro usuário antes de excluir.",
                "usuario_possui_vinculos");
        }

        IdentityResult resultado;
        try
        {
            resultado = await userManager.DeleteAsync(usuario);
        }
        catch (DbUpdateException)
        {
            throw new CrmBusinessException(
                "Não é possível excluir sua conta porque existem registros vinculados a ela.",
                "usuario_possui_vinculos");
        }

        if (!resultado.Succeeded)
        {
            throw new CrmBusinessException(IdentityErrors.Traduzir(resultado), "usuario_invalido");
        }

        await signInManager.SignOutAsync();
        return NoContent();
    }

    // --- auxiliares ---

    private async Task<ApplicationUser?> ObterUsuarioAtualAsync() =>
        User.Identity?.IsAuthenticated == true ? await userManager.GetUserAsync(User) : null;

    private async Task<ProfileDto> MontarPerfilAsync(ApplicationUser usuario)
    {
        var papeis = await userManager.GetRolesAsync(usuario);
        var podeExcluir = papeis.Contains(Roles.Admin) || papeis.Contains(Roles.GestorMaster);
        return new ProfileDto(
            usuario.Id, usuario.NomeCompleto, usuario.Email!, usuario.PhoneNumber, usuario.FotoUrl,
            usuario.TwoFactorEnabled, papeis.ToList(), podeExcluir);
    }

    private static string FormatarChave(string chave) =>
        string.Join(" ", Enumerable.Range(0, (chave.Length + 3) / 4).Select(i => chave.Substring(i * 4, Math.Min(4, chave.Length - i * 4))));

    private static string GerarUriQrCode(string email, string chave)
    {
        const string emissor = "CSS Brasil CRM";
        return $"otpauth://totp/{HttpUtility.UrlEncode(emissor)}:{HttpUtility.UrlEncode(email)}" +
               $"?secret={chave}&issuer={HttpUtility.UrlEncode(emissor)}&digits=6";
    }

    private async Task<SessionDto> MontarSessaoAsync(ApplicationUser usuario)
    {
        var papeis = await userManager.GetRolesAsync(usuario);
        var areaInicial = "/app/crm";

        var menu = new List<MenuItemDto>();
        menu.Add(new MenuItemDto("portal", "Portal do Consultor", "briefcase", "/app/portal"));
        menu.Add(new MenuItemDto("crm-overview", "Visão geral", "gauge", "/app/crm"));
        menu.Add(new MenuItemDto("crm-leads", "Leads", "users", "/app/crm/leads"));
        menu.Add(new MenuItemDto("crm-leads-kanban", "Quadro de leads", "layout-grid", "/app/crm/leads/kanban"));
        menu.Add(new MenuItemDto("crm-pipeline", "Pipeline", "kanban-square", "/app/crm/pipeline"));
        menu.Add(new MenuItemDto("crm-activities", "Atividades", "check-square", "/app/crm/activities"));
        menu.Add(new MenuItemDto("crm-agenda", "Agenda", "calendar-days", "/app/crm/agenda"));
        menu.Add(new MenuItemDto("crm-goals", "Metas", "target", "/app/crm/goals"));

        if (papeis.Contains(Roles.Admin) || papeis.Contains(Roles.GestorMaster) || papeis.Contains(Roles.GestorComercial))
        {
            menu.Add(new MenuItemDto("crm-management", "Gestão comercial", "users-round", "/app/crm/gestao"));
            menu.Add(new MenuItemDto("crm-consultores", "Consultores", "id-card", "/app/crm/consultores"));
            menu.Add(new MenuItemDto("crm-users", "Usuários", "user-cog", "/app/crm/usuarios"));
        }

        return new SessionDto(usuario.Id, usuario.Email!, usuario.NomeCompleto, usuario.FotoUrl, papeis.ToList(), areaInicial, menu);
    }

    private async Task ExcluirArquivoFotoAtualAsync(ApplicationUser usuario, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(usuario.FotoUrl)) return;

        await armazenamento.ExcluirSeExistirAsync(usuario.FotoUrl, ct);
    }
}
