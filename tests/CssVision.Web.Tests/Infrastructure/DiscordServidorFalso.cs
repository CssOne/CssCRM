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

    /// <summary>Cor e "destacar" com que cada cargo foi criado (id do cargo → aparência).</summary>
    public Dictionary<string, DiscordAparenciaCargo?> AparenciasDeCargo { get; } = [];

    public Task<string> CriarCargoAsync(string nome, CancellationToken ct, DiscordAparenciaCargo? aparencia = null)
    {
        if (SemPermissao) throw new DiscordApiException("O bot não tem permissão para criar um cargo.");
        var id = Novo();
        Cargos.Add(id);
        NomesDeCargo[id] = nome;
        AparenciasDeCargo[id] = aparencia;
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

    /// <summary>Apelido de cada membro no servidor (id do Discord → apelido; ausente = sem apelido).</summary>
    public Dictionary<string, string> Apelidos { get; } = [];

    /// <summary>O Discord recusa trocar apelidos (ex.: o bot não tem "Gerenciar apelidos" ou o membro é o dono do servidor).</summary>
    public bool SemPermissaoParaApelidos { get; set; }

    public List<(string DiscordUserId, string Apelido)> ApelidosDefinidos { get; } = [];

    public Task<DiscordMembro?> ObterMembroAsync(string discordUserId, CancellationToken ct) =>
        Task.FromResult<DiscordMembro?>(Membros.TryGetValue(discordUserId, out var c) ? new DiscordMembro(c.ToList(), Apelidos.GetValueOrDefault(discordUserId)) : null);

    public Task DefinirApelidoAsync(string discordUserId, string apelido, CancellationToken ct)
    {
        if (SemPermissaoParaApelidos) throw new DiscordApiException("O bot não tem permissão para definir o apelido de um membro.");
        Apelidos[discordUserId] = apelido;
        ApelidosDefinidos.Add((discordUserId, apelido));
        return Task.CompletedTask;
    }

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

    /// <summary>Cartões publicados (o <see cref="Avisos"/> guarda o mesmo conteúdo em texto corrido, para as conferências simples).</summary>
    public List<(string CanalId, DiscordCartao Cartao)> Cartoes { get; } = [];

    public List<(string CanalId, string MensagemId)> Fixadas { get; } = [];

    /// <summary>Só fixar mensagens falha (falta "Gerenciar mensagens").</summary>
    public bool SemPermissaoParaFixar { get; set; }

    public Task<DiscordMensagem> PublicarCartaoAsync(string canalId, DiscordCartao cartao, CancellationToken ct)
    {
        if (SemPermissao) throw new DiscordApiException("O bot não tem permissão para publicar o aviso.");
        Cartoes.Add((canalId, cartao));
        var texto = string.Join("\n", new[] { cartao.Titulo, cartao.Descricao }.Concat((cartao.Campos ?? []).Select(c => $"{c.Nome}: {c.Valor}")).Where(t => !string.IsNullOrEmpty(t)));
        Avisos.Add((canalId, texto));
        return Task.FromResult(new DiscordMensagem(Novo(), "CRM", null, texto, DateTimeOffset.UtcNow, [], false));
    }

    public Task FixarMensagemAsync(string canalId, string mensagemId, CancellationToken ct)
    {
        if (SemPermissaoParaFixar) throw new DiscordApiException("O bot não tem permissão para fixar a mensagem.");
        Fixadas.Add((canalId, mensagemId));
        return Task.CompletedTask;
    }

    /// <summary>Arquivos enviados ao chat (destino = thread ou canal).</summary>
    public List<(string CanalId, string Nome, string Texto, DiscordArquivo Arquivo)> ArquivosEnviados { get; } = [];

    public List<(string CanalId, string Nome)> TopicosCriados { get; } = [];
    public List<(string Onde, string Autor, string Pergunta, IReadOnlyList<string> Respostas, int Horas, bool Varias)> EnquetesEnviadas { get; } = [];

    public Task<string> CriarTopicoAsync(string canalId, string nome, CancellationToken ct)
    {
        if (SemPermissao) throw new DiscordApiException("O bot não tem permissão para criar o tópico.");
        TopicosCriados.Add((canalId, nome));
        return Task.FromResult($"topico-{TopicosCriados.Count}");
    }

    public Task<DiscordMensagem> EnviarEnqueteAsync(string canalOuThreadId, string autor, string pergunta, IReadOnlyList<string> respostas, int horas, bool variasEscolhas, CancellationToken ct)
    {
        if (SemPermissao) throw new DiscordApiException("O bot não tem permissão para criar a enquete.");
        EnquetesEnviadas.Add((canalOuThreadId, autor, pergunta, respostas, horas, variasEscolhas));
        var enquete = new DiscordEnquete(pergunta, respostas.Select(r => new DiscordRespostaDaEnquete(r, 0)).ToList(), variasEscolhas, DateTimeOffset.UtcNow.AddHours(horas), false);
        var mensagem = new DiscordMensagem(Novo(), "CssBrasilCRM", null, $"{autor} criou uma enquete", DateTimeOffset.UtcNow, [], false, false, true, enquete);
        if (!Mensagens.TryGetValue(canalOuThreadId, out var lista)) Mensagens[canalOuThreadId] = lista = [];
        lista.Add(mensagem);
        return Task.FromResult(mensagem);
    }

    public List<DiscordEmoji> EmojisDoServidor { get; } = [];
    public List<DiscordSticker> FigurinhasDoServidor { get; } = [];
    public List<(string CanalId, string Nome, string ImagemUrl)> ImagensEnviadas { get; } = [];

    public Task<IReadOnlyList<DiscordEmoji>> ListarEmojisAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<DiscordEmoji>>(EmojisDoServidor.ToList());

    public Task<IReadOnlyList<DiscordSticker>> ListarStickersAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<DiscordSticker>>(FigurinhasDoServidor.ToList());

    public Task<DiscordMensagem> EnviarImagemAsync(string canalId, string nome, string? fotoUrl, string imagemUrl, string descricao, CancellationToken ct, string? threadId = null)
    {
        if (SemPermissao) throw new DiscordApiException("O bot não tem permissão para enviar a figurinha.");
        var onde = threadId ?? canalId;
        ImagensEnviadas.Add((onde, nome, imagemUrl));
        var mensagem = new DiscordMensagem(Novo(), nome, fotoUrl, "", DateTimeOffset.UtcNow, [new DiscordAnexo(descricao, imagemUrl, true)], true);
        if (!Mensagens.TryGetValue(onde, out var lista)) Mensagens[onde] = lista = [];
        lista.Add(mensagem);
        return Task.FromResult(mensagem);
    }

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
