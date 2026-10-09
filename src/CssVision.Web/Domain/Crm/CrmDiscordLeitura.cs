namespace CssVision.Web.Domain.Crm;

/// <summary>
/// Até onde cada pessoa já leu cada conversa do chat (grupo ou 1:1): o id da última mensagem lida. As mensagens com id maior são as não lidas.
/// Os ids do Discord são "snowflakes" (crescem com o tempo), então comparar os ids equivale a comparar as datas.
/// </summary>
public class CrmDiscordLeitura : CrmEntityBase
{
    public Guid UsuarioId { get; set; }

    /// <summary>Chave da conversa no chat: <c>geral</c>, <c>regional:{id}</c>, <c>grupo:{id}</c> ou <c>dm:{id}</c>.</summary>
    public string Chave { get; set; } = string.Empty;

    public string UltimaLidaId { get; set; } = string.Empty;
}
