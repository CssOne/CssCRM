namespace CssVision.Web.Domain.Crm;

/// <summary>
/// Navegador de um usuário inscrito para receber notificações push (Web Push) — o aviso de lead
/// novo aparece no sistema operacional mesmo com o CRM fechado. Um usuário pode ter vários
/// (computador do trabalho, notebook, celular).
/// </summary>
public class CrmPushInscricao : CrmEntityBase
{
    public Guid UsuarioId { get; set; }
    /// <summary>Endereço do serviço de push do navegador (único por navegador).</summary>
    public string Endpoint { get; set; } = string.Empty;
    public string P256dh { get; set; } = string.Empty;
    public string Auth { get; set; } = string.Empty;
    public string? Navegador { get; set; }
    public DateTimeOffset? UltimoEnvioEm { get; set; }
}

/// <summary>Parâmetro do sistema guardado no banco (ex.: chaves VAPID do Web Push, geradas no primeiro uso).</summary>
public class CrmParametro
{
    public string Chave { get; set; } = string.Empty;
    public string Valor { get; set; } = string.Empty;
}
