namespace CssVision.Web.Domain.Crm;

/// <summary>
/// Conversa 1:1 entre duas pessoas do CRM: uma thread privada do Discord (dentro do canal "Conversas diretas") onde só as duas entram.
/// O par é guardado em ordem fixa (<see cref="UsuarioAId"/> menor que <see cref="UsuarioBId"/>) para existir uma só conversa por par.
/// </summary>
public class CrmDiscordConversa : CrmEntityBase
{
    public Guid UsuarioAId { get; set; }

    public Guid UsuarioBId { get; set; }

    public string DiscordThreadId { get; set; } = string.Empty;

    /// <summary>Canal de voz privado das duas pessoas, criado na primeira chamada.</summary>
    public string? DiscordVozId { get; set; }
}
