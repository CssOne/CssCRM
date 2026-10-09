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

    public Task<DiscordMensagem> EnviarMensagemAsync(string canalId, string nome, string? fotoUrl, string texto, CancellationToken ct)
    {
        if (SemPermissao) throw new DiscordApiException("O bot não tem permissão para enviar a mensagem.");
        Enviadas.Add((canalId, nome, fotoUrl, texto));
        var mensagem = new DiscordMensagem(Novo(), nome, fotoUrl, texto, DateTimeOffset.UtcNow, [], true);
        if (!Mensagens.TryGetValue(canalId, out var lista)) Mensagens[canalId] = lista = [];
        lista.Add(mensagem);
        return Task.FromResult(mensagem);
    }
}
