namespace CssVision.Web.Domain.Crm;

public class CrmTag : CrmEntityBase
{
    public string Nome { get; set; } = string.Empty;
    public string? Cor { get; set; }

    public ICollection<CrmLeadTag> LeadTags { get; set; } = new List<CrmLeadTag>();
}

/// <summary>Tabela de junção Lead &lt;-&gt; Tag (muitos-para-muitos).</summary>
public class CrmLeadTag
{
    public Guid LeadId { get; set; }
    public CrmLead Lead { get; set; } = null!;

    public Guid TagId { get; set; }
    public CrmTag Tag { get; set; } = null!;
}
