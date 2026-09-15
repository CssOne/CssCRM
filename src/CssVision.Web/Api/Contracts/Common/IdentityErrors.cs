using Microsoft.AspNetCore.Identity;

namespace CssVision.Web.Api.Contracts.Common;

/// <summary>Traduz mensagens de erro do ASP.NET Core Identity (em inglês) para o usuário final.</summary>
public static class IdentityErrors
{
    public static string Traduzir(IdentityResult resultado) =>
        string.Join(" ", resultado.Errors.Select(e => e.Description switch
        {
            var d when d.Contains("already taken", StringComparison.OrdinalIgnoreCase) => "Este e-mail já está em uso.",
            var d when d.Contains("Passwords must", StringComparison.OrdinalIgnoreCase) => "Senha muito fraca: use ao menos 8 caracteres.",
            var d when d.Contains("Incorrect password", StringComparison.OrdinalIgnoreCase) => "Senha atual incorreta.",
            var d when d.Contains("Invalid token", StringComparison.OrdinalIgnoreCase) => "Link inválido ou expirado. Solicite a redefinição novamente.",
            _ => e.Description
        }));
}
