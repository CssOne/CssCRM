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

    /// <summary>Últimas mensagens do canal, da mais antiga para a mais nova. <paramref name="antesDeId"/> pagina para trás.</summary>
    Task<IReadOnlyList<DiscordMensagem>> ListarMensagensAsync(string canalId, int limite, string? antesDeId, CancellationToken ct);

    /// <summary>
    /// Publica no canal com o nome e a foto da pessoa do CRM (via webhook do próprio CRM) e devolve a mensagem criada. Para uma thread,
    /// <paramref name="canalId"/> é o canal pai (dono do webhook) e <paramref name="threadId"/> a thread.
    /// </summary>
    Task<DiscordMensagem> EnviarMensagemAsync(string canalId, string nome, string? fotoUrl, string texto, CancellationToken ct, string? threadId = null);

    /// <summary>
    /// Cria o canal "Conversas diretas": quem tem o cargo vê o canal (e as threads em que foi adicionado) e pode escrever nas threads, mas
    /// não escreve no canal em si. As conversas 1:1 são threads privadas dentro dele.
    /// </summary>
    Task<string> CriarCanalDeConversasAsync(string nome, string categoriaId, string cargoId, CancellationToken ct);

    /// <summary>Cria uma thread privada no canal de conversas e devolve o id dela.</summary>
    Task<string> CriarConversaPrivadaAsync(string canalPaiId, string nome, CancellationToken ct);

    Task AdicionarAThreadAsync(string threadId, string discordUserId, CancellationToken ct);

    /// <summary>Igual a <see cref="EnviarMensagemAsync"/>, com um arquivo anexado (o texto pode ser vazio).</summary>
    Task<DiscordMensagem> EnviarArquivoAsync(string canalId, string nome, string? fotoUrl, string texto, DiscordArquivo arquivo, CancellationToken ct, string? threadId = null);

    /// <summary>Publica um aviso do próprio CRM (como bot) num canal. Nunca marca ninguém (@everyone/@here/cargos).</summary>
    Task PublicarAvisoAsync(string canalId, string texto, CancellationToken ct);

    /// <summary>
    /// Cria um canal de voz que só os <paramref name="permitidos"/> veem e usam (cargos ou pessoas). O CRM não consegue embutir a chamada:
    /// a tela abre este canal no Discord.
    /// </summary>
    Task<string> CriarCanalDeVozAsync(string nome, string categoriaId, IReadOnlyList<DiscordPermitido> permitidos, CancellationToken ct);
}

/// <summary>Quem pode usar um canal de voz: um cargo (<see cref="Pessoa"/> falso) ou uma pessoa (id da conta no Discord).</summary>
public record DiscordPermitido(string Id, bool Pessoa);

/// <summary>Mensagem de um canal do Discord, já com menções resolvidas para nomes.</summary>
public record DiscordMensagem(string Id, string AutorNome, string? AutorFotoUrl, string Conteudo, DateTimeOffset CriadaEm, IReadOnlyList<DiscordAnexo> Anexos, bool DoCrm);

public record DiscordAnexo(string Nome, string Url, bool Imagem);

/// <summary>Arquivo a anexar: o conteúdo fica em memória (o limite do chat é pequeno) para poder reenviar se o Discord pedir calma (429).</summary>
public record DiscordArquivo(string Nome, string TipoDeConteudo, byte[] Conteudo);

public sealed class DiscordGuildApi(HttpClient http, IOptions<DiscordOptions> options, ILogger<DiscordGuildApi> logger) : IDiscordGuildApi
{
    // Bits de permissão do Discord, em texto (a API usa texto porque passam de 32 bits).
    private const string VerCanal = "1024";

    /// <summary>Ver canal, enviar mensagens, ler o histórico, anexar arquivos, inserir links e reagir (1024+2048+65536+32768+16384+64).</summary>
    private const string PermissoesDoGrupo = "117824";

