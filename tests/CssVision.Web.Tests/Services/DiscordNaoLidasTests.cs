using CssVision.Web.Authorization;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Domain.Identity;
using CssVision.Web.Services.Discord;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Contador de não lidas do chat: o que é novidade para cada pessoa, e o que vira "lido".</summary>
public class DiscordNaoLidasTests
{
    private static DiscordOptions Configurado() => new()
    {
        BotToken = "bot", ClientId = "app-1", ClientSecret = "segredo", GuildId = "servidor-1", UrlPublica = "https://crm.exemplo.com/",
    };

    private sealed class Cenario
    {
        public required TestDbContextFactory Factory { get; init; }
        public required ApplicationDbContext Db { get; init; }
        public required DiscordServidorFalso Servidor { get; init; }
        public required CrmRegional Mg132 { get; init; }

        /// <summary>Cada chamada usa um cache novo: o "tem novidade?" real fica guardado por 20 s, e os testes avançam o tempo à mão.</summary>
        public DiscordChatService Servico(DiscordOptions? opcoes = null) =>
            new(Db, Servidor, new MemoryCache(new MemoryCacheOptions()), Options.Create(opcoes ?? Configurado()));

        public string CanalGeral() => Db.CrmDiscordCanais.Single(c => c.Chave == "geral").DiscordCanalId;
    }

    private static async Task<Cenario> MontarAsync(TestDbContextFactory factory)
    {
        var db = factory.CreateContext();
        var mg132 = await factory.CriarRegionalAsync(db, "MG132");
        var servidor = new DiscordServidorFalso();
        await new DiscordGruposService(db, servidor, Options.Create(Configurado()), NullLogger<DiscordGruposService>.Instance).SincronizarAsync(CancellationToken.None);
        return new Cenario { Factory = factory, Db = db, Servidor = servidor, Mg132 = mg132 };
    }

    private static async Task<ApplicationUser> PessoaAsync(Cenario c, string nome, bool vinculada = true)
    {
        var u = await c.Factory.CriarUsuarioAsync(c.Db, nome);
        u.RegionalId = c.Mg132.Id;
        await c.Db.SaveChangesAsync();
        await c.Factory.AtribuirPapelAsync(c.Db, u, Roles.Comercial);
        if (vinculada)
        {
            c.Db.CrmDiscordVinculos.Add(new CrmDiscordVinculo { UsuarioId = u.Id, DiscordUserId = $"d-{nome.ToLowerInvariant()}", DiscordNome = nome, NoServidor = true, VinculadoEm = DateTimeOffset.UtcNow });
            await c.Db.SaveChangesAsync();
        }

        return u;
    }

    private static void Posta(Cenario c, string canalId, params string[] ids)
    {
        if (!c.Servidor.Mensagens.TryGetValue(canalId, out var lista)) c.Servidor.Mensagens[canalId] = lista = [];
        foreach (var id in ids) lista.Add(new DiscordMensagem(id, "Alguém", null, $"msg {id}", DateTimeOffset.UtcNow, [], false));
    }

    [Fact]
    public void Maior_ComparaOsIdsComoNumeros_MesmoComTamanhosDiferentes()
    {
        Assert.True(DiscordChatService.Maior("101", "100"));
        Assert.False(DiscordChatService.Maior("100", "100"));
        Assert.True(DiscordChatService.Maior("1000", "999"));
        Assert.False(DiscordChatService.Maior("999", "1000"));
        Assert.True(DiscordChatService.Maior("5", "0"));
    }

    [Fact]
    public async Task SemMensagens_NaoHaNaoLidas()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var ana = await PessoaAsync(c, "Ana");

        var r = await c.Servico().ContarNaoLidasAsync(ana.Id, CancellationToken.None);

