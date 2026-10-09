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

    Task<string> CriarCargoAsync(string nome, CancellationToken ct, DiscordAparenciaCargo? aparencia = null);

    /// <summary>Cria a categoria que agrupa os canais do CRM (invisível para quem não tem cargo).</summary>
    Task<string> CriarCategoriaAsync(string nome, CancellationToken ct);

    /// <summary>Cria um canal de texto visível e utilizável só por quem tem o cargo informado.</summary>
    Task<string> CriarCanalDeTextoAsync(string nome, string categoriaId, string cargoId, string topico, CancellationToken ct);

    Task<bool> CanalExisteAsync(string canalId, CancellationToken ct);

    /// <returns>Os cargos e o apelido do membro no servidor, ou <c>null</c> se a conta não está no servidor.</returns>
    Task<DiscordMembro?> ObterMembroAsync(string discordUserId, CancellationToken ct);

    /// <summary>Define o apelido do membro neste servidor (até 32 caracteres). Exige "Gerenciar apelidos" e que o cargo do bot esteja acima do membro.</summary>
    Task DefinirApelidoAsync(string discordUserId, string apelido, CancellationToken ct);

    Task AtribuirCargoAsync(string discordUserId, string cargoId, CancellationToken ct);

    Task RemoverCargoAsync(string discordUserId, string cargoId, CancellationToken ct);

    /// <summary>Últimas mensagens do canal, da mais antiga para a mais nova. <paramref name="antesDeId"/> pagina para trás.</summary>
    Task<IReadOnlyList<DiscordMensagem>> ListarMensagensAsync(string canalId, int limite, string? antesDeId, CancellationToken ct);

    /// <summary>
    /// Publica no canal com o nome e a foto da pessoa do CRM (via webhook do próprio CRM) e devolve a mensagem criada. Para uma thread,
    /// <paramref name="canalId"/> é o canal pai (dono do webhook) e <paramref name="threadId"/> a thread.
    /// </summary>
    Task<DiscordMensagem> EnviarMensagemAsync(string canalId, string nome, string? fotoUrl, string texto, CancellationToken ct, string? threadId = null, IReadOnlyList<string>? mencionar = null);

    /// <summary>
    /// Cria o canal "Conversas diretas": quem tem o cargo vê o canal (e as threads em que foi adicionado) e pode escrever nas threads, mas
    /// não escreve no canal em si. As conversas 1:1 são threads privadas dentro dele.
    /// </summary>
    Task<string> CriarCanalDeConversasAsync(string nome, string categoriaId, string cargoId, CancellationToken ct);

    /// <summary>Cria uma thread privada no canal de conversas e devolve o id dela.</summary>
    Task<string> CriarConversaPrivadaAsync(string canalPaiId, string nome, CancellationToken ct);

    Task AdicionarAThreadAsync(string threadId, string discordUserId, CancellationToken ct);

    /// <summary>Apaga um canal do servidor (irreversível). Canal que já não existe conta como apagado. Exige "Gerenciar canais".</summary>
    Task ApagarCanalAsync(string canalId, CancellationToken ct);

    /// <summary>Troca o nome (e, se informado, o tópico) de um canal do servidor. Exige "Gerenciar canais". O Discord limita a 2 trocas de nome a cada 10 minutos por canal.</summary>
    Task RenomearCanalAsync(string canalId, string nome, string? topico, CancellationToken ct);

    /// <summary>Reage à mensagem como o bot do CRM. <paramref name="emoji"/>: o caractere ou <c>nome:id</c>.</summary>
    Task AdicionarReacaoAsync(string canalOuThreadId, string mensagemId, string emoji, CancellationToken ct);

    /// <summary>Tira a reação do bot do CRM da mensagem.</summary>
    Task RemoverReacaoAsync(string canalOuThreadId, string mensagemId, string emoji, CancellationToken ct);

    /// <summary>Tira a fixação de uma mensagem (exige "Gerenciar mensagens" ou "Fixar mensagens").</summary>
    Task DesafixarMensagemAsync(string canalId, string mensagemId, CancellationToken ct);

    /// <summary>Mensagens fixadas do canal, da mais recente para a mais antiga.</summary>
    Task<IReadOnlyList<DiscordMensagem>> ListarFixadasAsync(string canalId, CancellationToken ct);

    /// <summary>Tópicos (threads públicas) ativos do servidor.</summary>
    Task<IReadOnlyList<DiscordTopico>> ListarTopicosAtivosAsync(CancellationToken ct);

    /// <summary>Um canal ou tópico (para saber a que canal um tópico pertence). Nulo se não existe mais.</summary>
    Task<DiscordTopico?> ObterTopicoAsync(string topicoId, CancellationToken ct);

    /// <summary>Cria um tópico (thread pública) no canal e devolve o id dele.</summary>
    Task<string> CriarTopicoAsync(string canalId, string nome, CancellationToken ct);

    /// <summary>
    /// Publica uma enquete no canal (ou thread). Vai pelo bot, porque o Discord não aceita enquete por webhook; <paramref name="autor"/> aparece na legenda.
    /// </summary>
    Task<DiscordMensagem> EnviarEnqueteAsync(string canalOuThreadId, string autor, string pergunta, IReadOnlyList<string> respostas, int horas, bool variasEscolhas, CancellationToken ct);

    /// <summary>Emojis personalizados do servidor.</summary>
    Task<IReadOnlyList<DiscordEmoji>> ListarEmojisAsync(CancellationToken ct);

    /// <summary>Figurinhas (stickers) do servidor.</summary>
    Task<IReadOnlyList<DiscordSticker>> ListarStickersAsync(CancellationToken ct);

    /// <summary>Publica só uma imagem (a figurinha), com o nome e a foto de quem enviou. O webhook não aceita figurinhas do servidor, por isso vai como imagem.</summary>
    Task<DiscordMensagem> EnviarImagemAsync(string canalId, string nome, string? fotoUrl, string imagemUrl, string descricao, CancellationToken ct, string? threadId = null);

    /// <summary>Igual a <see cref="EnviarMensagemAsync"/>, com um arquivo anexado (o texto pode ser vazio).</summary>
    Task<DiscordMensagem> EnviarArquivoAsync(string canalId, string nome, string? fotoUrl, string texto, DiscordArquivo arquivo, CancellationToken ct, string? threadId = null);

    /// <summary>Lê uma mensagem (para conferir de quem é antes de editar ou apagar).</summary>
    Task<DiscordMensagem> ObterMensagemAsync(string canalOuThreadId, string mensagemId, CancellationToken ct);

    /// <summary>Troca o texto de uma mensagem publicada pelo webhook do CRM. <paramref name="canalId"/> é o canal dono do webhook.</summary>
    Task EditarMensagemAsync(string canalId, string? threadId, string mensagemId, string texto, CancellationToken ct);

    /// <summary>Apaga uma mensagem publicada pelo webhook do CRM.</summary>
    Task ApagarMensagemAsync(string canalId, string? threadId, string mensagemId, CancellationToken ct);

    /// <summary>Publica um cartão (embed) do próprio CRM, como bot, num canal. Nunca marca ninguém (@everyone/@here/cargos).</summary>
    Task<DiscordMensagem> PublicarCartaoAsync(string canalId, DiscordCartao cartao, CancellationToken ct);

    /// <summary>Fixa uma mensagem no canal (exige "Gerenciar mensagens").</summary>
    Task FixarMensagemAsync(string canalId, string mensagemId, CancellationToken ct);

    /// <summary>
    /// Cria um canal de voz que só os <paramref name="permitidos"/> veem e usam (cargos ou pessoas). O CRM não consegue embutir a chamada:
    /// a tela abre este canal no Discord.
    /// </summary>
    Task<string> CriarCanalDeVozAsync(string nome, string categoriaId, IReadOnlyList<DiscordPermitido> permitidos, CancellationToken ct);
}

