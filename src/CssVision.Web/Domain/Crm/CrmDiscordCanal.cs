namespace CssVision.Web.Domain.Crm;

/// <summary>
/// Grupo do CRM espelhado no servidor do Discord: um canal de texto + um cargo que dá acesso a ele. É por esta tabela que o CRM sabe qual
/// canal e qual cargo do Discord correspondem a cada regional, grupo (e aos grupos fixos "geral" e "gestão").
/// </summary>
public class CrmDiscordCanal : CrmEntityBase
{
    /// <summary><c>geral</c>, <c>gestao</c>, <c>regional:{id}</c> ou <c>grupo:{id}</c>. Único.</summary>
    public string Chave { get; set; } = string.Empty;

    /// <summary>Nome mostrado no CRM (ex.: "MG132 · Growth Sales").</summary>
    public string Nome { get; set; } = string.Empty;

    public string DiscordCanalId { get; set; } = string.Empty;

    public string DiscordCargoId { get; set; } = string.Empty;

    /// <summary>Canal de voz do grupo (visível só a quem tem o cargo). Nulo até a próxima sincronização criá-lo.</summary>
    public string? DiscordVozId { get; set; }

    /// <summary>Falso quando a regional/grupo deixou de existir no CRM. O canal do Discord não é apagado, só deixa de receber membros.</summary>
    public bool Ativo { get; set; } = true;
}
