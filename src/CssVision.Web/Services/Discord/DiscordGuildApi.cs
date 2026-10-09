using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace CssVision.Web.Services.Discord;

/// <summary>
/// Gestão do servidor (guilda) da empresa no Discord: cargos, categoria, canais e cargos dos membros. É o que o CRM usa para criar os
/// grupos (um canal + um cargo por grupo) e dar a cada pessoa o cargo dos grupos dela. Exige que o bot tenha as permissões
/// "Gerenciar canais" e "Gerenciar cargos" no servidor (e que o cargo do bot esteja acima dos cargos que ele gerencia).
/// </summary>
public interface IDiscordGuildApi
{
    Task<HashSet<string>> ListarIdsDeCargosAsync(CancellationToken ct);

    Task<string> CriarCargoAsync(string nome, CancellationToken ct);

    /// <summary>Cria a categoria que agrupa os canais do CRM (invisível para quem não tem cargo).</summary>
    Task<string> CriarCategoriaAsync(string nome, CancellationToken ct);

    /// <summary>Cria um canal de texto visível e utilizável só por quem tem o cargo informado.</summary>
    Task<string> CriarCanalDeTextoAsync(string nome, string categoriaId, string cargoId, string topico, CancellationToken ct);

    Task<bool> CanalExisteAsync(string canalId, CancellationToken ct);

    /// <returns>Os cargos do membro, ou <c>null</c> se a conta não está no servidor.</returns>
    Task<IReadOnlyCollection<string>?> ObterCargosDoMembroAsync(string discordUserId, CancellationToken ct);

    Task AtribuirCargoAsync(string discordUserId, string cargoId, CancellationToken ct);

    Task RemoverCargoAsync(string discordUserId, string cargoId, CancellationToken ct);
}

public sealed class DiscordGuildApi(HttpClient http, IOptions<DiscordOptions> options, ILogger<DiscordGuildApi> logger) : IDiscordGuildApi
{
    // Bits de permissão do Discord, em texto (a API usa texto porque passam de 32 bits).
    private const string VerCanal = "1024";

    /// <summary>Ver canal, enviar mensagens, ler o histórico, anexar arquivos, inserir links e reagir (1024+2048+65536+32768+16384+64).</summary>
    private const string PermissoesDoGrupo = "117824";

    private const string MotivoAuditoria = "CRM CSS Brasil: sincronizacao de grupos";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private DiscordOptions Opcoes => options.Value;

    public async Task<HashSet<string>> ListarIdsDeCargosAsync(CancellationToken ct)
    {
        using var resposta = await EnviarAsync(() => Bot(HttpMethod.Get, $"guilds/{Opcoes.GuildId}/roles"), ct);
        await GarantirAsync(resposta, "listar os cargos do servidor", ct);
        using var documento = await LerAsync(resposta, ct);
        return documento.RootElement.EnumerateArray().Select(e => e.GetProperty("id").GetString()!).ToHashSet();
    }

    public async Task<string> CriarCargoAsync(string nome, CancellationToken ct) =>
        await CriarAsync($"guilds/{Opcoes.GuildId}/roles", new { name = Cortar(nome, 100), permissions = "0", hoist = false, mentionable = false }, "criar um cargo", ct);

    public async Task<string> CriarCategoriaAsync(string nome, CancellationToken ct) =>
        await CriarAsync($"guilds/{Opcoes.GuildId}/channels",
            new { name = Cortar(nome, 100), type = 4, permission_overwrites = new object[] { Sobrescrita(Opcoes.GuildId, allow: null, deny: VerCanal) } },
            "criar a categoria", ct);

    public async Task<string> CriarCanalDeTextoAsync(string nome, string categoriaId, string cargoId, string topico, CancellationToken ct) =>
        await CriarAsync($"guilds/{Opcoes.GuildId}/channels",
            new
            {
                name = Cortar(nome, 100),
                type = 0,
                parent_id = categoriaId,
                topic = Cortar(topico, 1024),
                permission_overwrites = new object[]
                {
                    // @everyone (o cargo "todos" tem o mesmo id do servidor) não vê; só quem tem o cargo do grupo.
                    Sobrescrita(Opcoes.GuildId, allow: null, deny: VerCanal),
                    Sobrescrita(cargoId, allow: PermissoesDoGrupo, deny: null),
                },
            },
            "criar um canal", ct);

