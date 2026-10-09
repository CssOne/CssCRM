using CssVision.Web.Services.Crm;

namespace CssVision.Web.Services.Discord;

/// <summary>Avisa uma pessoa de que alguém a "@marcou" no chat do CRM.</summary>
public interface IAvisoDeMencao
{
    Task AvisarAsync(Guid usuarioId, string nomeDeQuemMarcou, CancellationToken ct);
}

/// <summary>
/// Notificação do navegador/celular (push do CRM). Usa o <see cref="PushService"/> direto, e não o que também manda mensagem direta no Discord:
/// a menção já toca no Discord por conta própria, e duas mensagens iguais seriam ruído.
/// </summary>
public sealed class AvisoDeMencaoPush(PushService push) : IAvisoDeMencao
{
    public async Task AvisarAsync(Guid usuarioId, string nomeDeQuemMarcou, CancellationToken ct) =>
        await push.EnviarAsync(usuarioId, new PushMensagem($"{nomeDeQuemMarcou} marcou você no chat", "Toque para abrir a conversa.", "/app/chat", $"chat-mencao-{usuarioId}"), ct);
}
