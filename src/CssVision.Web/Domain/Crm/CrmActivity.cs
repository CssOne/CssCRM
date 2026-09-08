using CssVision.Web.Domain.Identity;

namespace CssVision.Web.Domain.Crm;

public class CrmActivity : CrmArchivableEntity
{
    public Guid LeadId { get; set; }
    public CrmLead Lead { get; set; } = null!;

    public Guid? OpportunityId { get; set; }
    public CrmOpportunity? Opportunity { get; set; }

    public Guid ResponsavelId { get; set; }
    public ApplicationUser Responsavel { get; set; } = null!;

    public TipoAtividade Tipo { get; set; }
    public string Assunto { get; set; } = string.Empty;
    public string? Descricao { get; set; }

    public DateTimeOffset DataHoraPrevista { get; set; }
    public DateTimeOffset? DataHoraConclusao { get; set; }

    public string? Resultado { get; set; }
    public StatusAtividade Status { get; set; } = StatusAtividade.Pendente;

    /// <summary>Minutos antes da data prevista para lembrete. Null = sem lembrete. Sem envio real (ponto de extensão futuro).</summary>
    public int? LembreteMinutosAntes { get; set; }
}
