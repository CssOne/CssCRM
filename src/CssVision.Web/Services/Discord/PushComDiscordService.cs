using CssVision.Web.Services.Crm;

namespace CssVision.Web.Services.Discord;

/// <summary>
/// Decorador do <see cref="IPushService"/>: tudo que o CRM já avisa por push (lead novo, aviso de pagamento, alerta de distribuição,
/// Suporte) passa a chegar também no Discord do usuário — e, pelo app do Discord, no celular. Só entrega no Discord quem vinculou a
/// conta e não desligou os avisos; para os demais nada muda. Falha no Discord nunca atrapalha o push.
/// </summary>
public sealed class PushComDiscordService(PushService push, IDiscordService discord, ILogger<PushComDiscordService> logger) : IPushService
{
    public Task<string> ChavePublicaAsync(CancellationToken ct) => push.ChavePublicaAsync(ct);

    public Task InscreverAsync(Guid usuarioId, PushInscricaoRequest request, CancellationToken ct) => push.InscreverAsync(usuarioId, request, ct);

    public Task RemoverAsync(Guid usuarioId, string endpoint, CancellationToken ct) => push.RemoverAsync(usuarioId, endpoint, ct);

    /// <returns>Quantos destinos receberam: navegadores do push + 1 se o aviso foi para o Discord.</returns>
    public async Task<int> EnviarAsync(Guid usuarioId, PushMensagem mensagem, CancellationToken ct)
    {
        var entregues = await push.EnviarAsync(usuarioId, mensagem, ct);

        try
        {
            if (await discord.EnviarAvisoAsync(usuarioId, mensagem.Titulo, mensagem.Corpo, mensagem.Url, ct)) entregues++;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falha ao avisar pelo Discord o usuário {UsuarioId}; o push seguiu normalmente.", usuarioId);
        }

        return entregues;
    }
}
