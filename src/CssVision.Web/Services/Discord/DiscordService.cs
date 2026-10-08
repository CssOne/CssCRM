using System.Security.Cryptography;
using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace CssVision.Web.Services.Discord;

public interface IDiscordService
{
    Task<DiscordStatusDto> ObterStatusAsync(Guid usuarioId, CancellationToken ct);

    /// <returns>O endereço do Discord para onde o usuário é levado para autorizar o vínculo.</returns>
    Task<string> IniciarVinculoAsync(Guid usuarioId, string redirectUri, CancellationToken ct);

    /// <summary>Volta do Discord: confere o <paramref name="state"/>, lê a conta autorizada e grava o vínculo.</summary>
    Task<DiscordStatusDto> ConcluirVinculoAsync(string code, string state, string redirectUri, CancellationToken ct);

    Task DesvincularAsync(Guid usuarioId, CancellationToken ct);

    Task DefinirAvisosAsync(Guid usuarioId, bool ativos, CancellationToken ct);

    /// <summary>Manda o aviso como mensagem direta no Discord do usuário, se ele vinculou e não desligou os avisos.</summary>
    /// <returns><c>true</c> se a mensagem foi entregue ao Discord.</returns>
    Task<bool> EnviarAvisoAsync(Guid usuarioId, string titulo, string corpo, string? urlRelativa, CancellationToken ct);
}

