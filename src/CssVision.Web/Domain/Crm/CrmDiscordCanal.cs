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

    /// <summary>Nome mostrado no CRM escolhido por um administrador (renomear canal). Tem prioridade sobre <see cref="Nome"/>, que a sincronização reescreve.</summary>
    public string? NomePersonalizado { get; set; }

    /// <summary>
    /// Só nos canais extras (<c>extra:{id}</c>, criados por um administrador): a chave do grupo (<c>geral</c>, <c>gestao</c>, <c>regional:{id}</c> ou <c>grupo:{id}</c>)
    /// cujos membros veem o canal. Nos canais dos grupos fica vazio: a própria chave decide.
    /// </summary>
    public string? AcessoChave { get; set; }

    /// <summary>Nome que aparece no CRM.</summary>
    public string NomeExibido => string.IsNullOrWhiteSpace(NomePersonalizado) ? Nome : NomePersonalizado;

    /// <summary>
    /// Verdadeiro quando um administrador apagou o(s) canal(is) deste grupo no Discord: o grupo continua existindo (as pessoas mantêm o cargo), mas fica
    /// <b>sem canal</b> e a sincronização não o recria — até alguém "religar" o grupo. Só vale para canais de grupo; os extras apagados somem da tabela.
    /// </summary>
    public bool Desligado { get; set; }

    /// <summary>Falso quando a regional/grupo deixou de existir no CRM. O canal do Discord não é apagado, só deixa de receber membros.</summary>
    public bool Ativo { get; set; } = true;
}