    public async Task<bool> CanalExisteAsync(string canalId, CancellationToken ct)
    {
        using var resposta = await EnviarAsync(() => Bot(HttpMethod.Get, $"channels/{canalId}"), ct);
        if (resposta.StatusCode == HttpStatusCode.NotFound) return false;
        await GarantirAsync(resposta, "consultar um canal", ct);
        return true;
    }

    public async Task<IReadOnlyCollection<string>?> ObterCargosDoMembroAsync(string discordUserId, CancellationToken ct)
    {
        using var resposta = await EnviarAsync(() => Bot(HttpMethod.Get, $"guilds/{Opcoes.GuildId}/members/{discordUserId}"), ct);
        if (resposta.StatusCode == HttpStatusCode.NotFound) return null;
        await GarantirAsync(resposta, "consultar um membro", ct);
        using var documento = await LerAsync(resposta, ct);
        return documento.RootElement.TryGetProperty("roles", out var cargos)
            ? cargos.EnumerateArray().Select(c => c.GetString()!).ToList()
            : [];
    }

    public async Task AtribuirCargoAsync(string discordUserId, string cargoId, CancellationToken ct)
    {
        using var resposta = await EnviarAsync(() => Bot(HttpMethod.Put, $"guilds/{Opcoes.GuildId}/members/{discordUserId}/roles/{cargoId}"), ct);
        await GarantirAsync(resposta, "dar um cargo a um membro", ct);
    }

    public async Task RemoverCargoAsync(string discordUserId, string cargoId, CancellationToken ct)
    {
        using var resposta = await EnviarAsync(() => Bot(HttpMethod.Delete, $"guilds/{Opcoes.GuildId}/members/{discordUserId}/roles/{cargoId}"), ct);
        await GarantirAsync(resposta, "tirar um cargo de um membro", ct);
    }

    private async Task<string> CriarAsync(string caminho, object corpo, string acao, CancellationToken ct)
    {
        using var resposta = await EnviarAsync(() =>
        {
            var requisicao = Bot(HttpMethod.Post, caminho);
            requisicao.Content = new StringContent(JsonSerializer.Serialize(corpo, Json), Encoding.UTF8, "application/json");
            return requisicao;
        }, ct);
        await GarantirAsync(resposta, acao, ct);
        using var documento = await LerAsync(resposta, ct);
        return documento.RootElement.GetProperty("id").GetString() ?? throw new DiscordApiException($"O Discord não devolveu o id ao {acao}.");
    }

    private static object Sobrescrita(string id, string? allow, string? deny) =>
        new { id, type = 0, allow = allow ?? "0", deny = deny ?? "0" };

    private HttpRequestMessage Bot(HttpMethod metodo, string caminho)
    {
        var requisicao = new HttpRequestMessage(metodo, caminho);
        requisicao.Headers.Authorization = new AuthenticationHeaderValue("Bot", Opcoes.BotToken);
        requisicao.Headers.TryAddWithoutValidation("X-Audit-Log-Reason", MotivoAuditoria);
        return requisicao;
    }

    /// <summary>Transforma a resposta de erro do Discord numa mensagem que o administrador entende (sem expor token).</summary>
    private async Task GarantirAsync(HttpResponseMessage resposta, string acao, CancellationToken ct)
    {
        if (resposta.IsSuccessStatusCode) return;

        int? codigo = null;
        try
        {
            using var documento = await LerAsync(resposta, ct);
            if (documento.RootElement.TryGetProperty("code", out var c) && c.TryGetInt32(out var valor)) codigo = valor;
        }
        catch (JsonException)
        {
            // corpo sem JSON
        }

        logger.LogWarning("Discord recusou ao {Acao} (HTTP {Status}, código {Codigo}).", acao, (int)resposta.StatusCode, codigo);
        throw new DiscordApiException(resposta.StatusCode == HttpStatusCode.Forbidden
            ? $"O bot não tem permissão para {acao}. No servidor, o bot precisa de \"Gerenciar canais\" e \"Gerenciar cargos\", e o cargo dele precisa estar acima dos cargos do CRM."
            : $"O Discord recusou ao {acao} (HTTP {(int)resposta.StatusCode}).");
    }

    private static async Task<JsonDocument> LerAsync(HttpResponseMessage resposta, CancellationToken ct) =>
        await JsonDocument.ParseAsync(await resposta.Content.ReadAsStreamAsync(ct), cancellationToken: ct);

    private static string Cortar(string texto, int limite) => texto.Length <= limite ? texto : texto[..limite];

    /// <summary>Envia e, se o Discord pedir calma (429), espera o tempo indicado (no máximo 5 s) e tenta mais uma vez.</summary>
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
