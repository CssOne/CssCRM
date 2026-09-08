using CssVision.Web.Api.Contracts;
using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Authorization;
using CssVision.Web.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace CssVision.Web.Api.Controllers;

[ApiController]
[Route("api/account")]
public class AccountController(SignInManager<ApplicationUser> signInManager, UserManager<ApplicationUser> userManager) : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<SessionDto>> Login(LoginRequest request, CancellationToken ct)
    {
        var usuario = await userManager.FindByEmailAsync(request.Email.Trim());
        if (usuario is null || !usuario.Ativo)
        {
            return Unauthorized(new ApiError("E-mail ou senha inválidos.", "credenciais_invalidas"));
        }

        var resultado = await signInManager.PasswordSignInAsync(usuario, request.Senha, isPersistent: true, lockoutOnFailure: true);
        if (!resultado.Succeeded)
        {
            return Unauthorized(new ApiError("E-mail ou senha inválidos.", "credenciais_invalidas"));
        }

        return Ok(await MontarSessaoAsync(usuario));
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

    private async Task<SessionDto> MontarSessaoAsync(ApplicationUser usuario)
    {
        var papeis = await userManager.GetRolesAsync(usuario);
        var admin = papeis.Contains(Roles.Admin) || papeis.Contains(Roles.GestorMaster);
        var areaInicial = admin ? "/app" : "/app/crm";

        var menu = new List<MenuItemDto>();
        if (admin)
        {
            menu.Add(new MenuItemDto("admin", "Área administrativa", "layout-dashboard", "/app"));
        }

        menu.Add(new MenuItemDto("crm-overview", "Visão geral", "gauge", "/app/crm"));
        menu.Add(new MenuItemDto("crm-leads", "Leads", "users", "/app/crm/leads"));
        menu.Add(new MenuItemDto("crm-pipeline", "Pipeline", "kanban-square", "/app/crm/pipeline"));
        menu.Add(new MenuItemDto("crm-activities", "Atividades", "check-square", "/app/crm/activities"));
        menu.Add(new MenuItemDto("crm-agenda", "Agenda", "calendar-days", "/app/crm/agenda"));
        menu.Add(new MenuItemDto("crm-goals", "Metas", "target", "/app/crm/goals"));

        if (papeis.Contains(Roles.Admin) || papeis.Contains(Roles.GestorMaster) || papeis.Contains(Roles.GestorComercial))
        {
            menu.Add(new MenuItemDto("crm-management", "Gestão comercial", "users-round", "/app/crm/gestao"));
        }

        return new SessionDto(usuario.Id, usuario.Email!, usuario.NomeCompleto, papeis.ToList(), areaInicial, menu);
    }
}