/// <summary>
/// Vínculo entre a conta do CRM e a conta do Discord (OAuth2, scopes <c>identify</c> e <c>guilds.join</c>) e envio dos avisos como
/// mensagem direta. O token OAuth2 do usuário só é usado na hora do vínculo e <b>não é guardado</b>. O <c>state</c> amarra a volta do
/// Discord ao usuário que iniciou: é aleatório, de uso único e vale 10 minutos.
/// </summary>
public sealed class DiscordService(
    ApplicationDbContext db,
    IDiscordApi api,
    IMemoryCache cache,
    IOptions<DiscordOptions> options,
    ILogger<DiscordService> logger) : IDiscordService
{
    private static readonly TimeSpan ValidadeDoState = TimeSpan.FromMinutes(10);
    private DiscordOptions Opcoes => options.Value;

    private static string ChaveDoState(string state) => $"discord:state:{state}";

    public async Task<DiscordStatusDto> ObterStatusAsync(Guid usuarioId, CancellationToken ct)
    {
        var vinculo = await db.CrmDiscordVinculos.AsNoTracking().FirstOrDefaultAsync(v => v.UsuarioId == usuarioId, ct);
        return Status(vinculo);
    }

    private DiscordStatusDto Status(CrmDiscordVinculo? vinculo) => new(
        Opcoes.Configurado, vinculo is not null, vinculo?.DiscordNome, vinculo?.AvisosAtivos ?? false, vinculo?.NoServidor ?? false, vinculo?.VinculadoEm);

    public Task<string> IniciarVinculoAsync(Guid usuarioId, string redirectUri, CancellationToken ct)
    {
        ExigirConfigurado();

        var state = Base64Url(RandomNumberGenerator.GetBytes(24));
        cache.Set(ChaveDoState(state), usuarioId, ValidadeDoState);

        var url = "https://discord.com/oauth2/authorize"
            + $"?client_id={Uri.EscapeDataString(Opcoes.ClientId)}"
            + "&response_type=code"
            + $"&redirect_uri={Uri.EscapeDataString(redirectUri)}"
            + $"&scope={Uri.EscapeDataString("identify guilds.join")}"
            + $"&state={Uri.EscapeDataString(state)}"
            + "&prompt=consent";
        return Task.FromResult(url);
    }

    public async Task<DiscordStatusDto> ConcluirVinculoAsync(string code, string state, string redirectUri, CancellationToken ct)
    {
        ExigirConfigurado();

        // Uso único: tira do cache antes de qualquer outra coisa, para o mesmo link não valer duas vezes.
        var chave = ChaveDoState(state);
        if (!cache.TryGetValue(chave, out Guid usuarioId))
        {
            throw new CrmBusinessException("O pedido de vínculo expirou ou é inválido. Comece de novo pelo botão \"Vincular Discord\".", "discord_state_invalido");
        }

        cache.Remove(chave);

        DiscordUsuario conta;
        string token;
        try
        {
            token = await api.TrocarCodigoAsync(code, redirectUri, ct);
            conta = await api.ObterUsuarioAsync(token, ct);
        }
        catch (DiscordApiException ex)
        {
            throw new CrmBusinessException($"Não foi possível vincular: {ex.Message} Tente de novo em instantes.", "discord_indisponivel");
        }
        catch (HttpRequestException)
        {
            throw new CrmBusinessException("Não foi possível falar com o Discord agora. Tente de novo em instantes.", "discord_indisponivel");
        }

        if (await db.CrmDiscordVinculos.AnyAsync(v => v.DiscordUserId == conta.Id && v.UsuarioId != usuarioId, ct))
        {
            throw new CrmBusinessException("Esta conta do Discord já está vinculada a outro usuário do CRM.", "discord_conta_em_uso");
        }

        var vinculo = await db.CrmDiscordVinculos.FirstOrDefaultAsync(v => v.UsuarioId == usuarioId, ct);
        var primeiraVez = vinculo is null;
        if (vinculo is null)
        {
            vinculo = new CrmDiscordVinculo { UsuarioId = usuarioId, AvisosAtivos = true };
            db.CrmDiscordVinculos.Add(vinculo);
        }

        vinculo.DiscordUserId = conta.Id;
        vinculo.DiscordNome = conta.Nome.Length > 100 ? conta.Nome[..100] : conta.Nome;
        vinculo.VinculadoEm = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        // Colocar no servidor da empresa não pode derrubar o vínculo: se falhar, o usuário entra por convite.
        vinculo.NoServidor = await api.AdicionarAoServidorAsync(conta.Id, token, ct);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Usuário {UsuarioId} vinculou a conta do Discord {DiscordUserId} (no servidor: {NoServidor}).", usuarioId, conta.Id, vinculo.NoServidor);

        // Boas-vindas por mensagem direta, só na primeira vez (e sem que uma falha dela estrague o vínculo, que já está feito e salvo).
        if (primeiraVez) await EnviarBoasVindasAsync(usuarioId, ct);
        return Status(vinculo);
    }

    private async Task EnviarBoasVindasAsync(Guid usuarioId, CancellationToken ct)
    {
        try
        {
            await EnviarAvisoAsync(usuarioId, "Conta do Discord vinculada! 🎉",
                "A partir de agora os avisos do CRM chegam por aqui: lead novo, pagamentos em aberto e alertas — inclusive no celular, com o app do Discord instalado e as notificações ligadas.\n\n"
                + "Você também já pode conversar com a equipe pelo menu **Chat** do CRM, com o seu nome e a sua foto.",
                "app/chat", ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogInformation(ex, "Não foi possível enviar a mensagem de boas-vindas pelo Discord ao usuário {UsuarioId}.", usuarioId);
        }
    }

    public async Task DesvincularAsync(Guid usuarioId, CancellationToken ct)
    {
        var vinculo = await db.CrmDiscordVinculos.FirstOrDefaultAsync(v => v.UsuarioId == usuarioId, ct);
        if (vinculo is null) return;

        db.CrmDiscordVinculos.Remove(vinculo);
        await db.SaveChangesAsync(ct);
    }

    public async Task DefinirAvisosAsync(Guid usuarioId, bool ativos, CancellationToken ct)
    {
        var vinculo = await db.CrmDiscordVinculos.FirstOrDefaultAsync(v => v.UsuarioId == usuarioId, ct)
            ?? throw new CrmBusinessException("Vincule sua conta do Discord primeiro.", "discord_sem_vinculo");
        vinculo.AvisosAtivos = ativos;
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> EnviarAvisoAsync(Guid usuarioId, string titulo, string corpo, string? urlRelativa, CancellationToken ct)
    {
        if (!Opcoes.Configurado) return false;

        var contaDiscord = await db.CrmDiscordVinculos.AsNoTracking()
            .Where(v => v.UsuarioId == usuarioId && v.AvisosAtivos)
            .Select(v => v.DiscordUserId)
            .FirstOrDefaultAsync(ct);
        if (contaDiscord is null) return false;

        var link = !string.IsNullOrWhiteSpace(Opcoes.UrlPublica) && !string.IsNullOrWhiteSpace(urlRelativa)
            ? Opcoes.UrlPublica.TrimEnd('/') + "/" + urlRelativa.TrimStart('/')
            : null;

        return await api.EnviarMensagemDiretaAsync(contaDiscord, new DiscordAviso(titulo, corpo, link), ct) == ResultadoEnvioDiscord.Enviada;
    }

    private void ExigirConfigurado()
    {
        if (!Opcoes.Configurado)
        {
            throw new CrmBusinessException("A integração com o Discord ainda não foi configurada no servidor. Fale com o administrador.", "discord_nao_configurado");
        }
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