/// <summary>Quem pode usar um canal de voz: um cargo (<see cref="Pessoa"/> falso) ou uma pessoa (id da conta no Discord).</summary>
public record DiscordPermitido(string Id, bool Pessoa);

/// <summary>Mensagem de um canal do Discord, já com menções resolvidas para nomes.</summary>
public record DiscordMensagem(string Id, string AutorNome, string? AutorFotoUrl, string Conteudo, DateTimeOffset CriadaEm, IReadOnlyList<DiscordAnexo> Anexos, bool DoCrm, bool Editada = false, bool AutorEhBot = false, DiscordEnquete? Enquete = null,
    IReadOnlyList<DiscordReacao>? Reacoes = null, bool Fixada = false);

/// <summary>
/// Reação de uma mensagem. <see cref="Chave"/> é como o Discord identifica o emoji nas chamadas: o próprio caractere (emoji comum) ou <c>nome:id</c> (personalizado).
/// <see cref="EuReagi"/> é a reação do próprio bot do CRM (as reações feitas pelo CRM saem em nome dele).
/// </summary>
public record DiscordReacao(string Chave, string Nome, string? EmojiId, bool Animado, int Contagem, bool EuReagi);

/// <summary>Tópico (thread) do servidor: onde ele mora (<see cref="CanalPaiId"/>) e se já foi arquivado.</summary>
public record DiscordTopico(string Id, string Nome, string CanalPaiId, bool Arquivado);

