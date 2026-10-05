namespace CssVision.Web.Domain.Crm;

/// <summary>
/// Linha mensal da base "CONTROLE CSS BRASIL" do Notion (relatórios 2026): investimento em mídia,
/// leads e faturamento lançados à mão pelo marketing. O sistema não tem o gasto em anúncios, então
/// estes meses vêm do Notion (carga única na migration) e entram no Relatório comercial ao lado dos
/// números calculados pelo CRM. Não herda CrmEntityBase: é dado de referência, sem auditoria.
/// </summary>
public class CrmControleMarketingMes
{
    /// <summary>Primeiro dia do mês — chave primária.</summary>
    public DateOnly Mes { get; set; }

    public int VendasQuantidade { get; set; }
    public int LeadsGerados { get; set; }
    public decimal FacebookAds { get; set; }
    public decimal GoogleAds { get; set; }
    public decimal FerramentasMarketing { get; set; }
    public decimal Backlinks { get; set; }
    public decimal FaturamentoTotal { get; set; }
    public decimal MetaFaturamento { get; set; }
}
