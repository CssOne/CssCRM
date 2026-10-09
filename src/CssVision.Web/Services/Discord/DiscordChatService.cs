using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Authorization;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace CssVision.Web.Services.Discord;

public interface IDiscordChatService
{
    /// <summary>Conversas que a pessoa pode abrir: os grupos dela e as conversas 1:1 de que participa.</summary>
    Task<IReadOnlyList<DiscordChatCanalDto>> ListarCanaisAsync(Guid usuarioId, CancellationToken ct);

    /// <summary>Últimas mensagens da conversa (ou as anteriores a <paramref name="antesDeId"/>).</summary>
    Task<DiscordChatMensagensDto> ListarMensagensAsync(Guid usuarioId, string chave, string? antesDeId, CancellationToken ct);

    /// <summary>Publica na conversa, no Discord, com o nome e a foto da pessoa.</summary>
    Task<DiscordChatMensagemDto> EnviarAsync(Guid usuarioId, string chave, string texto, CancellationToken ct);

    /// <summary>Pessoas com quem dá para iniciar uma conversa 1:1: quem vinculou o Discord e já está no servidor.</summary>
    Task<IReadOnlyList<DiscordChatContatoDto>> ListarContatosAsync(Guid usuarioId, string? busca, CancellationToken ct);

    /// <summary>Abre a conversa 1:1 com a pessoa (cria a thread privada na primeira vez) e devolve a conversa.</summary>
    Task<DiscordChatCanalDto> IniciarConversaAsync(Guid usuarioId, Guid outraPessoaId, CancellationToken ct);

    /// <summary>
    /// Mensagens não lidas por conversa. Abrir a conversa (ler as últimas mensagens) ou enviar nela marca como lida. Sem Discord ativado,
    /// ou sem conversas, devolve zero (nunca erro): é chamado em segundo plano pelo menu.
    /// </summary>
    Task<DiscordChatNaoLidasDto> ContarNaoLidasAsync(Guid usuarioId, CancellationToken ct);

    /// <summary>
    /// Prepara a chamada de voz da conversa: devolve o endereço do canal de voz no Discord (o CRM não embute chamada) e avisa a conversa de que
    /// a pessoa está numa chamada, com o link. Grupo = o canal de voz do grupo; 1:1 = um canal de voz privado das duas pessoas, criado na primeira chamada.
    /// </summary>
    Task<DiscordChatChamadaDto> IniciarChamadaAsync(Guid usuarioId, string chave, CancellationToken ct);
}

