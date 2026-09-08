namespace CssVision.Web.Domain.Crm;

/// <summary>
/// Etapa configurável do funil de vendas. As oito etapas iniciais são semeadas pelo seeder,
/// mas Admin/GestorMaster/GestorComercial podem criar/reordenar novas etapas "Abertas".
/// Ganho e Perdido são etapas terminais fixas (Ordem mais alta) e não podem ser removidas.
/// </summary>
public class CrmPipelineStage : CrmEntityBase
{
    public string Nome { get; set; } = string.Empty;
    public int Ordem { get; set; }
    public TipoEtapaPipeline Tipo { get; set; } = TipoEtapaPipeline.Aberta;
    public bool Ativa { get; set; } = true;

    /// <summary>Cor usada nos cartões do kanban (token hex, ex: #2563eb).</summary>
    public string? Cor { get; set; }

    public ICollection<CrmOpportunity> Oportunidades { get; set; } = new List<CrmOpportunity>();
}
