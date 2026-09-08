using CssVision.Web.Authorization;
using CssVision.Web.Domain.Identity;
using Microsoft.AspNetCore.Identity;

namespace CssVision.Web.Data.Seed;

/// <summary>
/// SeedRolesAsync roda em qualquer ambiente (papéis são pré-requisito de qualquer usuário,
/// inclusive os consultores reais do ConsultorSeeder). SeedDemoUsersAsync só deve rodar em
/// desenvolvimento — cria contas fictícias com senha conhecida, nunca deve existir em produção.
/// </summary>
public static class IdentitySeeder
{
    /// <summary>Papéis do sistema — precisa existir em qualquer ambiente antes de qualquer usuário poder ser criado.</summary>
    public static async Task SeedRolesAsync(IServiceProvider services)
    {
        var roleManager = services.GetRequiredService<RoleManager<ApplicationRole>>();
        foreach (var role in Roles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new ApplicationRole(role));
            }
        }
    }

    /// <summary>Usuários fictícios de demonstração — nunca deve rodar em produção.</summary>
    public static async Task SeedDemoUsersAsync(IServiceProvider services)
    {
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();

        var admin = await CriarUsuarioSeNaoExistirAsync(userManager, "admin@cssvision.local", "Administrador Geral", Roles.Admin);
        var gestor = await CriarUsuarioSeNaoExistirAsync(userManager, "gestor.comercial@cssvision.local", "Gestora Comercial Demo", Roles.GestorComercial);
        var vendedor1 = await CriarUsuarioSeNaoExistirAsync(userManager, "vendedor1@cssvision.local", "Vendedor Um", Roles.Comercial, gestor.Id);
        await CriarUsuarioSeNaoExistirAsync(userManager, "vendedor2@cssvision.local", "Vendedora Dois", Roles.Comercial, gestor.Id);
    }

    private static async Task<ApplicationUser> CriarUsuarioSeNaoExistirAsync(
        UserManager<ApplicationUser> userManager, string email, string nomeCompleto, string papel, Guid? gestorId = null)
    {
        var usuario = await userManager.FindByEmailAsync(email);
        if (usuario is not null) return usuario;

        usuario = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            NomeCompleto = nomeCompleto,
            GestorComercialId = gestorId
        };

        var resultado = await userManager.CreateAsync(usuario, "Senha@123");
        if (!resultado.Succeeded)
        {
            throw new InvalidOperationException(string.Join("; ", resultado.Errors.Select(e => e.Description)));
        }

        await userManager.AddToRoleAsync(usuario, papel);
        return usuario;
    }
}