    /// <summary>O bot (membro cujo id é o da aplicação) precisa ver, ler e gerenciar webhooks nos canais que criou: o canal nega a visão ao @everyone.</summary>
    private const string PermissoesDoBot = "536939520"; // ver canal + enviar + ler histórico + gerenciar webhooks

    /// <summary>No canal de conversas, quem tem o cargo vê o canal, lê o histórico e escreve nas threads (1024 + 65536 + 274877906944).</summary>
    private const string PermissoesNoCanalDeConversas = "274877973504";

    /// <summary>
    /// O bot no canal de conversas: o que já tem nos outros canais (536939520) mais criar threads privadas (1 &lt;&lt; 36), gerenciar threads
    /// (1 &lt;&lt; 34) e escrever em threads (1 &lt;&lt; 38).
    /// </summary>
    private const string PermissoesDoBotNoCanalDeConversas = "361314192384";

    /// <summary>Canal de voz: ver, entrar, falar, transmitir tela/vídeo e usar detecção de voz (1024 + 1048576 + 2097152 + 512 + 33554432).</summary>
    private const string PermissoesDeVoz = "36701696";

    /// <summary>O bot só precisa ver o canal de voz e gerenciá-lo (conferir se existe, apagar): ver canal + gerenciar canal.</summary>
    private const string PermissoesDoBotNaVoz = "1040";

    private const string NomeDoWebhook = "CRM CSS Brasil";
    private const string MotivoAuditoria = "CRM CSS Brasil: sincronizacao de grupos";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // O cliente tipado é recriado de tempos em tempos: o guardado dos webhooks (canal -> id/token) vive no tipo, não na instância.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (string Id, string Token)> webhooks = new();

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
                    new { id = Opcoes.ClientId, type = 1, allow = PermissoesDoBot, deny = "0" },
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

    public async Task<IReadOnlyList<DiscordMensagem>> ListarMensagensAsync(string canalId, int limite, string? antesDeId, CancellationToken ct)
    {
        var caminho = $"channels/{canalId}/messages?limit={Math.Clamp(limite, 1, 100)}" + (antesDeId is null ? "" : $"&before={Uri.EscapeDataString(antesDeId)}");
        using var resposta = await EnviarAsync(() => Bot(HttpMethod.Get, caminho), ct);
        await GarantirAsync(resposta, "ler as mensagens do canal", ct);
        using var documento = await LerAsync(resposta, ct);
        // O Discord devolve da mais nova para a mais antiga; a tela mostra da mais antiga para a mais nova.
        return documento.RootElement.EnumerateArray().Select(LerMensagem).Reverse().ToList();
    }

    public async Task<DiscordMensagem> EnviarMensagemAsync(string canalId, string nome, string? fotoUrl, string texto, CancellationToken ct, string? threadId = null)
    {
        var (webhookId, webhookToken) = await ObterWebhookAsync(canalId, ct);
        var corpo = new
        {
            content = texto,
            username = Cortar(nome, 80),
            avatar_url = string.IsNullOrWhiteSpace(fotoUrl) ? null : fotoUrl,
            // Nunca marca @everyone/@here nem cargos por texto digitado no CRM.
            allowed_mentions = new { parse = Array.Empty<string>() },
        };
        var destino = $"webhooks/{webhookId}/{webhookToken}?wait=true" + (threadId is null ? "" : $"&thread_id={Uri.EscapeDataString(threadId)}");
        using var resposta = await EnviarAsync(() => new HttpRequestMessage(HttpMethod.Post, destino)
        {
            Content = new StringContent(JsonSerializer.Serialize(corpo, Json), Encoding.UTF8, "application/json"),
        }, ct);

        // Thread que o webhook não consegue usar (ex.: privada e arquivada): o bot, que é membro, publica com o nome de quem escreveu no começo.
        if (threadId is not null && !resposta.IsSuccessStatusCode && resposta.StatusCode != HttpStatusCode.NotFound)
        {
            return await EnviarComoBotAsync(threadId, nome, texto, ct);
        }

        if (resposta.StatusCode == HttpStatusCode.NotFound) webhooks.TryRemove(canalId, out _); // webhook apagado no Discord: recria na próxima
        await GarantirAsync(resposta, "enviar a mensagem", ct);
        using var documento = await LerAsync(resposta, ct);
        return LerMensagem(documento.RootElement);
    }

