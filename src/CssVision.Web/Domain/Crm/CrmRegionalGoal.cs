namespace CssVision.Web.Domain.Crm;

/// <summary>
/// Meta comercial geral de uma regional (não de um consultor específico) — definida pelo
/// administrador e somada às metas individuais dos consultores dessa regional ao calcular a
/// "Meta do mês" exibida no dashboard.
/// </summary>
public class CrmRegionalGoal : CrmEntityBase
{
    public Guid RegionalId { get; set; }
    public CrmRegional Regional { get; set; } = null!;

    /// <summary>Primeiro dia do mês de referência (ex: 2026-09-01).</summary>
    public DateOnly MesReferencia { get; set; }

    public int MetaQuantidadeVendas { get; set; }
    public decimal? MetaValor { get; set; }
}
