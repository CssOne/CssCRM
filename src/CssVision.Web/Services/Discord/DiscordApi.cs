using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace CssVision.Web.Services.Discord;

/// <summary>Conta do Discord devolvida pelo OAuth2 (<c>Nome</c> é o nome de exibição, ou o usuário se não houver).</summary>
public record DiscordUsuario(string Id, string Nome);

/// <summary>Aviso que vira uma mensagem direta (embed) no Discord.</summary>
public record DiscordAviso(string Titulo, string Corpo, string? Url);

public enum ResultadoEnvioDiscord
{
    Enviada,

    /// <summary>O usuário bloqueou mensagens diretas de quem está no servidor (configuração de privacidade dele).</summary>
    DmFechada,

    Erro,
}

/// <summary>O Discord recusou ou não respondeu a uma chamada que o fluxo precisa (troca do código, leitura do usuário).</summary>
public class DiscordApiException(string mensagem) : Exception(mensagem);

/// <summary>Chamadas à API REST do Discord que o CRM usa. Interface separada para os testes não precisarem do Discord de verdade.</summary>
public interface IDiscordApi
{
    /// <returns>O token de acesso do usuário (só vale para ler a conta dele e colocá-lo no servidor; não é guardado).</returns>
    Task<string> TrocarCodigoAsync(string code, string redirectUri, CancellationToken ct);

    Task<DiscordUsuario> ObterUsuarioAsync(string accessToken, CancellationToken ct);

    /// <returns><c>true</c> se a conta entrou (ou já estava) no servidor da empresa.</returns>
    Task<bool> AdicionarAoServidorAsync(string discordUserId, string accessToken, CancellationToken ct);

    Task<ResultadoEnvioDiscord> EnviarMensagemDiretaAsync(string discordUserId, DiscordAviso aviso, CancellationToken ct);
}

/// <summary>
/// Cliente HTTP da API v10 do Discord. O bot fala como "Bot {token}"; a conta do usuário só é lida com o token OAuth2 dele, na hora
/// do vínculo. Respeita o limite de requisições do Discord (429): espera o tempo pedido e tenta mais uma vez. O token do bot e o
/// segredo do OAuth2 nunca vão para o log.
/// </summary>
public sealed class DiscordApi(HttpClient http, IOptions<DiscordOptions> options, ILogger<DiscordApi> logger) : IDiscordApi
{
    public const string UrlBase = "https://discord.com/api/v10/";
    private const int CodigoMensagensDiretasBloqueadas = 50007;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private DiscordOptions Opcoes => options.Value;