    public async Task<DiscordMensagem> EnviarArquivoAsync(string canalId, string nome, string? fotoUrl, string texto, DiscordArquivo arquivo, CancellationToken ct, string? threadId = null)
    {
        var (webhookId, webhookToken) = await ObterWebhookAsync(canalId, ct);
        var corpo = new
        {
            content = texto,
            username = Cortar(nome, 80),
            avatar_url = string.IsNullOrWhiteSpace(fotoUrl) ? null : fotoUrl,
            allowed_mentions = new { parse = Array.Empty<string>() },
        };
        var destino = $"webhooks/{webhookId}/{webhookToken}?wait=true" + (threadId is null ? "" : $"&thread_id={Uri.EscapeDataString(threadId)}");
        using var resposta = await EnviarAsync(() => new HttpRequestMessage(HttpMethod.Post, destino) { Content = Multipart(corpo, arquivo) }, ct);

        if (threadId is not null && !resposta.IsSuccessStatusCode && resposta.StatusCode != HttpStatusCode.NotFound)
        {
            // Mesmo plano B do texto: o bot publica na thread com o nome de quem enviou.
            var legenda = texto.Length == 0 ? $"**{nome}** enviou um arquivo" : $"**{nome}:** {texto}";
            var plano = new { content = legenda, allowed_mentions = new { parse = Array.Empty<string>() } };
            using var comoBot = await EnviarAsync(() =>
            {
                var requisicao = Bot(HttpMethod.Post, $"channels/{threadId}/messages");
                requisicao.Content = Multipart(plano, arquivo);
                return requisicao;
            }, ct);
            await GarantirAsync(comoBot, "enviar o arquivo", ct);
            using var documentoBot = await LerAsync(comoBot, ct);
            return LerMensagem(documentoBot.RootElement);
        }

        if (resposta.StatusCode == HttpStatusCode.NotFound) webhooks.TryRemove(canalId, out _);
        await GarantirAsync(resposta, "enviar o arquivo", ct);
        using var documento = await LerAsync(resposta, ct);
        return LerMensagem(documento.RootElement);
    }

    /// <summary>Corpo "multipart": o JSON da mensagem em <c>payload_json</c> e o arquivo em <c>files[0]</c>.</summary>
    private static MultipartFormDataContent Multipart(object payload, DiscordArquivo arquivo)
    {
        var conteudo = new MultipartFormDataContent
        {
            { new StringContent(JsonSerializer.Serialize(payload, Json), Encoding.UTF8, "application/json"), "payload_json" },
        };
        var parte = new ByteArrayContent(arquivo.Conteudo);
        parte.Headers.ContentType = MediaTypeHeaderValue.TryParse(arquivo.TipoDeConteudo, out var tipo) ? tipo : new MediaTypeHeaderValue("application/octet-stream");
        conteudo.Add(parte, "files[0]", arquivo.Nome);
        return conteudo;
    }

    public async Task PublicarAvisoAsync(string canalId, string texto, CancellationToken ct) =>
        await EnviarComoBotAsync(canalId, texto, ct);

    private async Task<DiscordMensagem> EnviarComoBotAsync(string canalId, string texto, CancellationToken ct)
    {
        var corpo = new { content = Cortar(texto, 2000), allowed_mentions = new { parse = Array.Empty<string>() } };
        using var resposta = await EnviarAsync(() =>
        {
            var requisicao = Bot(HttpMethod.Post, $"channels/{canalId}/messages");
            requisicao.Content = new StringContent(JsonSerializer.Serialize(corpo, Json), Encoding.UTF8, "application/json");
            return requisicao;
        }, ct);
        await GarantirAsync(resposta, "publicar o aviso", ct);
        using var documento = await LerAsync(resposta, ct);
        return LerMensagem(documento.RootElement);
    }

