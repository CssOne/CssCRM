using CssVision.Web.Domain.Crm;
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

    /// <summary>Regional à qual este usuário pertence (Comercial/GestorComercial). Nulo para Admin/GestorMaster (visão global).</summary>
    public Guid? RegionalId { get; set; }
    public CrmRegional? Regional { get; set; }

    /// <summary>Subgrupo do consultor dentro da regional (ex: "Externos", "Internos"). Apenas organizacional.</summary>
    public Guid? GrupoId { get; set; }
    public CrmGrupo? Grupo { get; set; }

    /// <summary>Teto de leads que a distribuição automática atribui a este vendedor por mês corrente. Nulo = sem limite.</summary>
    public int? LimiteMensalLeads { get; set; }

    /// <summary>
    /// Se preenchido, a distribuição automática só entrega a este consultor leads com um destes
    /// "O que?" (ProdutoInteresse), separados por vírgula — ex.: "AGV TRUCK". Nulo = recebe qualquer lead.
    /// </summary>
    public string? RecebeSomenteOQue { get; set; }

    /// <summary>Caminho da foto do vendedor (ex: /uploads/consultores/nome.jpeg), usada na página de obrigado pós-lead.</summary>
    public string? FotoUrl { get; set; }

    public DateTimeOffset CriadoEm { get; set; } = DateTimeOffset.UtcNow;
}