/// <summary>Enquete (poll) de uma mensagem: a pergunta, as respostas com os votos até agora e se ainda aceita votos.</summary>
public record DiscordEnquete(string Pergunta, IReadOnlyList<DiscordRespostaDaEnquete> Respostas, bool VariasEscolhas, DateTimeOffset? EncerraEm, bool Encerrada);

public record DiscordRespostaDaEnquete(string Texto, int Votos);

/// <summary>Emoji personalizado do servidor (<c>&lt;:nome:id&gt;</c> no texto) e a imagem dele.</summary>
public record DiscordEmoji(string Id, string Nome, bool Animado, string Url);

/// <summary>Figurinha (sticker) do servidor e a imagem dela. As do tipo Lottie (animação vetorial) não aparecem: o navegador não as mostra como imagem.</summary>
public record DiscordSticker(string Id, string Nome, string? Descricao, string Url);

public record DiscordAnexo(string Nome, string Url, bool Imagem);

/// <summary>O que se sabe do membro do servidor: cargos e apelido (nulo = sem apelido; o Discord mostra o nome de usuário).</summary>
public record DiscordMembro(IReadOnlyCollection<string> Cargos, string? Apelido);

/// <summary>Cor do cargo (RGB, ex.: <c>0x3498DB</c>) e se aparece separado na lista de membros ("destacar").</summary>
public record DiscordAparenciaCargo(int Cor, bool Destacar);

/// <summary>Campo de um cartão: <paramref name="Lado"/> = mostra lado a lado com os vizinhos.</summary>
public record DiscordCampo(string Nome, string Valor, bool Lado = true);

/// <summary>Aviso do CRM no formato "cartão" do Discord: título, texto, cor da barra lateral e campos organizados.</summary>
public record DiscordCartao(string Titulo, string? Descricao, int Cor, IReadOnlyList<DiscordCampo>? Campos = null, string? Rodape = null);

/// <summary>Arquivo a anexar: o conteúdo fica em memória (o limite do chat é pequeno) para poder reenviar se o Discord pedir calma (429).</summary>
public record DiscordArquivo(string Nome, string TipoDeConteudo, byte[] Conteudo);

public sealed class DiscordGuildApi(HttpClient http, IOptions<DiscordOptions> options, ILogger<DiscordGuildApi> logger) : IDiscordGuildApi
{
    // Bits de permissão do Discord, em texto (a API usa texto porque passam de 32 bits).
    private const string VerCanal = "1024";

    /// <summary>Ver canal, enviar mensagens, ler o histórico, anexar arquivos, inserir links e reagir (1024+2048+65536+32768+16384+64).</summary>
    private const string PermissoesDoGrupo = "117824";

    /// <summary>O bot (membro cujo id é o da aplicação) precisa ver, ler e gerenciar webhooks nos canais que criou: o canal nega a visão ao @everyone.</summary>
    private const string PermissoesDoBot = "563259728006144"; // ver canal + enviar + ler histórico + gerenciar webhooks + criar tópicos públicos + enviar em tópicos + criar enquetes

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

    public async Task<string> CriarCargoAsync(string nome, CancellationToken ct, DiscordAparenciaCargo? aparencia = null) =>
        await CriarAsync($"guilds/{Opcoes.GuildId}/roles",
            new { name = Cortar(nome, 100), permissions = "0", hoist = aparencia?.Destacar ?? false, mentionable = false, color = aparencia?.Cor ?? 0 },
            "criar um cargo", ct);

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

    public async Task<DiscordMembro?> ObterMembroAsync(string discordUserId, CancellationToken ct)
    {
        using var resposta = await EnviarAsync(() => Bot(HttpMethod.Get, $"guilds/{Opcoes.GuildId}/members/{discordUserId}"), ct);
        if (resposta.StatusCode == HttpStatusCode.NotFound) return null;
        await GarantirAsync(resposta, "consultar um membro", ct);
        using var documento = await LerAsync(resposta, ct);
        var cargos = documento.RootElement.TryGetProperty("roles", out var lista) && lista.ValueKind == JsonValueKind.Array
            ? lista.EnumerateArray().Select(c => c.GetString()!).ToList()
            : [];
        return new DiscordMembro(cargos, Texto(documento.RootElement, "nick"));
    }

