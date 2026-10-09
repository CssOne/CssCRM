using CssVision.Web.Services.Discord;

namespace CssVision.Web.Tests.Infrastructure;

/// <summary>Servidor do Discord de mentira: guarda cargos, canais, membros e mensagens em memória.</summary>
public sealed class DiscordServidorFalso : IDiscordGuildApi
{
    private int _proximoId = 1000;
    public HashSet<string> Cargos { get; } = [];
    public HashSet<string> Canais { get; } = [];
    public Dictionary<string, string> NomesDeCargo { get; } = [];

    /// <summary>Membros do servidor (id do Discord → cargos). Quem não está aqui "não entrou no servidor".</summary>
    public Dictionary<string, HashSet<string>> Membros { get; } = [];

    public bool SemPermissao { get; set; }

    /// <summary>Só a criação de conversas (threads privadas) falha, por falta de permissão do bot.</summary>
    public bool SemPermissaoParaThreads { get; set; }

    /// <summary>Mensagens por canal, da mais antiga para a mais nova.</summary>
    public Dictionary<string, List<DiscordMensagem>> Mensagens { get; } = [];

    public int LeiturasDeMensagens { get; private set; }

    public List<(string CanalId, string Nome, string? FotoUrl, string Texto)> Enviadas { get; } = [];

    private string Novo() => (_proximoId++).ToString();

    public Task<HashSet<string>> ListarIdsDeCargosAsync(CancellationToken ct) => Task.FromResult(new HashSet<string>(Cargos));

    public Task<string> CriarCargoAsync(string nome, CancellationToken ct)
    {
        if (SemPermissao) throw new DiscordApiException("O bot não tem permissão para criar um cargo.");
        var id = Novo();
        Cargos.Add(id);
        NomesDeCargo[id] = nome;
        return Task.FromResult(id);
    }

    public Task<string> CriarCategoriaAsync(string nome, CancellationToken ct)
    {
        var id = Novo();
        Canais.Add(id);
        return Task.FromResult(id);
    }

    public Task<string> CriarCanalDeTextoAsync(string nome, string categoriaId, string cargoId, string topico, CancellationToken ct)
    {
        var id = Novo();
        Canais.Add(id);
        return Task.FromResult(id);
    }

    public Task<bool> CanalExisteAsync(string canalId, CancellationToken ct) => Task.FromResult(Canais.Contains(canalId));

