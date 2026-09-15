namespace CssVision.Web.Domain.Crm;

/// <summary>
/// Etapa configurável do quadro de leads (kanban). As oito etapas iniciais são semeadas pelo
/// seeder, mas Admin/GestorMaster/GestorComercial podem criar novas depois — igual ao
/// CrmPipelineStage, mas para o funil de LEADS, que é conceitualmente separado do funil de
/// OPORTUNIDADES (um lead pode ter zero, uma ou várias oportunidades; a etapa dele no quadro é
/// controlada manualmente, sem transição automática vinculada ao ciclo de vida da oportunidade).
/// </summary>
public class CrmLeadStage : CrmEntityBase
{
    public string Nome { get; set; } = string.Empty;
    public int Ordem { get; set; }
    public bool Ativa { get; set; } = true;

    /// <summary>Etapa terminal (ex: Venda concluída, Perdido) — excluída dos alertas de "lead parado".</summary>
    public bool Fechada { get; set; }

    /// <summary>Cor usada nos cartões do kanban (token hex, ex: #2563eb).</summary>
    public string? Cor { get; set; }

    public ICollection<CrmLead> Leads { get; set; } = new List<CrmLead>();
}
