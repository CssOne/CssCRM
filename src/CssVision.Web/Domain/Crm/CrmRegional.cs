using CssVision.Web.Domain.Identity;

namespace CssVision.Web.Domain.Crm;

/// <summary>
/// Regional comercial (ex: "Grande BH", "Interior de Minas"). Agrupa usuários (Comercial e
/// GestorComercial) para que um gestor de regional enxergue e administre os leads de toda a sua
/// regional, não apenas dos consultores que reportam diretamente a ele (ver EquipeComercialService).
/// </summary>
public class CrmRegional : CrmEntityBase
{
    public string Nome { get; set; } = string.Empty;
    public bool Ativa { get; set; } = true;

    public ICollection<ApplicationUser> Usuarios { get; set; } = new List<ApplicationUser>();
}
