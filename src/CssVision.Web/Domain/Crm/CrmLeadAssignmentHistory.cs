using CssVision.Web.Domain.Identity;

namespace CssVision.Web.Domain.Crm;

/// <summary>Histórico imutável de atribuições/redistribuições de leads.</summary>
public class CrmLeadAssignmentHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid LeadId { get; set; }
    public CrmLead Lead { get; set; } = null!;

    public Guid? ResponsavelAnteriorId { get; set; }
    public ApplicationUser? ResponsavelAnterior { get; set; }

    public Guid ResponsavelNovoId { get; set; }
    public ApplicationUser ResponsavelNovo { get; set; } = null!;

    public Guid AlteradoPorId { get; set; }
    public ApplicationUser AlteradoPor { get; set; } = null!;

    public DateTimeOffset AlteradoEm { get; set; } = DateTimeOffset.UtcNow;

    public string? Motivo { get; set; }
}