    public async Task<string> TrocarCodigoAsync(string code, string redirectUri, CancellationToken ct)
    {
        using var resposta = await EnviarAsync(() => new HttpRequestMessage(HttpMethod.Post, "oauth2/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = Opcoes.ClientId,
                ["client_secret"] = Opcoes.ClientSecret,
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = redirectUri,
            }),
        }, ct);

        if (!resposta.IsSuccessStatusCode)
        {
            logger.LogWarning("Discord recusou o código de autorização do OAuth2 (HTTP {Status}).", (int)resposta.StatusCode);
            throw new DiscordApiException("O Discord recusou a autorização.");
        }

        using var documento = await LerAsync(resposta, ct);
        return documento.RootElement.TryGetProperty("access_token", out var token) && token.GetString() is { Length: > 0 } valor
            ? valor
            : throw new DiscordApiException("O Discord não devolveu o token de acesso.");
    }

    public async Task<DiscordUsuario> ObterUsuarioAsync(string accessToken, CancellationToken ct)
    {
        using var resposta = await EnviarAsync(() =>
        {
            var requisicao = new HttpRequestMessage(HttpMethod.Get, "users/@me");
            requisicao.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            return requisicao;
        }, ct);

        if (!resposta.IsSuccessStatusCode)
        {
            logger.LogWarning("Discord recusou a leitura da conta do usuário (HTTP {Status}).", (int)resposta.StatusCode);
            throw new DiscordApiException("Não foi possível ler a conta do Discord.");
        }

        using var documento = await LerAsync(resposta, ct);
        var raiz = documento.RootElement;
        var id = raiz.TryGetProperty("id", out var i) ? i.GetString() : null;
        if (string.IsNullOrWhiteSpace(id)) throw new DiscordApiException("O Discord não devolveu o id da conta.");

        var global = raiz.TryGetProperty("global_name", out var g) && g.ValueKind == JsonValueKind.String ? g.GetString() : null;
        var usuario = raiz.TryGetProperty("username", out var u) ? u.GetString() : null;
        return new DiscordUsuario(id, !string.IsNullOrWhiteSpace(global) ? global : usuario ?? id);
    }

    public async Task<bool> AdicionarAoServidorAsync(string discordUserId, string accessToken, CancellationToken ct)
    {
        try
        {
            using var resposta = await EnviarAsync(() =>
            {
                var requisicao = new HttpRequestMessage(HttpMethod.Put, $"guilds/{Opcoes.GuildId}/members/{discordUserId}")
                {
                    Content = Corpo(new { access_token = accessToken }),
                };
                AutenticarComoBot(requisicao);
                return requisicao;
            }, ct);

            // 201 = entrou agora; 204 = já estava no servidor.
            if (resposta.StatusCode is HttpStatusCode.Created or HttpStatusCode.NoContent) return true;

            logger.LogWarning("Discord não colocou a conta {DiscordUserId} no servidor (HTTP {Status}). O bot precisa estar no servidor com a permissão \"Criar convite\".",
                discordUserId, (int)resposta.StatusCode);
            return false;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Falha ao colocar a conta {DiscordUserId} no servidor do Discord.", discordUserId);
            return false;
        }
    }

    public async Task<ResultadoEnvioDiscord> EnviarMensagemDiretaAsync(string discordUserId, DiscordAviso aviso, CancellationToken ct)
    {
        try
        {
            // 1) abre (ou reaproveita) a conversa direta com o usuário
            using var canal = await EnviarAsync(() =>
            {
                var requisicao = new HttpRequestMessage(HttpMethod.Post, "users/@me/channels") { Content = Corpo(new { recipient_id = discordUserId }) };
                AutenticarComoBot(requisicao);
                return requisicao;
            }, ct);
            if (!canal.IsSuccessStatusCode) return await ClassificarFalhaAsync(canal, discordUserId, "abrir a conversa", ct);

            string canalId;
            using (var documento = await LerAsync(canal, ct))
            {
                canalId = documento.RootElement.GetProperty("id").GetString() ?? throw new DiscordApiException("Canal sem id.");
            }

            // 2) manda o aviso (embed: título em destaque, texto e link para abrir no CRM)
            var embed = new Dictionary<string, object?>
            {
                ["title"] = Cortar(aviso.Titulo, 256),
                ["description"] = Cortar(aviso.Corpo, 4000),
                ["color"] = 0x004384,
                ["timestamp"] = DateTimeOffset.UtcNow.ToString("O"),
            };
            if (!string.IsNullOrWhiteSpace(aviso.Url)) embed["url"] = aviso.Url;

            using var mensagem = await EnviarAsync(() =>
            {
                var requisicao = new HttpRequestMessage(HttpMethod.Post, $"channels/{canalId}/messages") { Content = Corpo(new { embeds = new[] { embed } }) };
                AutenticarComoBot(requisicao);
                return requisicao;
            }, ct);

            return mensagem.IsSuccessStatusCode ? ResultadoEnvioDiscord.Enviada : await ClassificarFalhaAsync(mensagem, discordUserId, "enviar a mensagem", ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or DiscordApiException or JsonException or KeyNotFoundException && !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Falha ao enviar aviso pelo Discord para a conta {DiscordUserId}.", discordUserId);
            return ResultadoEnvioDiscord.Erro;
        }
    }

    private async Task<ResultadoEnvioDiscord> ClassificarFalhaAsync(HttpResponseMessage resposta, string discordUserId, string etapa, CancellationToken ct)
    {
        int? codigo = null;
        try
        {
            using var documento = await LerAsync(resposta, ct);
            if (documento.RootElement.TryGetProperty("code", out var c) && c.TryGetInt32(out var valor)) codigo = valor;
        }
        catch (JsonException)
        {
            // corpo sem JSON: fica só o status HTTP
        }

        if (resposta.StatusCode == HttpStatusCode.Forbidden && codigo == CodigoMensagensDiretasBloqueadas)
        {
            logger.LogInformation("O usuário {DiscordUserId} bloqueou mensagens diretas no Discord; o aviso não foi entregue por lá.", discordUserId);
            return ResultadoEnvioDiscord.DmFechada;
        }

        logger.LogWarning("Discord recusou {Etapa} para {DiscordUserId} (HTTP {Status}, código {Codigo}).", etapa, discordUserId, (int)resposta.StatusCode, codigo);
        return ResultadoEnvioDiscord.Erro;
    }

    private void AutenticarComoBot(HttpRequestMessage requisicao) =>
        requisicao.Headers.Authorization = new AuthenticationHeaderValue("Bot", Opcoes.BotToken);

    private static StringContent Corpo(object conteudo) => new(JsonSerializer.Serialize(conteudo, Json), Encoding.UTF8, "application/json");

    private static async Task<JsonDocument> LerAsync(HttpResponseMessage resposta, CancellationToken ct) =>
        await JsonDocument.ParseAsync(await resposta.Content.ReadAsStreamAsync(ct), cancellationToken: ct);

    private static string Cortar(string texto, int limite) => texto.Length <= limite ? texto : texto[..(limite - 1)] + "…";

    /// <summary>Envia e, se o Discord pedir calma (429), espera o tempo indicado (no máximo 5 s) e tenta uma segunda vez.</summary>
    private async Task<HttpResponseMessage> EnviarAsync(Func<HttpRequestMessage> criar, CancellationToken ct)
    {
        var resposta = await http.SendAsync(criar(), ct);
        if (resposta.StatusCode != HttpStatusCode.TooManyRequests) return resposta;

        var espera = TimeSpan.FromSeconds(1);
        try
        {
            using var documento = await LerAsync(resposta, ct);
            if (documento.RootElement.TryGetProperty("retry_after", out var segundos) && segundos.TryGetDouble(out var s))
            {
                espera = TimeSpan.FromSeconds(Math.Clamp(s, 0.1, 5));
            }
        }
        catch (JsonException)
        {
            // sem corpo legível: espera o padrão
        }

        resposta.Dispose();
        await Task.Delay(espera, ct);
        return await http.SendAsync(criar(), ct);
    }
}
