namespace CssVision.Web.Domain.Crm;

/// <summary>
/// Acompanhamento da espera de um lead do tráfego pago que está com um consultor mas ainda em "Sem etapa" (ninguém começou a atender).
/// Guarda os avisos já enviados — para não repetir — e quando o consultor pegou o lead, o que alimenta o aviso à gestão de quem demora com frequência
/// (ver <c>EsperaTrafegoService</c>). Um registro por lead; se o lead passa para outro consultor, o registro recomeça para o novo.
/// </summary>
public class CrmEsperaLead : CrmEntityBase
{
    public Guid LeadId { get; set; }
    public CrmLead Lead { get; set; } = null!;

    public Guid ConsultorId { get; set; }

    /// <summary>Quando o lead passou para o consultor (ou chegou, se não houve atribuição): início da espera, o mesmo do cronômetro do cartão.</summary>
    public DateTimeOffset EsperandoDesde { get; set; }

    /// <summary>Primeiro aviso ao consultor ("pegue o lead").</summary>
    public DateTimeOffset? AvisoConsultorEm { get; set; }

    /// <summary>Segundo aviso, mais forte: o lead já está frio. É o que conta como "demorou" para o aviso à gestão.</summary>
    public DateTimeOffset? AvisoUrgenteEm { get; set; }

    /// <summary>Quando o lead saiu de "Sem etapa" (ou foi arquivado/passado a outro consultor), detectado pelo vigia — precisão de cerca de um minuto.</summary>
    public DateTimeOffset? AtendidoEm { get; set; }
}
