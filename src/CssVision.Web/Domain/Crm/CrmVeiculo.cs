using CssVision.Web.Domain.Identity;

namespace CssVision.Web.Domain.Crm;

/// <summary>
/// Dados do veículo/rastreador associados a uma oportunidade de proteção veicular.
/// Relação 1:1 com CrmOpportunity — não é arquivável isoladamente, segue o ciclo de vida da oportunidade.
/// </summary>
public class CrmVeiculo : CrmEntityBase
{
    public Guid OpportunityId { get; set; }
    public CrmOpportunity Opportunity { get; set; } = null!;

    public string? Descricao { get; set; }
    public string? Placa { get; set; }

    /// <summary>Valor de referência FIPE do veículo. Precisão: numeric(14,2).</summary>
    public decimal? Fipe { get; set; }

    public string? Rastreador { get; set; }

    public Guid? VistoriadorId { get; set; }
    public ApplicationUser? Vistoriador { get; set; }

    /// <summary>Data/hora em que o veículo chegou para vistoria/instalação.</summary>
    public DateTimeOffset? DataChegada { get; set; }
}
