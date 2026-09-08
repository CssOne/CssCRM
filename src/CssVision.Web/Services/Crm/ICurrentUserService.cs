using CssVision.Web.Authorization;

namespace CssVision.Web.Services.Crm;

/// <summary>
/// Identifica o usuário autenticado a partir dos claims do Identity — nunca a partir de um
/// identificador enviado pelo corpo/query da requisição. Toda regra de propriedade de registro
/// (um vendedor só vê o que é seu) deve se apoiar nesta abstração.
/// </summary>
public interface ICurrentUserService
{
    Guid UserId { get; }
    string? NomeCompleto { get; }
    bool IsAuthenticated { get; }
    bool IsInRole(string role);

    /// <summary>Admin ou GestorMaster: enxerga toda a base sem restrição de carteira/equipe.</summary>
    bool TemVisaoTotal { get; }

    /// <summary>Admin, GestorMaster ou GestorComercial: pode distribuir leads e configurar o funil.</summary>
    bool PodeGerirComercial { get; }

    /// <summary>Gestor comercial atual (quando o usuário tiver esse papel) — usado para filtrar a equipe.</summary>
    bool IsGestorComercial { get; }
}

public sealed class CurrentUserService(IHttpContextAccessor httpContextAccessor) : ICurrentUserService
{
    private System.Security.Claims.ClaimsPrincipal? Principal => httpContextAccessor.HttpContext?.User;

    public Guid UserId
    {
        get
        {
            var raw = Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            return Guid.TryParse(raw, out var id) ? id : Guid.Empty;
        }
    }

    public string? NomeCompleto => Principal?.FindFirst("nome_completo")?.Value ?? Principal?.Identity?.Name;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

    public bool IsInRole(string role) => Principal?.IsInRole(role) ?? false;

    public bool TemVisaoTotal => Roles.VisaoTotal.Any(IsInRole);

    public bool PodeGerirComercial => Roles.GestaoComercial.Any(IsInRole);

    public bool IsGestorComercial => IsInRole(Roles.GestorComercial);
}
