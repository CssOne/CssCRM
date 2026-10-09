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
    /// <summary>Grupos de conversa que a pessoa pode abrir (os canais criados pela sincronização dos grupos).</summary>
    Task<IReadOnlyList<DiscordChatCanalDto>> ListarCanaisAsync(Guid usuarioId, CancellationToken ct);

    /// <summary>Últimas mensagens do grupo (ou as anteriores a <paramref name="antesDeId"/>).</summary>
    Task<DiscordChatMensagensDto> ListarMensagensAsync(Guid usuarioId, string chave, string? antesDeId, CancellationToken ct);

    /// <summary>Publica no grupo, no Discord, com o nome e a foto da pessoa.</summary>
    Task<DiscordChatMensagemDto> EnviarAsync(Guid usuarioId, string chave, string texto, CancellationToken ct);
}

/// <summary>
/// Chat de texto dos grupos dentro do CRM. As mensagens vivem no Discord (um canal por grupo, criado pela sincronização dos grupos); o CRM
/// só mostra e publica. Quem pode abrir cada grupo é decidido <b>aqui</b>, pelas mesmas regras que dão o cargo no Discord — nunca pelo que o
/// Discord diz. Admin, gestor master e supervisor veem todos os grupos.
/// </summary>
public sealed class DiscordChatService(
    ApplicationDbContext db,
    IDiscordGuildApi api,
    IMemoryCache cache,
    IOptions<DiscordOptions> options) : IDiscordChatService
{
    public const int LimiteDoTexto = 2000;
    private const int MensagensPorPagina = 50;

    // Todos os usuários que abrem o mesmo grupo compartilham a leitura: o Discord limita as consultas por canal, e a tela atualiza a cada poucos segundos.
    private static readonly TimeSpan ValidadeDaLeitura = TimeSpan.FromSeconds(3);

    private static string ChaveDeCache(string canalId) => $"discord:chat:{canalId}";

    public async Task<IReadOnlyList<DiscordChatCanalDto>> ListarCanaisAsync(Guid usuarioId, CancellationToken ct)
    {
        var canais = await CanaisDaPessoaAsync(usuarioId, ct);
        return canais.OrderBy(c => c.Chave == DiscordGruposService.ChaveGeral ? 0 : c.Chave == DiscordGruposService.ChaveGestao ? 1 : 2)
            .ThenBy(c => c.Nome)
            .Select(c => new DiscordChatCanalDto(c.Chave, c.Nome))
            .ToList();
    }

    public async Task<DiscordChatMensagensDto> ListarMensagensAsync(Guid usuarioId, string chave, string? antesDeId, CancellationToken ct)
    {
        var canal = await ObterCanalPermitidoAsync(usuarioId, chave, ct);

        IReadOnlyList<DiscordMensagem> mensagens;
        if (antesDeId is null)
        {
            mensagens = await cache.GetOrCreateAsync(ChaveDeCache(canal.DiscordCanalId), async entrada =>
            {
                entrada.AbsoluteExpirationRelativeToNow = ValidadeDaLeitura;
                return await LerAsync(() => api.ListarMensagensAsync(canal.DiscordCanalId, MensagensPorPagina, null, ct));
            }) ?? [];
        }
        else
        {
            mensagens = await LerAsync(() => api.ListarMensagensAsync(canal.DiscordCanalId, MensagensPorPagina, antesDeId, ct));
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

        var canal = await ObterCanalPermitidoAsync(usuarioId, chave, ct);
        var pessoa = await db.Users.AsNoTracking().Where(u => u.Id == usuarioId).Select(u => new { u.NomeCompleto, u.FotoUrl }).FirstAsync(ct);

        var enviada = await LerAsync(async () => await api.EnviarMensagemAsync(canal.DiscordCanalId, pessoa.NomeCompleto, FotoAbsoluta(pessoa.FotoUrl), texto, ct));
        cache.Remove(ChaveDeCache(canal.DiscordCanalId));
        return Converter(enviada);
    }

    private async Task<CrmDiscordCanal> ObterCanalPermitidoAsync(Guid usuarioId, string chave, CancellationToken ct)
    {
        if (!options.Value.Configurado)
        {
            throw new CrmBusinessException("A integração com o Discord ainda não foi configurada no servidor.", "discord_nao_configurado");
        }

        var canal = (await CanaisDaPessoaAsync(usuarioId, ct)).FirstOrDefault(c => c.Chave == chave);
        return canal ?? throw new CrmForbiddenException("Você não tem acesso a esta conversa.");
    }

    /// <summary>Canais ativos que a pessoa pode abrir: "geral" para todos, "gestão" para gestores, o da regional e o do grupo dela; visão total vê todos.</summary>
    private async Task<List<CrmDiscordCanal>> CanaisDaPessoaAsync(Guid usuarioId, CancellationToken ct)
    {
        var pessoa = await db.Users.AsNoTracking().Where(u => u.Id == usuarioId).Select(u => new { u.Ativo, u.RegionalId, u.GrupoId }).FirstOrDefaultAsync(ct);
        if (pessoa is null || !pessoa.Ativo) return [];

        var papeis = await db.UserRoles.Where(ur => ur.UserId == usuarioId)
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (_, r) => r.Name!).ToListAsync(ct);
        var canais = await db.CrmDiscordCanais.AsNoTracking().Where(c => c.Ativo).ToListAsync(ct);

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