    public async Task DefinirApelidoAsync(string discordUserId, string apelido, CancellationToken ct)
    {
        using var resposta = await EnviarAsync(() =>
        {
            var requisicao = Bot(HttpMethod.Patch, $"guilds/{Opcoes.GuildId}/members/{discordUserId}");
            requisicao.Content = new StringContent(JsonSerializer.Serialize(new { nick = Cortar(apelido, 32) }, Json), Encoding.UTF8, "application/json");
            return requisicao;
        }, ct);
        await GarantirAsync(resposta, "definir o apelido de um membro", ct);
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

    public async Task<DiscordMensagem> EnviarMensagemAsync(string canalId, string nome, string? fotoUrl, string texto, CancellationToken ct, string? threadId = null, IReadOnlyList<string>? mencionar = null)
    {
        var (webhookId, webhookToken) = await ObterWebhookAsync(canalId, ct);
        var corpo = new
        {
            content = texto,
            username = Cortar(nome, 80),
            avatar_url = string.IsNullOrWhiteSpace(fotoUrl) ? null : fotoUrl,
            // Nunca marca @everyone/@here nem cargos por texto digitado no CRM; só as pessoas escolhidas na lista de menção.
            allowed_mentions = new { parse = Array.Empty<string>(), users = (mencionar ?? []).Distinct().Take(20).ToArray() },
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

    public async Task ApagarCanalAsync(string canalId, CancellationToken ct)
    {
        using var resposta = await EnviarAsync(() => Bot(HttpMethod.Delete, $"channels/{Uri.EscapeDataString(canalId)}"), ct);
        if (resposta.StatusCode == HttpStatusCode.NotFound) return; // já foi apagado no Discord
        await GarantirAsync(resposta, "apagar o canal", ct);
    }

    public async Task RenomearCanalAsync(string canalId, string nome, string? topico, CancellationToken ct)
    {
        object corpo = topico is null ? new { name = Cortar(nome, 100) } : new { name = Cortar(nome, 100), topic = Cortar(topico, 1024) };
        using var resposta = await EnviarAsync(() =>
        {
            var requisicao = Bot(HttpMethod.Patch, $"channels/{canalId}");
            requisicao.Content = new StringContent(JsonSerializer.Serialize(corpo, Json), Encoding.UTF8, "application/json");
            return requisicao;
        }, ct);
        await GarantirAsync(resposta, "renomear o canal", ct);
    }

    public async Task AdicionarReacaoAsync(string canalOuThreadId, string mensagemId, string emoji, CancellationToken ct)
    {
        using var resposta = await EnviarAsync(() => Bot(HttpMethod.Put, $"channels/{canalOuThreadId}/messages/{Uri.EscapeDataString(mensagemId)}/reactions/{Uri.EscapeDataString(emoji)}/@me"), ct);
        await GarantirAsync(resposta, "reagir à mensagem", ct);
    }

    public async Task RemoverReacaoAsync(string canalOuThreadId, string mensagemId, string emoji, CancellationToken ct)
    {
        using var resposta = await EnviarAsync(() => Bot(HttpMethod.Delete, $"channels/{canalOuThreadId}/messages/{Uri.EscapeDataString(mensagemId)}/reactions/{Uri.EscapeDataString(emoji)}/@me"), ct);
        if (resposta.StatusCode == HttpStatusCode.NotFound) return; // a reação já não estava lá
        await GarantirAsync(resposta, "tirar a reação", ct);
    }

    public async Task DesafixarMensagemAsync(string canalId, string mensagemId, CancellationToken ct)
    {
        using var resposta = await EnviarAsync(() => Bot(HttpMethod.Delete, $"channels/{canalId}/pins/{Uri.EscapeDataString(mensagemId)}"), ct);
        if (resposta.StatusCode == HttpStatusCode.NotFound) return;
        await GarantirAsync(resposta, "desafixar a mensagem", ct);
    }

    public async Task<IReadOnlyList<DiscordMensagem>> ListarFixadasAsync(string canalId, CancellationToken ct)
    {
        using var resposta = await EnviarAsync(() => Bot(HttpMethod.Get, $"channels/{canalId}/pins"), ct);
        await GarantirAsync(resposta, "ler as mensagens fixadas", ct);
        using var documento = await LerAsync(resposta, ct);
        return documento.RootElement.ValueKind == JsonValueKind.Array ? documento.RootElement.EnumerateArray().Select(LerMensagem).ToList() : [];
    }

    public async Task<IReadOnlyList<DiscordTopico>> ListarTopicosAtivosAsync(CancellationToken ct)
    {
        using var resposta = await EnviarAsync(() => Bot(HttpMethod.Get, $"guilds/{Opcoes.GuildId}/threads/active"), ct);
        await GarantirAsync(resposta, "listar os tópicos", ct);
        using var documento = await LerAsync(resposta, ct);
        var lista = new List<DiscordTopico>();
        if (documento.RootElement.TryGetProperty("threads", out var threads) && threads.ValueKind == JsonValueKind.Array)
        {
            foreach (var t in threads.EnumerateArray())
            {
                // type 11 = thread pública (as privadas das conversas 1:1 ficam de fora).
                if (!t.TryGetProperty("type", out var tipo) || tipo.ValueKind != JsonValueKind.Number || tipo.GetInt32() != 11) continue;
                if (LerTopico(t) is { } topico) lista.Add(topico);
            }
        }

        return lista;
    }

    public async Task<DiscordTopico?> ObterTopicoAsync(string topicoId, CancellationToken ct)
    {
        using var resposta = await EnviarAsync(() => Bot(HttpMethod.Get, $"channels/{Uri.EscapeDataString(topicoId)}"), ct);
        if (resposta.StatusCode == HttpStatusCode.NotFound) return null;
        await GarantirAsync(resposta, "ler o tópico", ct);
        using var documento = await LerAsync(resposta, ct);
        return LerTopico(documento.RootElement);
    }

    private static DiscordTopico? LerTopico(JsonElement t)
    {
        var id = Texto(t, "id");
        var pai = Texto(t, "parent_id");
        if (id is null || pai is null) return null;
        var arquivado = t.TryGetProperty("thread_metadata", out var meta) && meta.ValueKind == JsonValueKind.Object
            && meta.TryGetProperty("archived", out var arq) && arq.ValueKind == JsonValueKind.True;
        return new DiscordTopico(id, Texto(t, "name") ?? "tópico", pai, arquivado);
    }

    /// <summary>Reações da mensagem (<c>reactions</c>): emoji comum ou personalizado, quantas pessoas reagiram e se o bot é uma delas.</summary>
    private static List<DiscordReacao> LerReacoes(JsonElement m)
    {
        var lista = new List<DiscordReacao>();
        if (!m.TryGetProperty("reactions", out var reacoes) || reacoes.ValueKind != JsonValueKind.Array) return lista;
        foreach (var r in reacoes.EnumerateArray())
        {
            if (!r.TryGetProperty("emoji", out var emoji) || emoji.ValueKind != JsonValueKind.Object) continue;
            var nome = Texto(emoji, "name");
            if (string.IsNullOrEmpty(nome)) continue;
            var id = Texto(emoji, "id");
            var contagem = r.TryGetProperty("count", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : 0;
            var eu = r.TryGetProperty("me", out var me) && me.ValueKind == JsonValueKind.True;
            var animado = emoji.TryGetProperty("animated", out var an) && an.ValueKind == JsonValueKind.True;
            lista.Add(new DiscordReacao(id is null ? nome : $"{nome}:{id}", nome, id, animado, contagem, eu));
        }

        return lista;
    }

    public async Task<string> CriarTopicoAsync(string canalId, string nome, CancellationToken ct) =>
        // type 11 = thread pública; arquiva sozinha após 7 dias sem uso (volta ao escrever).
        await CriarAsync($"channels/{canalId}/threads", new { name = Cortar(nome, 100), type = 11, auto_archive_duration = 10080 }, "criar o tópico", ct);

    public async Task<DiscordMensagem> EnviarEnqueteAsync(string canalOuThreadId, string autor, string pergunta, IReadOnlyList<string> respostas, int horas, bool variasEscolhas, CancellationToken ct)
    {
        var corpo = new
        {
            content = $"📊 **{Cortar(autor, 80)}** criou uma enquete",
            poll = new
            {
                question = new { text = Cortar(pergunta, 300) },
                answers = respostas.Select(r => new { poll_media = new { text = Cortar(r, 55) } }).ToArray(),
                duration = horas,
                allow_multiselect = variasEscolhas,
            },
            allowed_mentions = new { parse = Array.Empty<string>() },
        };
        using var resposta = await EnviarAsync(() =>
        {
            var requisicao = Bot(HttpMethod.Post, $"channels/{canalOuThreadId}/messages");
            requisicao.Content = new StringContent(JsonSerializer.Serialize(corpo, Json), Encoding.UTF8, "application/json");
            return requisicao;
        }, ct);
        await GarantirAsync(resposta, "criar a enquete", ct);
        using var documento = await LerAsync(resposta, ct);
        return LerMensagem(documento.RootElement);
    }

    public async Task<IReadOnlyList<DiscordEmoji>> ListarEmojisAsync(CancellationToken ct)
    {
        using var resposta = await EnviarAsync(() => Bot(HttpMethod.Get, $"guilds/{Opcoes.GuildId}/emojis"), ct);
        await GarantirAsync(resposta, "listar os emojis", ct);
        using var documento = await LerAsync(resposta, ct);
        var emojis = new List<DiscordEmoji>();
        foreach (var e in documento.RootElement.EnumerateArray())
        {
            var id = Texto(e, "id");
            var nome = Texto(e, "name");
            if (id is null || nome is null) continue;
            if (e.TryGetProperty("available", out var disponivel) && disponivel.ValueKind == JsonValueKind.False) continue;
            var animado = e.TryGetProperty("animated", out var an) && an.ValueKind == JsonValueKind.True;
            emojis.Add(new DiscordEmoji(id, nome, animado, $"https://cdn.discordapp.com/emojis/{id}.{(animado ? "gif" : "png")}?size=64"));
        }
        return emojis.OrderBy(e => e.Nome, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<IReadOnlyList<DiscordSticker>> ListarStickersAsync(CancellationToken ct)
    {
        using var resposta = await EnviarAsync(() => Bot(HttpMethod.Get, $"guilds/{Opcoes.GuildId}/stickers"), ct);
        await GarantirAsync(resposta, "listar as figurinhas", ct);
        using var documento = await LerAsync(resposta, ct);
        var figurinhas = new List<DiscordSticker>();
        foreach (var f in documento.RootElement.EnumerateArray())
        {
            var id = Texto(f, "id");
            var nome = Texto(f, "name");
            if (id is null || nome is null) continue;
            var formato = f.TryGetProperty("format_type", out var ft) && ft.ValueKind == JsonValueKind.Number ? ft.GetInt32() : 1;
            if (UrlDaFigurinha(id, formato) is not { } url) continue;
            figurinhas.Add(new DiscordSticker(id, nome, Texto(f, "description"), url));
        }
        return figurinhas.OrderBy(f => f.Nome, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Imagem da figurinha: PNG/APNG e GIF o navegador mostra; Lottie (3) não.</summary>
    internal static string? UrlDaFigurinha(string id, int formato) => formato switch
    {
        1 or 2 => $"https://media.discordapp.net/stickers/{id}.png?size=160",
        4 => $"https://media.discordapp.net/stickers/{id}.gif?size=160",
        _ => null,
    };

    public async Task<DiscordMensagem> EnviarImagemAsync(string canalId, string nome, string? fotoUrl, string imagemUrl, string descricao, CancellationToken ct, string? threadId = null)
    {
        var (webhookId, webhookToken) = await ObterWebhookAsync(canalId, ct);
        var corpo = new
        {
            username = Cortar(nome, 80),
            avatar_url = string.IsNullOrWhiteSpace(fotoUrl) ? null : fotoUrl,
            embeds = new[] { new { image = new { url = imagemUrl } } },
            allowed_mentions = new { parse = Array.Empty<string>() },
        };
        var destino = $"webhooks/{webhookId}/{webhookToken}?wait=true" + (threadId is null ? "" : $"&thread_id={Uri.EscapeDataString(threadId)}");
        using var resposta = await EnviarAsync(() => new HttpRequestMessage(HttpMethod.Post, destino)
        {
            Content = new StringContent(JsonSerializer.Serialize(corpo, Json), Encoding.UTF8, "application/json"),
        }, ct);

        // Thread que o webhook não alcança: o bot publica o endereço da imagem, que o Discord abre como imagem.
        if (threadId is not null && !resposta.IsSuccessStatusCode && resposta.StatusCode != HttpStatusCode.NotFound)
        {
            return await EnviarComoBotAsync(threadId, nome, $"{descricao}\n{imagemUrl}", ct);
        }

        if (resposta.StatusCode == HttpStatusCode.NotFound) webhooks.TryRemove(canalId, out _);
        await GarantirAsync(resposta, "enviar a figurinha", ct);
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

    public async Task<DiscordMensagem> ObterMensagemAsync(string canalOuThreadId, string mensagemId, CancellationToken ct)
    {
        using var resposta = await EnviarAsync(() => Bot(HttpMethod.Get, $"channels/{canalOuThreadId}/messages/{Uri.EscapeDataString(mensagemId)}"), ct);
        await GarantirAsync(resposta, "ler a mensagem", ct);
        using var documento = await LerAsync(resposta, ct);
        return LerMensagem(documento.RootElement);
    }

    public async Task EditarMensagemAsync(string canalId, string? threadId, string mensagemId, string texto, CancellationToken ct)
    {
        var (webhookId, webhookToken) = await ObterWebhookAsync(canalId, ct);
        var corpo = new { content = texto, allowed_mentions = new { parse = Array.Empty<string>() } };
        using var resposta = await EnviarAsync(() => new HttpRequestMessage(HttpMethod.Patch, CaminhoDaMensagemDoWebhook(webhookId, webhookToken, mensagemId, threadId))
        {
            Content = new StringContent(JsonSerializer.Serialize(corpo, Json), Encoding.UTF8, "application/json"),
        }, ct);
        await GarantirAsync(resposta, "editar a mensagem", ct);
    }

    public async Task ApagarMensagemAsync(string canalId, string? threadId, string mensagemId, CancellationToken ct)
    {
        var (webhookId, webhookToken) = await ObterWebhookAsync(canalId, ct);
        using var resposta = await EnviarAsync(() => new HttpRequestMessage(HttpMethod.Delete, CaminhoDaMensagemDoWebhook(webhookId, webhookToken, mensagemId, threadId)), ct);
        await GarantirAsync(resposta, "apagar a mensagem", ct);
    }

    private static string CaminhoDaMensagemDoWebhook(string webhookId, string webhookToken, string mensagemId, string? threadId) =>
        $"webhooks/{webhookId}/{webhookToken}/messages/{Uri.EscapeDataString(mensagemId)}" + (threadId is null ? "" : $"?thread_id={Uri.EscapeDataString(threadId)}");

    public async Task<DiscordMensagem> PublicarCartaoAsync(string canalId, DiscordCartao cartao, CancellationToken ct)
    {
        var embed = new Dictionary<string, object?>
        {
            ["title"] = Cortar(cartao.Titulo, 256),
            ["color"] = cartao.Cor,
            ["timestamp"] = DateTimeOffset.UtcNow.ToString("O"),
        };
        if (!string.IsNullOrWhiteSpace(cartao.Descricao)) embed["description"] = Cortar(cartao.Descricao, 4000);
        if (cartao.Campos is { Count: > 0 })
        {
            embed["fields"] = cartao.Campos.Take(25).Select(c => new { name = Cortar(c.Nome, 256), value = Cortar(string.IsNullOrWhiteSpace(c.Valor) ? "-" : c.Valor, 1024), inline = c.Lado }).ToArray();
        }

        if (!string.IsNullOrWhiteSpace(cartao.Rodape)) embed["footer"] = new { text = Cortar(cartao.Rodape, 2048) };

        using var resposta = await EnviarAsync(() =>
        {
            var requisicao = Bot(HttpMethod.Post, $"channels/{canalId}/messages");
            requisicao.Content = new StringContent(JsonSerializer.Serialize(new { embeds = new[] { embed }, allowed_mentions = new { parse = Array.Empty<string>() } }, Json), Encoding.UTF8, "application/json");
            return requisicao;
        }, ct);
        await GarantirAsync(resposta, "publicar o aviso", ct);
        using var documento = await LerAsync(resposta, ct);
        return LerMensagem(documento.RootElement);
    }

    public async Task FixarMensagemAsync(string canalId, string mensagemId, CancellationToken ct)
    {
        using var resposta = await EnviarAsync(() => Bot(HttpMethod.Put, $"channels/{canalId}/pins/{Uri.EscapeDataString(mensagemId)}"), ct);
        await GarantirAsync(resposta, "fixar a mensagem", ct);
    }

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

    private DiscordMensagem LerMensagem(JsonElement m)
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

        // Mensagem só com cartão (embed), como os avisos e a boas-vindas que o próprio CRM publica: o texto está no cartão, não em "content".
        if (conteudo.Length == 0 && m.TryGetProperty("embeds", out var cartoes) && cartoes.ValueKind == JsonValueKind.Array)
        {
            conteudo = string.Join("\n\n", cartoes.EnumerateArray().Select(TextoDoCartao).Where(t => t.Length > 0));
        }

        var anexos = new List<DiscordAnexo>();
        if (m.TryGetProperty("attachments", out var arquivos) && arquivos.ValueKind == JsonValueKind.Array)
        {
            foreach (var f in arquivos.EnumerateArray())
            {
                var tipo = Texto(f, "content_type") ?? "";
                anexos.Add(new DiscordAnexo(Texto(f, "filename") ?? "arquivo", Texto(f, "url") ?? "", tipo.StartsWith("image/", StringComparison.OrdinalIgnoreCase)));
            }
        }

        // Figurinhas da mensagem aparecem como imagem (as animadas Lottie não têm imagem e ficam de fora).
        if (m.TryGetProperty("sticker_items", out var figurinhas) && figurinhas.ValueKind == JsonValueKind.Array)
        {
            foreach (var f in figurinhas.EnumerateArray())
            {
                var fid = Texto(f, "id");
                var formato = f.TryGetProperty("format_type", out var ft) && ft.ValueKind == JsonValueKind.Number ? ft.GetInt32() : 1;
                if (fid is not null && UrlDaFigurinha(fid, formato) is { } url) anexos.Add(new DiscordAnexo(Texto(f, "name") ?? "figurinha", url, true));
            }
        }

        // Menção a canal/tópico (<#id>) vira o endereço dele no Discord, que a tela mostra como link.
        conteudo = MencaoDeCanal.Replace(conteudo, $"https://discord.com/channels/{Opcoes.GuildId}/$1");

        // "DoCrm": mensagem publicada por um webhook do CRM (a tela alinha as suas à direita). Reconhece pelo dono do webhook (a aplicação do CRM),
        // que vale mesmo depois de reiniciar o servidor; o guardado em memória cobre respostas que não trazem o dono.
        var doCrm = Texto(m, "webhook_id") is { } wid
            && (Texto(m, "application_id") == Opcoes.ClientId || webhooks.Values.Any(w => w.Id == wid));
        var quando = DateTimeOffset.Parse(Texto(m, "timestamp")!, System.Globalization.CultureInfo.InvariantCulture);
        var autorEhBot = autor.TryGetProperty("bot", out var bot) && bot.ValueKind == JsonValueKind.True;
        return new DiscordMensagem(m.GetProperty("id").GetString()!, nome, avatar, conteudo, quando, anexos, doCrm, Texto(m, "edited_timestamp") is not null, autorEhBot, LerEnquete(m), LerReacoes(m), m.TryGetProperty("pinned", out var fixada) && fixada.ValueKind == JsonValueKind.True);
    }

    /// <summary>Texto corrido de um cartão: título em negrito, descrição e cada campo como "**Nome:** valor".</summary>
    private static string TextoDoCartao(JsonElement cartao)
    {
        var linhas = new List<string>();
        if (Texto(cartao, "title") is { Length: > 0 } titulo) linhas.Add($"**{titulo}**");
        if (Texto(cartao, "description") is { Length: > 0 } descricao) linhas.Add(descricao);
        if (cartao.TryGetProperty("fields", out var campos) && campos.ValueKind == JsonValueKind.Array)
        {
            foreach (var campo in campos.EnumerateArray())
            {
                if (Texto(campo, "name") is { Length: > 0 } nome) linhas.Add($"**{nome}:** {Texto(campo, "value")}");
            }
        }

        return string.Join("\n", linhas);
    }

    private static readonly System.Text.RegularExpressions.Regex MencaoDeCanal = new(@"<#(\d+)>", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>Enquete da mensagem (<c>poll</c>): pergunta, respostas com a contagem de votos e se já encerrou. Nulo se a mensagem não tem enquete.</summary>
    internal static DiscordEnquete? LerEnquete(JsonElement m)
    {
        if (!m.TryGetProperty("poll", out var poll) || poll.ValueKind != JsonValueKind.Object) return null;
        var pergunta = poll.TryGetProperty("question", out var q) ? Texto(q, "text") : null;
        if (string.IsNullOrWhiteSpace(pergunta)) return null;

        var votos = new Dictionary<int, int>();
        var encerrada = false;
        if (poll.TryGetProperty("results", out var resultados) && resultados.ValueKind == JsonValueKind.Object)
        {
            encerrada = resultados.TryGetProperty("is_finalized", out var fin) && fin.ValueKind == JsonValueKind.True;
            if (resultados.TryGetProperty("answer_counts", out var contagens) && contagens.ValueKind == JsonValueKind.Array)
            {
                foreach (var c in contagens.EnumerateArray())
                {
                    if (c.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number && c.TryGetProperty("count", out var n) && n.ValueKind == JsonValueKind.Number)
                    {
                        votos[id.GetInt32()] = n.GetInt32();
                    }
                }
            }
        }

        var respostas = new List<DiscordRespostaDaEnquete>();
        if (poll.TryGetProperty("answers", out var lista) && lista.ValueKind == JsonValueKind.Array)
        {
            var posicao = 0;
            foreach (var a in lista.EnumerateArray())
            {
                posicao++;
                var idDaResposta = a.TryGetProperty("answer_id", out var aid) && aid.ValueKind == JsonValueKind.Number ? aid.GetInt32() : posicao;
                var texto = a.TryGetProperty("poll_media", out var media) ? Texto(media, "text") : null;
                respostas.Add(new DiscordRespostaDaEnquete(texto ?? "—", votos.GetValueOrDefault(idDaResposta)));
            }
        }

        DateTimeOffset? expira = Texto(poll, "expiry") is { } e && DateTimeOffset.TryParse(e, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d) ? d : null;
        if (expira is { } fim && fim <= DateTimeOffset.UtcNow) encerrada = true;
        return new DiscordEnquete(pergunta, respostas, poll.TryGetProperty("allow_multiselect", out var multi) && multi.ValueKind == JsonValueKind.True, expira, encerrada);
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