    private async Task<DiscordMensagem> EnviarComoBotAsync(string canalId, string nome, string texto, CancellationToken ct)
    {
        var corpo = new { content = $"**{nome}:** {texto}", allowed_mentions = new { parse = Array.Empty<string>() } };
        using var resposta = await EnviarAsync(() =>
        {
            var requisicao = Bot(HttpMethod.Post, $"channels/{canalId}/messages");
            requisicao.Content = new StringContent(JsonSerializer.Serialize(corpo, Json), Encoding.UTF8, "application/json");
            return requisicao;
        }, ct);
        await GarantirAsync(resposta, "enviar a mensagem", ct);
        using var documento = await LerAsync(resposta, ct);
        return LerMensagem(documento.RootElement);
    }

    public async Task<string> CriarCanalDeConversasAsync(string nome, string categoriaId, string cargoId, CancellationToken ct) =>
        await CriarAsync($"guilds/{Opcoes.GuildId}/channels",
            new
            {
                name = Cortar(nome, 100),
                type = 0,
                parent_id = categoriaId,
                topic = "Conversas diretas entre pessoas da equipe. Cada conversa é uma thread privada: só as duas pessoas veem.",
                permission_overwrites = new object[]
                {
                    // @everyone não vê; quem tem o cargo vê o canal e escreve nas threads, mas não no canal em si (para ficar só com as conversas).
                    Sobrescrita(Opcoes.GuildId, allow: null, deny: VerCanal),
                    new { id = cargoId, type = 0, allow = PermissoesNoCanalDeConversas, deny = "2048" },
                    new { id = Opcoes.ClientId, type = 1, allow = PermissoesDoBotNoCanalDeConversas, deny = "0" },
                },
            },
            "criar o canal de conversas", ct);

    public async Task<string> CriarConversaPrivadaAsync(string canalPaiId, string nome, CancellationToken ct) =>
        // type 12 = thread privada; "invitable: false" impede que as pessoas convidem terceiros; arquiva sozinha após 7 dias sem uso (volta ao escrever).
        await CriarAsync($"channels/{canalPaiId}/threads", new { name = Cortar(nome, 100), type = 12, invitable = false, auto_archive_duration = 10080 }, "criar a conversa", ct);

    public async Task<string> CriarCanalDeVozAsync(string nome, string categoriaId, IReadOnlyList<DiscordPermitido> permitidos, CancellationToken ct)
    {
        var sobrescritas = new List<object> { Sobrescrita(Opcoes.GuildId, allow: null, deny: VerCanal) }; // @everyone não vê
        sobrescritas.AddRange(permitidos.Select(p => (object)new { id = p.Id, type = p.Pessoa ? 1 : 0, allow = PermissoesDeVoz, deny = "0" }));
        sobrescritas.Add(new { id = Opcoes.ClientId, type = 1, allow = PermissoesDoBotNaVoz, deny = "0" });

        // type 2 = canal de voz.
        return await CriarAsync($"guilds/{Opcoes.GuildId}/channels",
            new { name = Cortar(nome, 100), type = 2, parent_id = categoriaId, permission_overwrites = sobrescritas },
            "criar o canal de voz", ct);
    }

    public async Task AdicionarAThreadAsync(string threadId, string discordUserId, CancellationToken ct)
    {
        using var resposta = await EnviarAsync(() => Bot(HttpMethod.Put, $"channels/{threadId}/thread-members/{discordUserId}"), ct);
        await GarantirAsync(resposta, "adicionar alguém à conversa", ct);
    }

