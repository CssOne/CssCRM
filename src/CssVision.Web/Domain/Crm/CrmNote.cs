using CssVision.Web.Domain.Identity;

namespace CssVision.Web.Domain.Crm;

public class CrmNote : CrmEntityBase
{
    public Guid LeadId { get; set; }
    public CrmLead Lead { get; set; } = null!;

    public Guid AutorId { get; set; }
    public ApplicationUser Autor { get; set; } = null!;

    public string Texto { get; set; } = string.Empty;
}
