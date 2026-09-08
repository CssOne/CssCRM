using CssVision.Web.Domain.Identity;

namespace CssVision.Web.Domain.Crm;

/// <summary>
/// Metadados de anexo vinculado a um lead (ex: proposta enviada). O armazenamento físico do
/// arquivo é um ponto de extensão futuro (disco/blob) — aqui persistimos apenas os metadados
/// e o caminho relativo, sem depender de infraestrutura externa nesta primeira versão.
/// </summary>
public class CrmAttachment : CrmEntityBase
{
    public Guid LeadId { get; set; }
    public CrmLead Lead { get; set; } = null!;

    public Guid? OpportunityId { get; set; }
    public CrmOpportunity? Opportunity { get; set; }

    public string NomeArquivo { get; set; } = string.Empty;
    public string CaminhoArmazenamento { get; set; } = string.Empty;
    public long TamanhoBytes { get; set; }
    public string? TipoConteudo { get; set; }

    public Guid EnviadoPorId { get; set; }
    public ApplicationUser EnviadoPor { get; set; } = null!;
}