    /// <summary>Webhook do CRM no canal (criado na primeira mensagem e guardado em memória; se não estiver guardado, procura o existente antes de criar).</summary>
    private async Task<(string Id, string Token)> ObterWebhookAsync(string canalId, CancellationToken ct)
    {
        if (webhooks.TryGetValue(canalId, out var guardado)) return guardado;

        using (var lista = await EnviarAsync(() => Bot(HttpMethod.Get, $"channels/{canalId}/webhooks"), ct))
        {
            await GarantirAsync(lista, "consultar os webhooks do canal", ct);
            using var documento = await LerAsync(lista, ct);
            foreach (var w in documento.RootElement.EnumerateArray())
            {
                if (w.TryGetProperty("name", out var n) && n.GetString() == NomeDoWebhook
                    && w.TryGetProperty("token", out var t) && t.GetString() is { Length: > 0 } token)
                {
                    return webhooks[canalId] = (w.GetProperty("id").GetString()!, token);
                }
            }
        }

        using var criado = await EnviarAsync(() =>
        {
            var requisicao = Bot(HttpMethod.Post, $"channels/{canalId}/webhooks");
            requisicao.Content = new StringContent(JsonSerializer.Serialize(new { name = NomeDoWebhook }, Json), Encoding.UTF8, "application/json");
            return requisicao;
        }, ct);
        await GarantirAsync(criado, "criar o webhook do canal", ct);
        using var novo = await LerAsync(criado, ct);
        return webhooks[canalId] = (novo.RootElement.GetProperty("id").GetString()!, novo.RootElement.GetProperty("token").GetString()!);
    }

    private static string? Texto(JsonElement e, string propriedade) =>
        e.TryGetProperty(propriedade, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static DiscordMensagem LerMensagem(JsonElement m)
    {
        var autor = m.GetProperty("author");
        var autorId = autor.GetProperty("id").GetString()!;
        var nome = Texto(autor, "global_name") ?? Texto(autor, "username") ?? "Discord";
        var avatar = Texto(autor, "avatar") is { } hash ? $"https://cdn.discordapp.com/avatars/{autorId}/{hash}.png?size=64" : null;

        var conteudo = Texto(m, "content") ?? "";
        if (m.TryGetProperty("mentions", out var mencoes) && mencoes.ValueKind == JsonValueKind.Array)
        {
            foreach (var u in mencoes.EnumerateArray())
            {
                var uid = u.GetProperty("id").GetString()!;
                var uNome = Texto(u, "global_name") ?? Texto(u, "username") ?? "usuário";
                conteudo = conteudo.Replace($"<@{uid}>", $"@{uNome}").Replace($"<@!{uid}>", $"@{uNome}");
            }
        }
        conteudo = EmojiPersonalizado.Replace(conteudo, "$1");

        var anexos = new List<DiscordAnexo>();
        if (m.TryGetProperty("attachments", out var arquivos) && arquivos.ValueKind == JsonValueKind.Array)
        {
            foreach (var f in arquivos.EnumerateArray())
            {
                var tipo = Texto(f, "content_type") ?? "";
                anexos.Add(new DiscordAnexo(Texto(f, "filename") ?? "arquivo", Texto(f, "url") ?? "", tipo.StartsWith("image/", StringComparison.OrdinalIgnoreCase)));
            }
        }

        // "DoCrm": mensagem publicada por um webhook do CRM (a tela alinha as suas à direita).
        var doCrm = Texto(m, "webhook_id") is { } wid && webhooks.Values.Any(w => w.Id == wid);
        var quando = DateTimeOffset.Parse(Texto(m, "timestamp")!, System.Globalization.CultureInfo.InvariantCulture);
        return new DiscordMensagem(m.GetProperty("id").GetString()!, nome, avatar, conteudo, quando, anexos, doCrm);
    }

    private static readonly System.Text.RegularExpressions.Regex EmojiPersonalizado = new(@"<a?(:\w+:)\d+>", System.Text.RegularExpressions.RegexOptions.Compiled);

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
