using CssVision.Web.Domain.Identity;

namespace CssVision.Web.Domain.Crm;

/// <summary>Meta comercial mensal por vendedor.</summary>
public class CrmSalesGoal : CrmEntityBase
{
    public Guid VendedorId { get; set; }
    public ApplicationUser Vendedor { get; set; } = null!;

    /// <summary>Primeiro dia do mês de referência (ex: 2026-09-01).</summary>
    public DateOnly MesReferencia { get; set; }

    public decimal MetaValor { get; set; }
    public int? MetaQuantidadeVendas { get; set; }
}
