namespace CssVision.Web.Services.Discord;

/// <summary>
/// Configuração da integração com o Discord (env: Discord__BotToken, Discord__ClientId, Discord__ClientSecret, Discord__GuildId,
/// Discord__UrlPublica). Sem as quatro primeiras a integração fica desligada e o CRM funciona como antes.
/// </summary>
public class DiscordOptions
{
    public const string SectionName = "Discord";

    /// <summary>Token do bot (Portal do Desenvolvedor do Discord → Bot). Segredo: vem do Secrets Manager.</summary>
    public string BotToken { get; set; } = string.Empty;

    /// <summary>Id da aplicação (OAuth2 → Client ID).</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Segredo do OAuth2 (OAuth2 → Client Secret). Segredo: vem do Secrets Manager.</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Id do servidor (guild) da empresa, onde o bot coloca os usuários que vinculam a conta.</summary>
    public string GuildId { get; set; } = string.Empty;

    /// <summary>Endereço público do CRM (ex.: https://cssbrasil.duckdns.org): vira o link dos avisos e o endereço de retorno do OAuth2.</summary>
    public string UrlPublica { get; set; } = string.Empty;

    public bool Configurado =>
        !string.IsNullOrWhiteSpace(BotToken) && !string.IsNullOrWhiteSpace(ClientId)
        && !string.IsNullOrWhiteSpace(ClientSecret) && !string.IsNullOrWhiteSpace(GuildId);
}
