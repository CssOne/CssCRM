namespace CssVision.Web.Domain.Crm;

public class CrmLossReason : CrmEntityBase
{
    public string Descricao { get; set; } = string.Empty;
    public bool Ativo { get; set; } = true;

    public ICollection<CrmOpportunity> Oportunidades { get; set; } = new List<CrmOpportunity>();
}