/// <summary>
/// Chat de texto dentro do CRM: grupos da empresa e conversas 1:1. As mensagens vivem no Discord (um canal por grupo, uma thread privada
/// por conversa 1:1); o CRM só mostra e publica. Quem pode abrir cada conversa é decidido <b>aqui</b>, pelas mesmas regras que dão o cargo
/// no Discord — nunca pelo que o Discord diz. Admin, gestor master e supervisor veem todos os grupos (não as conversas 1:1 dos outros).
/// </summary>
public sealed class DiscordChatService(
    ApplicationDbContext db,
    IDiscordGuildApi api,
    IMemoryCache cache,
    IOptions<DiscordOptions> options) : IDiscordChatService
{
    public const int LimiteDoTexto = 2000;
    public const string PrefixoConversa = "dm:";
    private const int MensagensPorPagina = 50;
    private const int MaximoDeContatos = 30;

    // Todos os usuários que abrem o mesmo grupo compartilham a leitura: o Discord limita as consultas por canal, e a tela atualiza a cada poucos segundos.
    private static readonly TimeSpan ValidadeDaLeitura = TimeSpan.FromSeconds(3);

    /// <summary>Onde ler e para onde enviar uma conversa: grupo = o canal; 1:1 = a thread (lida) dentro do canal de conversas (dono do webhook).</summary>
    private sealed record Destino(string LeituraId, string CanalDoWebhook, string? ThreadId);

    private static string ChaveDeCache(string leituraId) => $"discord:chat:{leituraId}";

    // O "tem novidade?" do menu é consultado com mais folga que a conversa aberta: guarda só o id da última mensagem de cada conversa.
    private static readonly TimeSpan ValidadeDaUltima = TimeSpan.FromSeconds(20);

    private static string ChaveDaUltima(string leituraId) => $"discord:ultima:{leituraId}";

    /// <summary>Os ids do Discord são números que crescem com o tempo: com o mesmo tamanho, a comparação do texto vale a do número.</summary>
    internal static bool Maior(string a, string b) => a.Length != b.Length ? a.Length > b.Length : string.CompareOrdinal(a, b) > 0;

    public async Task<IReadOnlyList<DiscordChatCanalDto>> ListarCanaisAsync(Guid usuarioId, CancellationToken ct)
    {
        var grupos = (await CanaisDaPessoaAsync(usuarioId, ct))
            .OrderBy(c => c.Chave == DiscordGruposService.ChaveGeral ? 0 : c.Chave == DiscordGruposService.ChaveGestao ? 1 : 2)
            .ThenBy(c => c.Nome)
            .Select(c => new DiscordChatCanalDto(c.Chave, c.Nome))
            .ToList();

        var conversas = await db.CrmDiscordConversas.AsNoTracking()
            .Where(c => c.UsuarioAId == usuarioId || c.UsuarioBId == usuarioId)
            .Select(c => new { c.Id, OutraId = c.UsuarioAId == usuarioId ? c.UsuarioBId : c.UsuarioAId })
            .ToListAsync(ct);
        var outrasIds = conversas.Select(c => c.OutraId).ToList();
        var pessoas = await db.Users.AsNoTracking().Where(u => outrasIds.Contains(u.Id)).Select(u => new { u.Id, u.NomeCompleto, u.FotoUrl }).ToDictionaryAsync(u => u.Id, ct);

        var diretas = conversas
            .Where(c => pessoas.ContainsKey(c.OutraId))
            .Select(c => new DiscordChatCanalDto($"{PrefixoConversa}{c.Id}", pessoas[c.OutraId].NomeCompleto, "direta", FotoAbsoluta(pessoas[c.OutraId].FotoUrl)))
            .OrderBy(c => c.Nome, StringComparer.OrdinalIgnoreCase);

        return grupos.Concat(diretas).ToList();
    }

    public async Task<DiscordChatMensagensDto> ListarMensagensAsync(Guid usuarioId, string chave, string? antesDeId, CancellationToken ct)
    {
        var destino = await ObterDestinoPermitidoAsync(usuarioId, chave, ct);

        IReadOnlyList<DiscordMensagem> mensagens;
        if (antesDeId is null)
        {
            mensagens = await LerRecentesAsync(destino, ct);
            // Quem abre a conversa (e a deixa aberta, atualizando) está lendo: tudo até a última mensagem fica como lido.
            if (mensagens.Count > 0) await MarcarComoLidaAsync(usuarioId, chave, mensagens[^1].Id, ct);
        }
        else
        {
            mensagens = await LerAsync(() => api.ListarMensagensAsync(destino.LeituraId, MensagensPorPagina, antesDeId, ct));
        }

        // O Discord só entrega o texto das mensagens a um bot que tenha a permissão "Message Content Intent" ligada no portal do desenvolvedor.
        // Sem ela as mensagens chegam sem texto — a tela avisa o administrador em vez de mostrar balões vazios.
        var conteudoOculto = mensagens.Count > 0 && mensagens.All(m => m.Conteudo.Length == 0 && m.Anexos.Count == 0);

        return new DiscordChatMensagensDto(
            mensagens.Select(Converter).ToList(),
            TemMais: mensagens.Count >= MensagensPorPagina,
            ConteudoOculto: conteudoOculto);
    }

    public async Task<DiscordChatMensagemDto> EnviarAsync(Guid usuarioId, string chave, string texto, CancellationToken ct)
    {
        texto = (texto ?? "").Trim();
        if (texto.Length == 0)
        {
            throw new CrmBusinessException("Escreva uma mensagem.", "mensagem_vazia");
        }

        if (texto.Length > LimiteDoTexto)
        {
            throw new CrmBusinessException($"A mensagem pode ter no máximo {LimiteDoTexto} caracteres.", "mensagem_longa");
        }

        var destino = await ObterDestinoPermitidoAsync(usuarioId, chave, ct);
        var pessoa = await db.Users.AsNoTracking().Where(u => u.Id == usuarioId).Select(u => new { u.NomeCompleto, u.FotoUrl }).FirstAsync(ct);

        var enviada = await LerAsync(async () => await api.EnviarMensagemAsync(destino.CanalDoWebhook, pessoa.NomeCompleto, FotoAbsoluta(pessoa.FotoUrl), texto, ct, destino.ThreadId));
        cache.Remove(ChaveDeCache(destino.LeituraId));
        cache.Remove(ChaveDaUltima(destino.LeituraId));
        await MarcarComoLidaAsync(usuarioId, chave, enviada.Id, ct); // a mensagem que a própria pessoa mandou nunca é "não lida" para ela
        return Converter(enviada);
    }

    public async Task<DiscordChatNaoLidasDto> ContarNaoLidasAsync(Guid usuarioId, CancellationToken ct)
    {
        var vazio = new DiscordChatNaoLidasDto(0, new Dictionary<string, int>());
        if (!options.Value.Configurado) return vazio;

        var destinos = new List<(string Chave, Destino Destino)>();
        foreach (var canal in await CanaisDaPessoaAsync(usuarioId, ct)) destinos.Add((canal.Chave, new Destino(canal.DiscordCanalId, canal.DiscordCanalId, null)));

        var conversas = await db.CrmDiscordConversas.AsNoTracking().Where(c => c.UsuarioAId == usuarioId || c.UsuarioBId == usuarioId).ToListAsync(ct);
        foreach (var c in conversas) destinos.Add(($"{PrefixoConversa}{c.Id}", new Destino(c.DiscordThreadId, "", c.DiscordThreadId)));
        if (destinos.Count == 0) return vazio;

        var chaves = destinos.Select(d => d.Chave).ToList();
        var lidas = await db.CrmDiscordLeituras.Where(l => l.UsuarioId == usuarioId && chaves.Contains(l.Chave)).ToDictionaryAsync(l => l.Chave, ct);

        var porConversa = new Dictionary<string, int>();
        var alterou = false;
        foreach (var (chave, destino) in destinos)
        {
            string? ultima;
            try
            {
                ultima = await UltimaMensagemIdAsync(destino, ct);
            }
            catch (CrmBusinessException ex) when (ex.Codigo == "discord_indisponivel")
            {
                continue; // uma conversa que o Discord não entregou agora não derruba o contador das outras
            }

            if (ultima is null) continue; // conversa sem mensagens

            if (!lidas.TryGetValue(chave, out var leitura))
            {
                // Primeira vez que esta pessoa aparece nesta conversa. Grupo: o passado conta como lido (senão todo o histórico viraria "não lido").
                // Conversa 1:1: nada foi lido ainda — as mensagens de quem a abriu são novidade para quem acabou de ser convidado.
                var inicial = chave.StartsWith(PrefixoConversa, StringComparison.Ordinal) ? "0" : ultima;
                leitura = new CrmDiscordLeitura { UsuarioId = usuarioId, Chave = chave, UltimaLidaId = inicial };
                db.CrmDiscordLeituras.Add(leitura);
                lidas[chave] = leitura;
                alterou = true;
            }

            if (!Maior(ultima, leitura.UltimaLidaId)) continue;

            // Há novidade: conta olhando as mensagens recentes (a mesma leitura que a conversa aberta usa, guardada por poucos segundos).
            IReadOnlyList<DiscordMensagem> recentes;
            try
            {
                recentes = await LerRecentesAsync(destino, ct);
            }
            catch (CrmBusinessException ex) when (ex.Codigo == "discord_indisponivel")
            {
                continue;
            }

            porConversa[chave] = Math.Max(1, recentes.Count(m => Maior(m.Id, leitura.UltimaLidaId)));
        }

        if (alterou) await db.SaveChangesAsync(ct);
        return new DiscordChatNaoLidasDto(porConversa.Values.Sum(), porConversa);
    }

    // Clicar duas vezes em "Chamada" não deve encher a conversa de avisos iguais.
    private static readonly TimeSpan IntervaloEntreAvisos = TimeSpan.FromMinutes(5);

    public async Task<DiscordChatChamadaDto> IniciarChamadaAsync(Guid usuarioId, string chave, CancellationToken ct)
    {
        ExigirConfigurado();
        var url = await ObterUrlDaVozAsync(usuarioId, chave, ct);

        var chaveDoAviso = $"discord:chamada:{usuarioId}:{chave}";
        var avisar = !cache.TryGetValue(chaveDoAviso, out _);
        if (avisar)
        {
            var nome = await db.Users.AsNoTracking().Where(u => u.Id == usuarioId).Select(u => u.NomeCompleto).FirstAsync(ct);
            await EnviarAsync(usuarioId, chave, $"📞 {nome} está numa chamada de voz. Entre: {url}", ct);
            cache.Set(chaveDoAviso, true, IntervaloEntreAvisos);
        }

        return new DiscordChatChamadaDto(url, avisar);
    }

    private async Task<string> ObterUrlDaVozAsync(Guid usuarioId, string chave, CancellationToken ct)
    {
        string? vozId;
        if (chave.StartsWith(PrefixoConversa, StringComparison.Ordinal))
        {
            vozId = await ObterVozDaConversaAsync(usuarioId, chave, ct);
        }
        else
        {
            var canal = (await CanaisDaPessoaAsync(usuarioId, ct)).FirstOrDefault(c => c.Chave == chave)
                ?? throw new CrmForbiddenException("Você não tem acesso a esta conversa.");
            vozId = canal.DiscordVozId;
            if (string.IsNullOrEmpty(vozId) || !await LerAsync(() => api.CanalExisteAsync(vozId, ct)))
            {
                throw new CrmBusinessException("A chamada deste grupo ainda não foi criada no Discord. Peça a um administrador para sincronizar os grupos na página do Discord.", "chamada_nao_criada");
            }
        }

        return $"https://discord.com/channels/{options.Value.GuildId}/{vozId}";
    }

    /// <summary>Canal de voz privado das duas pessoas da conversa: cria na primeira chamada e recria se alguém o apagou no Discord.</summary>
    private async Task<string> ObterVozDaConversaAsync(Guid usuarioId, string chave, CancellationToken ct)
    {
        if (!Guid.TryParse(chave[PrefixoConversa.Length..], out var conversaId))
        {
            throw new CrmForbiddenException("Você não tem acesso a esta conversa.");
        }

        var conversa = await db.CrmDiscordConversas
            .FirstOrDefaultAsync(c => c.Id == conversaId && (c.UsuarioAId == usuarioId || c.UsuarioBId == usuarioId), ct)
            ?? throw new CrmForbiddenException("Você não tem acesso a esta conversa.");

        if (conversa.DiscordVozId is { Length: > 0 } existente && await LerAsync(() => api.CanalExisteAsync(existente, ct))) return existente;

        var vinculos = await db.CrmDiscordVinculos.AsNoTracking().Where(v => v.UsuarioId == conversa.UsuarioAId || v.UsuarioId == conversa.UsuarioBId).ToListAsync(ct);
        var a = vinculos.FirstOrDefault(v => v.UsuarioId == conversa.UsuarioAId);
        var b = vinculos.FirstOrDefault(v => v.UsuarioId == conversa.UsuarioBId);
        if (a is null || b is null)
        {
            throw new CrmBusinessException("Uma das pessoas desta conversa não tem mais o Discord vinculado, então a chamada não pode ser criada.", "contato_sem_discord");
        }

        var categoria = await db.CrmParametros.AsNoTracking().Where(p => p.Chave == DiscordGruposService.ChaveCategoria).Select(p => p.Valor).FirstOrDefaultAsync(ct)
            ?? throw new CrmBusinessException("Os grupos ainda não foram criados no Discord. Peça a um administrador para sincronizar os grupos na página do Discord.", "conversas_nao_criadas");

        var nomes = await db.Users.AsNoTracking().Where(u => u.Id == conversa.UsuarioAId || u.Id == conversa.UsuarioBId).Select(u => new { u.Id, u.NomeCompleto }).ToListAsync(ct);
        var nomeDoCanal = $"voz-{DiscordGruposService.Slug(nomes.First(n => n.Id == conversa.UsuarioAId).NomeCompleto)}-{DiscordGruposService.Slug(nomes.First(n => n.Id == conversa.UsuarioBId).NomeCompleto)}";

        var vozId = await LerAsync(() => api.CriarCanalDeVozAsync(nomeDoCanal, categoria, [new DiscordPermitido(a.DiscordUserId, Pessoa: true), new DiscordPermitido(b.DiscordUserId, Pessoa: true)], ct));
        conversa.DiscordVozId = vozId;
        await db.SaveChangesAsync(ct);
        return vozId;
    }

    private async Task<IReadOnlyList<DiscordMensagem>> LerRecentesAsync(Destino destino, CancellationToken ct) =>
        await cache.GetOrCreateAsync(ChaveDeCache(destino.LeituraId), async entrada =>
        {
            entrada.AbsoluteExpirationRelativeToNow = ValidadeDaLeitura;
            return await LerAsync(() => api.ListarMensagensAsync(destino.LeituraId, MensagensPorPagina, null, ct));
        }) ?? [];

    private async Task<string?> UltimaMensagemIdAsync(Destino destino, CancellationToken ct) =>
        await cache.GetOrCreateAsync(ChaveDaUltima(destino.LeituraId), async entrada =>
        {
            entrada.AbsoluteExpirationRelativeToNow = ValidadeDaUltima;
            var ultima = await LerAsync(() => api.ListarMensagensAsync(destino.LeituraId, 1, null, ct));
            return ultima.Count == 0 ? null : ultima[^1].Id;
        });

    /// <summary>Guarda "li até aqui" (só avança, nunca volta atrás).</summary>
    private async Task MarcarComoLidaAsync(Guid usuarioId, string chave, string ultimaId, CancellationToken ct)
    {
        var leitura = await db.CrmDiscordLeituras.FirstOrDefaultAsync(l => l.UsuarioId == usuarioId && l.Chave == chave, ct);
        if (leitura is null)
        {
            db.CrmDiscordLeituras.Add(new CrmDiscordLeitura { UsuarioId = usuarioId, Chave = chave, UltimaLidaId = ultimaId });
        }
        else if (Maior(ultimaId, leitura.UltimaLidaId))
        {
            leitura.UltimaLidaId = ultimaId;
        }
        else
        {
            return;
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Duas leituras ao mesmo tempo criaram a marca juntas: a outra já guardou — não vale derrubar a conversa por isso.
        }
    }

    public async Task<IReadOnlyList<DiscordChatContatoDto>> ListarContatosAsync(Guid usuarioId, string? busca, CancellationToken ct)
    {
        var consulta = db.CrmDiscordVinculos.AsNoTracking().Where(v => v.NoServidor && v.UsuarioId != usuarioId)
            .Join(db.Users.AsNoTracking().Where(u => u.Ativo), v => v.UsuarioId, u => u.Id,
                (_, u) => new { u.Id, u.NomeCompleto, u.FotoUrl, RegionalNome = u.Regional != null ? u.Regional.Nome : null });

        if (!string.IsNullOrWhiteSpace(busca))
        {
            var termo = busca.Trim().ToLower();
            consulta = consulta.Where(u => u.NomeCompleto.ToLower().Contains(termo));
        }

        return (await consulta.OrderBy(u => u.NomeCompleto).Take(MaximoDeContatos).ToListAsync(ct))
            .Select(u => new DiscordChatContatoDto(u.Id, u.NomeCompleto, FotoAbsoluta(u.FotoUrl), u.RegionalNome))
            .ToList();
    }

    public async Task<DiscordChatCanalDto> IniciarConversaAsync(Guid usuarioId, Guid outraPessoaId, CancellationToken ct)
    {
        ExigirConfigurado();
        if (outraPessoaId == usuarioId)
        {
            throw new CrmBusinessException("Escolha outra pessoa para conversar.", "conversa_consigo");
        }

        var outra = await db.Users.AsNoTracking().Where(u => u.Id == outraPessoaId && u.Ativo).Select(u => new { u.NomeCompleto, u.FotoUrl }).FirstOrDefaultAsync(ct)
            ?? throw new CrmNotFoundException("Usuário", outraPessoaId);

        var (a, b) = usuarioId.CompareTo(outraPessoaId) < 0 ? (usuarioId, outraPessoaId) : (outraPessoaId, usuarioId);
        var existente = await db.CrmDiscordConversas.AsNoTracking().FirstOrDefaultAsync(c => c.UsuarioAId == a && c.UsuarioBId == b, ct);
        if (existente is not null)
        {
            return new DiscordChatCanalDto($"{PrefixoConversa}{existente.Id}", outra.NomeCompleto, "direta", FotoAbsoluta(outra.FotoUrl));
        }

        var vinculos = await db.CrmDiscordVinculos.AsNoTracking().Where(v => v.UsuarioId == usuarioId || v.UsuarioId == outraPessoaId).ToListAsync(ct);
        var meu = vinculos.FirstOrDefault(v => v.UsuarioId == usuarioId);
        var dele = vinculos.FirstOrDefault(v => v.UsuarioId == outraPessoaId);
        if (meu is null || !meu.NoServidor)
        {
            throw new CrmBusinessException("Vincule a sua conta do Discord (página Discord) e entre no servidor da empresa para conversar.", "voce_sem_discord");
        }

        if (dele is null || !dele.NoServidor)
        {
            throw new CrmBusinessException($"{outra.NomeCompleto} ainda não vinculou o Discord (ou não entrou no servidor da empresa).", "contato_sem_discord");
        }

        var canalDeConversas = await db.CrmDiscordCanais.AsNoTracking().FirstOrDefaultAsync(c => c.Chave == DiscordGruposService.ChaveConversas && c.Ativo, ct)
            ?? throw new CrmBusinessException("As conversas diretas ainda não foram criadas no Discord. Peça a um administrador para sincronizar os grupos na página do Discord.", "conversas_nao_criadas");

        var nomes = await db.Users.AsNoTracking().Where(u => u.Id == usuarioId || u.Id == outraPessoaId).Select(u => new { u.Id, u.NomeCompleto }).ToListAsync(ct);
        var nomeDaThread = $"dm-{DiscordGruposService.Slug(nomes.First(n => n.Id == a).NomeCompleto)}-{DiscordGruposService.Slug(nomes.First(n => n.Id == b).NomeCompleto)}";

        var conversa = await LerAsync(async () =>
        {
            var threadId = await api.CriarConversaPrivadaAsync(canalDeConversas.DiscordCanalId, nomeDaThread, ct);
            await api.AdicionarAThreadAsync(threadId, meu.DiscordUserId, ct);
            await api.AdicionarAThreadAsync(threadId, dele.DiscordUserId, ct);
            return new CrmDiscordConversa { UsuarioAId = a, UsuarioBId = b, DiscordThreadId = threadId };
        });

        db.CrmDiscordConversas.Add(conversa);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Os dois clicaram ao mesmo tempo: vale a conversa que entrou primeiro.
            db.Entry(conversa).State = EntityState.Detached;
            conversa = await db.CrmDiscordConversas.AsNoTracking().FirstAsync(c => c.UsuarioAId == a && c.UsuarioBId == b, ct);
        }

        return new DiscordChatCanalDto($"{PrefixoConversa}{conversa.Id}", outra.NomeCompleto, "direta", FotoAbsoluta(outra.FotoUrl));
    }

    private void ExigirConfigurado()
    {
        if (!options.Value.Configurado)
        {
            throw new CrmBusinessException("A integração com o Discord ainda não foi configurada no servidor.", "discord_nao_configurado");
        }
    }

    private async Task<Destino> ObterDestinoPermitidoAsync(Guid usuarioId, string chave, CancellationToken ct)
    {
        ExigirConfigurado();

        if (chave.StartsWith(PrefixoConversa, StringComparison.Ordinal))
        {
            // Conversa 1:1: só as duas pessoas dela abrem (nem admin).
            if (!Guid.TryParse(chave[PrefixoConversa.Length..], out var conversaId))
            {
                throw new CrmForbiddenException("Você não tem acesso a esta conversa.");
            }

            var conversa = await db.CrmDiscordConversas.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == conversaId && (c.UsuarioAId == usuarioId || c.UsuarioBId == usuarioId), ct)
                ?? throw new CrmForbiddenException("Você não tem acesso a esta conversa.");
            var pai = await db.CrmDiscordCanais.AsNoTracking().FirstOrDefaultAsync(c => c.Chave == DiscordGruposService.ChaveConversas, ct)
                ?? throw new CrmBusinessException("As conversas diretas ainda não foram criadas no Discord.", "conversas_nao_criadas");
            return new Destino(conversa.DiscordThreadId, pai.DiscordCanalId, conversa.DiscordThreadId);
        }

        var canal = (await CanaisDaPessoaAsync(usuarioId, ct)).FirstOrDefault(c => c.Chave == chave)
            ?? throw new CrmForbiddenException("Você não tem acesso a esta conversa.");
        return new Destino(canal.DiscordCanalId, canal.DiscordCanalId, null);
    }

    /// <summary>Canais de grupo ativos que a pessoa pode abrir: "geral" para todos, "gestão" para gestores, o da regional e o do grupo dela; visão total vê todos.</summary>
    private async Task<List<CrmDiscordCanal>> CanaisDaPessoaAsync(Guid usuarioId, CancellationToken ct)
    {
        var pessoa = await db.Users.AsNoTracking().Where(u => u.Id == usuarioId).Select(u => new { u.Ativo, u.RegionalId, u.GrupoId }).FirstOrDefaultAsync(ct);
        if (pessoa is null || !pessoa.Ativo) return [];

        var papeis = await db.UserRoles.Where(ur => ur.UserId == usuarioId)
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (_, r) => r.Name!).ToListAsync(ct);
        var canais = await db.CrmDiscordCanais.AsNoTracking().Where(c => c.Ativo && c.Chave != DiscordGruposService.ChaveConversas).ToListAsync(ct);

        if (papeis.Any(p => Roles.VisaoTotal.Contains(p))) return canais;

        var permitidas = new HashSet<string> { DiscordGruposService.ChaveGeral };
        if (papeis.Any(p => Roles.GestaoComercial.Contains(p))) permitidas.Add(DiscordGruposService.ChaveGestao);
        if (pessoa.RegionalId is { } regional) permitidas.Add($"regional:{regional}");
        if (pessoa.GrupoId is { } grupo) permitidas.Add($"grupo:{grupo}");
        return canais.Where(c => permitidas.Contains(c.Chave)).ToList();
    }

    /// <summary>Falha do Discord vira mensagem para a tela (sem derrubar a página com erro 500).</summary>
    private static async Task<T> LerAsync<T>(Func<Task<T>> chamada)
    {
        try
        {
            return await chamada();
        }
        catch (DiscordApiException ex)
        {
            throw new CrmBusinessException(ex.Message, "discord_indisponivel");
        }
    }

    /// <summary>O Discord só aceita foto por endereço completo; as fotos do CRM guardadas com caminho relativo ganham o endereço público.</summary>
    private string? FotoAbsoluta(string? foto)
    {
        if (string.IsNullOrWhiteSpace(foto)) return null;
        if (foto.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return foto;
        var baseUrl = options.Value.UrlPublica.TrimEnd('/');
        return baseUrl.Length == 0 ? null : $"{baseUrl}/{foto.TrimStart('/')}";
    }

    private static DiscordChatMensagemDto Converter(DiscordMensagem m) =>
        new(m.Id, m.AutorNome, m.AutorFotoUrl, m.Conteudo, m.CriadaEm, m.Anexos.Select(a => new DiscordChatAnexoDto(a.Nome, a.Url, a.Imagem)).ToList(), m.DoCrm);
}
