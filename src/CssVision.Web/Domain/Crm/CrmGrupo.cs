using CssVision.Web.Domain.Identity;

namespace CssVision.Web.Domain.Crm;

/// <summary>
/// Subdivisão de consultores dentro de uma regional (ex: "Consultores externos", "Internos").
/// Usada apenas para organização/visualização da equipe — não afeta autorização ou visibilidade de
/// leads (que continua sendo por regional, ver EquipeComercialService).
/// </summary>
public class CrmGrupo : CrmEntityBase
{
    public Guid RegionalId { get; set; }
    public CrmRegional Regional { get; set; } = null!;

    public string Nome { get; set; } = string.Empty;
    public bool Ativo { get; set; } = true;

    public ICollection<ApplicationUser> Usuarios { get; set; } = new List<ApplicationUser>();
}
