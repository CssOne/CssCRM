using CssVision.Web.Authorization;
using CssVision.Web.Domain.Identity;
using Microsoft.AspNetCore.Identity;

namespace CssVision.Web.Data.Seed;

/// <summary>
/// Cria os papéis do sistema e um usuário administrador inicial para ambiente de
/// desenvolvimento. Em produção, a criação de usuários deve ocorrer pelos fluxos normais
/// de administração (compartilhados com a AplicacaoDashboard quando os projetos forem integrados).
/// </summary>
public static class IdentitySeeder
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        var roleManager = services.GetRequiredService<RoleManager<ApplicationRole>>();
        foreach (var role in Roles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new ApplicationRole(role));
            }
        }

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
