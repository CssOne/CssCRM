namespace CssVision.Web.Domain.Crm;

/// <summary>
/// Conta do Discord de um usuário do CRM (vínculo por OAuth2). É o que permite mandar os avisos do CRM como mensagem direta
/// no Discord do usuário — que chega no celular pelo app do Discord. Um usuário tem no máximo uma conta vinculada, e uma conta
/// do Discord só pode estar vinculada a um usuário.
/// </summary>
public class CrmDiscordVinculo : CrmEntityBase
{
    public Guid UsuarioId { get; set; }

    /// <summary>Id numérico da conta no Discord (snowflake), guardado como texto.</summary>
    public string DiscordUserId { get; set; } = string.Empty;

    /// <summary>Nome exibido no Discord no momento do vínculo (só para mostrar na tela).</summary>
    public string DiscordNome { get; set; } = string.Empty;

    /// <summary>O usuário quer receber os avisos do CRM no Discord (pode desligar sem desvincular).</summary>
    public bool AvisosAtivos { get; set; } = true;

    /// <summary>O bot conseguiu colocar a conta no servidor da empresa durante o vínculo.</summary>
    public bool NoServidor { get; set; }

    public DateTimeOffset VinculadoEm { get; set; }
}
