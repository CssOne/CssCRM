using Microsoft.AspNetCore.Identity;

namespace CssVision.Web.Domain.Identity;

/// <summary>
/// Usuário compartilhado entre a área administrativa e o CRM.
/// Estende o IdentityUser padrão com dados usados pela hierarquia comercial.
/// </summary>
public class ApplicationUser : IdentityUser<Guid>
{
    public string NomeCompleto { get; set; } = string.Empty;

    /// <summary>Gestor comercial responsável por este usuário, quando ele for do time de vendas (Comercial).</summary>
    public Guid? GestorComercialId { get; set; }
    public ApplicationUser? GestorComercial { get; set; }

    public bool Ativo { get; set; } = true;

    /// <summary>Teto de leads que a distribuição automática atribui a este vendedor por mês corrente. Nulo = sem limite.</summary>
    public int? LimiteMensalLeads { get; set; }

    /// <summary>Caminho da foto do vendedor (ex: /uploads/consultores/nome.jpeg), usada na página de obrigado pós-lead.</summary>
    public string? FotoUrl { get; set; }

    public DateTimeOffset CriadoEm { get; set; } = DateTimeOffset.UtcNow;
}