        Assert.Equal(0, r.Total);
        Assert.Empty(r.PorConversa);
    }

    [Fact]
    public async Task PrimeiraVezNoGrupo_OHistoricoContaComoLido_EAsMensagensNovasSaoNaoLidas()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var ana = await PessoaAsync(c, "Ana");
        Posta(c, c.CanalGeral(), "100", "101", "102");

        var primeira = await c.Servico().ContarNaoLidasAsync(ana.Id, CancellationToken.None);
        Assert.Equal(0, primeira.Total); // o passado não vira "300 não lidas"

        Posta(c, c.CanalGeral(), "103", "104");
        var depois = await c.Servico().ContarNaoLidasAsync(ana.Id, CancellationToken.None);

        Assert.Equal(2, depois.Total);
        Assert.Equal(2, depois.PorConversa["geral"]);
    }

    [Fact]
    public async Task AbrirAConversa_MarcaComoLida()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var ana = await PessoaAsync(c, "Ana");
        Posta(c, c.CanalGeral(), "100");
        await c.Servico().ContarNaoLidasAsync(ana.Id, CancellationToken.None); // inicia a marca em "100"
        Posta(c, c.CanalGeral(), "101", "102", "103");
        Assert.Equal(3, (await c.Servico().ContarNaoLidasAsync(ana.Id, CancellationToken.None)).Total);

        await c.Servico().ListarMensagensAsync(ana.Id, "geral", null, CancellationToken.None);

        Assert.Equal(0, (await c.Servico().ContarNaoLidasAsync(ana.Id, CancellationToken.None)).Total);
    }

    [Fact]
    public async Task LerAsMensagensAnteriores_NaoMarcaComoLida()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var ana = await PessoaAsync(c, "Ana");
        Posta(c, c.CanalGeral(), "100");
        await c.Servico().ContarNaoLidasAsync(ana.Id, CancellationToken.None);
        Posta(c, c.CanalGeral(), "101", "102");

        await c.Servico().ListarMensagensAsync(ana.Id, "geral", "101", CancellationToken.None); // "carregar anteriores"

        Assert.Equal(2, (await c.Servico().ContarNaoLidasAsync(ana.Id, CancellationToken.None)).Total);
    }

    [Fact]
    public async Task AMensagemQueAPropriaPessoaEnviou_NaoContaComoNaoLida()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var ana = await PessoaAsync(c, "Ana");
        Posta(c, c.CanalGeral(), "100");
        await c.Servico().ContarNaoLidasAsync(ana.Id, CancellationToken.None);

        await c.Servico().EnviarAsync(ana.Id, "geral", "oi", CancellationToken.None);

        Assert.Equal(0, (await c.Servico().ContarNaoLidasAsync(ana.Id, CancellationToken.None)).Total);
    }

    [Fact]
    public async Task CadaPessoaTemASuaMarca_ALeituraDeUmaNaoLimpaADeOutra()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var ana = await PessoaAsync(c, "Ana");
        var bia = await PessoaAsync(c, "Bia");
        Posta(c, c.CanalGeral(), "100");
        await c.Servico().ContarNaoLidasAsync(ana.Id, CancellationToken.None);
        await c.Servico().ContarNaoLidasAsync(bia.Id, CancellationToken.None);
        Posta(c, c.CanalGeral(), "101");

        await c.Servico().ListarMensagensAsync(ana.Id, "geral", null, CancellationToken.None);

        Assert.Equal(0, (await c.Servico().ContarNaoLidasAsync(ana.Id, CancellationToken.None)).Total);
        Assert.Equal(1, (await c.Servico().ContarNaoLidasAsync(bia.Id, CancellationToken.None)).Total);
    }

    [Fact]
    public async Task ConversaDireta_AMensagemDeQuemAbriuJaEhNaoLidaParaQuemFoiConvidado()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var ana = await PessoaAsync(c, "Ana");
        var bia = await PessoaAsync(c, "Bia");
        var conversa = await c.Servico().IniciarConversaAsync(ana.Id, bia.Id, CancellationToken.None);
        await c.Servico().EnviarAsync(ana.Id, conversa.Chave, "oi, Bia", CancellationToken.None);

        var daBia = await c.Servico().ContarNaoLidasAsync(bia.Id, CancellationToken.None);
        var daAna = await c.Servico().ContarNaoLidasAsync(ana.Id, CancellationToken.None);

        Assert.Equal(1, daBia.PorConversa[conversa.Chave]);
        Assert.Equal(0, daAna.Total);

        // A Bia abre e responde: some a marca dela e aparece a da Ana.
        await c.Servico().ListarMensagensAsync(bia.Id, conversa.Chave, null, CancellationToken.None);
        await c.Servico().EnviarAsync(bia.Id, conversa.Chave, "oi, Ana", CancellationToken.None);
        Assert.Equal(0, (await c.Servico().ContarNaoLidasAsync(bia.Id, CancellationToken.None)).Total);
        Assert.Equal(1, (await c.Servico().ContarNaoLidasAsync(ana.Id, CancellationToken.None)).PorConversa[conversa.Chave]);
    }

    [Fact]
    public async Task SemDiscordConfigurado_DevolveZero_SemErro()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var ana = await PessoaAsync(c, "Ana");
        Posta(c, c.CanalGeral(), "100");

        var r = await c.Servico(new DiscordOptions()).ContarNaoLidasAsync(ana.Id, CancellationToken.None);

        Assert.Equal(0, r.Total);
        Assert.Equal(0, c.Servidor.LeiturasDeMensagens);
    }

    [Fact]
    public async Task FalhaDoDiscord_NaoDerrubaOContador()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var ana = await PessoaAsync(c, "Ana");
        Posta(c, c.CanalGeral(), "100");
        c.Servidor.SemPermissao = true;

        var r = await c.Servico().ContarNaoLidasAsync(ana.Id, CancellationToken.None);

        Assert.Equal(0, r.Total);
    }

    [Fact]
    public async Task OContadorDoMenu_UsaPoucaLeituraDoDiscord_EmVariasChamadasSeguidas()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var ana = await PessoaAsync(c, "Ana");
        Posta(c, c.CanalGeral(), "100");
        var servico = c.Servico(); // mesmo cache nas três chamadas

        await servico.ContarNaoLidasAsync(ana.Id, CancellationToken.None);
        var antes = c.Servidor.LeiturasDeMensagens;
        await servico.ContarNaoLidasAsync(ana.Id, CancellationToken.None);
        await servico.ContarNaoLidasAsync(ana.Id, CancellationToken.None);

        Assert.Equal(antes, c.Servidor.LeiturasDeMensagens); // sem novidade e dentro da validade do cache: o Discord nem é consultado
    }
}
