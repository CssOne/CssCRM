namespace CssVision.Web.Domain.Crm;

public enum StatusAvisoConsultor
{
    Aberto = 1,
    Resolvido = 2,
}

/// <summary>
/// Aviso enviado a um consultor (hoje: pagamento em aberto). Aparece para ele como notificação (push e aviso dentro do CRM) e no card
/// "Avisos importantes" do Portal do Consultor até alguém da gestão ou do financeiro marcá-lo como resolvido.
/// </summary>
public class CrmAvisoConsultor : CrmEntityBase
{
    public Guid ConsultorId { get; set; }

    public string Titulo { get; set; } = "Pagamento em aberto";
    public string Mensagem { get; set; } = string.Empty;

    /// <summary>Valor em aberto, quando o aviso informa (opcional).</summary>
    public decimal? Valor { get; set; }

    /// <summary>Cliente, placa ou contrato a que o aviso se refere (opcional).</summary>
    public string? Referencia { get; set; }

    public Guid EnviadoPorId { get; set; }
    public StatusAvisoConsultor Status { get; set; } = StatusAvisoConsultor.Aberto;

    /// <summary>Quando o consultor marcou "ciente".</summary>
    public DateTimeOffset? LidoEm { get; set; }

    public DateTimeOffset? ResolvidoEm { get; set; }
    public Guid? ResolvidoPorId { get; set; }
}