    public Task<IReadOnlyCollection<string>?> ObterCargosDoMembroAsync(string discordUserId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyCollection<string>?>(Membros.TryGetValue(discordUserId, out var c) ? c.ToList() : null);

    public Task AtribuirCargoAsync(string discordUserId, string cargoId, CancellationToken ct)
    {
        Membros[discordUserId].Add(cargoId);
        return Task.CompletedTask;
    }

    public Task RemoverCargoAsync(string discordUserId, string cargoId, CancellationToken ct)
    {
        Membros[discordUserId].Remove(cargoId);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DiscordMensagem>> ListarMensagensAsync(string canalId, int limite, string? antesDeId, CancellationToken ct)
    {
        LeiturasDeMensagens++;
        if (SemPermissao) throw new DiscordApiException("O bot não tem permissão para ler as mensagens do canal.");
        var todas = Mensagens.TryGetValue(canalId, out var l) ? l : [];
        var anteriores = antesDeId is null ? todas : todas.TakeWhile(m => m.Id != antesDeId).ToList();
        return Task.FromResult<IReadOnlyList<DiscordMensagem>>(anteriores.TakeLast(limite).ToList());
    }

    /// <summary>Threads privadas criadas: id da thread → ids do Discord de quem foi adicionado.</summary>
    public Dictionary<string, List<string>> Threads { get; } = [];

    public List<(string CanalPaiId, string Nome)> ThreadsCriadas { get; } = [];

    /// <summary>Pessoas marcadas (ids do Discord) em cada envio com menção.</summary>
    public List<IReadOnlyList<string>> MencoesEnviadas { get; } = [];

    public Task<DiscordMensagem> EnviarMensagemAsync(string canalId, string nome, string? fotoUrl, string texto, CancellationToken ct, string? threadId = null, IReadOnlyList<string>? mencionar = null)
    {
        if (mencionar is { Count: > 0 }) MencoesEnviadas.Add(mencionar);
        if (SemPermissao) throw new DiscordApiException("O bot não tem permissão para enviar a mensagem.");
        Enviadas.Add((threadId ?? canalId, nome, fotoUrl, texto));
        var mensagem = new DiscordMensagem(Novo(), nome, fotoUrl, texto, DateTimeOffset.UtcNow, [], true);
        var onde = threadId ?? canalId;
        if (!Mensagens.TryGetValue(onde, out var lista)) Mensagens[onde] = lista = [];
        lista.Add(mensagem);
        return Task.FromResult(mensagem);
    }

    public Task<string> CriarCanalDeConversasAsync(string nome, string categoriaId, string cargoId, CancellationToken ct)
    {
        if (SemPermissao) throw new DiscordApiException("O bot não tem permissão para criar o canal de conversas.");
        var id = Novo();
        Canais.Add(id);
        return Task.FromResult(id);
    }

    public Task<string> CriarConversaPrivadaAsync(string canalPaiId, string nome, CancellationToken ct)
    {
        if (SemPermissaoParaThreads) throw new DiscordApiException("O bot não tem permissão para criar a conversa.");
        var id = Novo();
        Threads[id] = [];
        ThreadsCriadas.Add((canalPaiId, nome));
        return Task.FromResult(id);
    }

    public Task AdicionarAThreadAsync(string threadId, string discordUserId, CancellationToken ct)
    {
        Threads[threadId].Add(discordUserId);
        return Task.CompletedTask;
    }

    public List<(string Nome, string CategoriaId, IReadOnlyList<DiscordPermitido> Permitidos)> VozesCriadas { get; } = [];

    public Task<string> CriarCanalDeVozAsync(string nome, string categoriaId, IReadOnlyList<DiscordPermitido> permitidos, CancellationToken ct)
    {
        if (SemPermissao) throw new DiscordApiException("O bot não tem permissão para criar o canal de voz.");
        var id = Novo();
        Canais.Add(id);
        VozesCriadas.Add((nome, categoriaId, permitidos));
        return Task.FromResult(id);
    }

    /// <summary>Avisos do CRM publicados nos canais (como bot).</summary>
    public List<(string CanalId, string Texto)> Avisos { get; } = [];

    public Task PublicarAvisoAsync(string canalId, string texto, CancellationToken ct)
    {
        if (SemPermissao) throw new DiscordApiException("O bot não tem permissão para publicar o aviso.");
        Avisos.Add((canalId, texto));
        return Task.CompletedTask;
    }

    /// <summary>Arquivos enviados ao chat (destino = thread ou canal).</summary>
    public List<(string CanalId, string Nome, string Texto, DiscordArquivo Arquivo)> ArquivosEnviados { get; } = [];

    public Task<DiscordMensagem> EnviarArquivoAsync(string canalId, string nome, string? fotoUrl, string texto, DiscordArquivo arquivo, CancellationToken ct, string? threadId = null)
    {
        if (SemPermissao) throw new DiscordApiException("O bot não tem permissão para enviar o arquivo.");
        var onde = threadId ?? canalId;
        ArquivosEnviados.Add((onde, nome, texto, arquivo));
        var mensagem = new DiscordMensagem(Novo(), nome, fotoUrl, texto, DateTimeOffset.UtcNow, [new DiscordAnexo(arquivo.Nome, $"https://cdn.exemplo.com/{arquivo.Nome}", arquivo.Nome.EndsWith(".png"))], true);
        if (!Mensagens.TryGetValue(onde, out var lista)) Mensagens[onde] = lista = [];
        lista.Add(mensagem);
        return Task.FromResult(mensagem);
    }

    public List<(string Onde, string MensagemId, string Texto)> Edicoes { get; } = [];

    public List<(string Onde, string MensagemId)> Apagadas { get; } = [];

    public Task<DiscordMensagem> ObterMensagemAsync(string canalOuThreadId, string mensagemId, CancellationToken ct)
    {
        var m = Mensagens.TryGetValue(canalOuThreadId, out var l) ? l.FirstOrDefault(x => x.Id == mensagemId) : null;
        return m is null ? throw new DiscordApiException("O Discord recusou ao ler a mensagem (HTTP 404).") : Task.FromResult(m);
    }

    public Task EditarMensagemAsync(string canalId, string? threadId, string mensagemId, string texto, CancellationToken ct)
    {
        var onde = threadId ?? canalId;
        var lista = Mensagens[onde];
        var i = lista.FindIndex(x => x.Id == mensagemId);
        lista[i] = lista[i] with { Conteudo = texto, Editada = true };
        Edicoes.Add((onde, mensagemId, texto));
        return Task.CompletedTask;
    }

    public Task ApagarMensagemAsync(string canalId, string? threadId, string mensagemId, CancellationToken ct)
    {
        var onde = threadId ?? canalId;
        Mensagens[onde].RemoveAll(x => x.Id == mensagemId);
        Apagadas.Add((onde, mensagemId));
        return Task.CompletedTask;
    }
}
