namespace CssVision.Web.Domain.Crm;

/// <summary>
/// Reação de uma pessoa do CRM a uma mensagem do chat. No Discord a reação sai em nome do bot (uma só por emoji); esta tabela guarda <b>quem</b>
/// reagiu pelo CRM, para contar e para cada pessoa poder tirar a sua. A reação do bot fica na mensagem enquanto houver ao menos uma pessoa aqui.
/// </summary>
public class CrmDiscordReacao : CrmEntityBase
{
    /// <summary>Id da mensagem no Discord.</summary>
    public string MensagemId { get; set; } = string.Empty;

    /// <summary>Canal ou tópico onde a mensagem está (é onde se lê e se reage).</summary>
    public string LeituraId { get; set; } = string.Empty;

    public Guid UsuarioId { get; set; }

    /// <summary>O emoji como o Discord o identifica: o caractere ou <c>nome:id</c>.</summary>
    public string Emoji { get; set; } = string.Empty;
}
