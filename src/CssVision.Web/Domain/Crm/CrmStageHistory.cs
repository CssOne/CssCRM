using CssVision.Web.Domain.Identity;

namespace CssVision.Web.Domain.Crm;

/// <summary>Histórico imutável de mudanças de etapa de uma oportunidade (auditoria do pipeline).</summary>
public class CrmStageHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid OpportunityId { get; set; }
    public CrmOpportunity Opportunity { get; set; } = null!;

    public Guid? EtapaAnteriorId { get; set; }
    public CrmPipelineStage? EtapaAnterior { get; set; }

    public Guid EtapaNovaId { get; set; }
    public CrmPipelineStage EtapaNova { get; set; } = null!;

    public Guid UsuarioId { get; set; }
    public ApplicationUser Usuario { get; set; } = null!;

    public DateTimeOffset AlteradoEm { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Obrigatório quando a nova etapa é do tipo "Perdido".</summary>
    public string? MotivoPerdaDescricao { get; set; }
}
