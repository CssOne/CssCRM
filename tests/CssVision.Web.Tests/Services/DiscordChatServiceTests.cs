using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Authorization;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Domain.Identity;
using CssVision.Web.Services.Discord;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Chat de texto dos grupos: quem abre cada grupo é decidido pelo CRM, as mensagens vêm e vão pelo Discord.</summary>
public class DiscordChatServiceTests
{
    private static DiscordOptions Configurado() => new()
    {
        BotToken = "bot", ClientId = "app-1", ClientSecret = "segredo", GuildId = "servidor-1", UrlPublica = "https://crm.exemplo.com/",
    };

    /// <summary>Regional MG132 com o grupo "Growth Sales", outra regional MG134 e os canais já sincronizados.</summary>
    private sealed class Cenario
    {
        public required TestDbContextFactory Factory { get; init; }
        public required ApplicationDbContext Db { get; init; }
        public required DiscordServidorFalso Servidor { get; init; }
        public required CrmRegional Mg132 { get; init; }
        public required CrmRegional Mg134 { get; init; }
        public required CrmGrupo Growth { get; init; }
        public required IMemoryCache Cache { get; init; }

        public DiscordChatService Servico(DiscordOptions? opcoes = null) => new(Db, Servidor, Cache, Options.Create(opcoes ?? Configurado()));

        public string CanalDe(string chave) => Db.CrmDiscordCanais.Single(c => c.Chave == chave).DiscordCanalId;
    }

    private static async Task<Cenario> MontarAsync(TestDbContextFactory factory)
    {
        var db = factory.CreateContext();
        var mg132 = await factory.CriarRegionalAsync(db, "MG132");
        var mg134 = await factory.CriarRegionalAsync(db, "MG134");
        var growth = await factory.CriarGrupoAsync(db, mg132.Id, "Growth Sales");
        var servidor = new DiscordServidorFalso();
        var gruposService = new DiscordGruposService(db, servidor, Options.Create(Configurado()), NullLogger<DiscordGruposService>.Instance);
        await gruposService.SincronizarAsync(CancellationToken.None);
        return new Cenario
        {
            Factory = factory, Db = db, Servidor = servidor, Mg132 = mg132, Mg134 = mg134, Growth = growth,
            Cache = new MemoryCache(new MemoryCacheOptions()),
        };
    }

    private static async Task<ApplicationUser> ConsultoraAsync(Cenario c, string nome, CrmRegional? regional = null, CrmGrupo? grupo = null, string papel = Roles.Comercial)
    {
        var u = await c.Factory.CriarUsuarioAsync(c.Db, nome);
        u.RegionalId = regional?.Id;
        u.GrupoId = grupo?.Id;
        await c.Db.SaveChangesAsync();
        await c.Factory.AtribuirPapelAsync(c.Db, u, papel);
        return u;
    }

    private static DiscordMensagem Mensagem(string id, string texto, string autor = "Ana") =>
        new(id, autor, null, texto, DateTimeOffset.UtcNow, [], false);

    [Fact]
    public async Task ListarCanaisAsync_ConsultoraVeGeralESuaRegionalESeuGrupo_ENaoOsDeOutraRegional()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var ana = await ConsultoraAsync(c, "Ana", c.Mg132, c.Growth);

        var canais = await c.Servico().ListarCanaisAsync(ana.Id, CancellationToken.None);

        Assert.Equal(["Geral", "MG132", "MG132 · Growth Sales"], canais.Select(x => x.Nome).ToList());
    }

    [Fact]
    public async Task ListarCanaisAsync_GestorComercialTambemVeGestao_ESupervisorVeTodos()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var gestora = await ConsultoraAsync(c, "Gestora", c.Mg132, papel: Roles.GestorComercial);
        var supervisor = await ConsultoraAsync(c, "Supervisor", papel: Roles.SupervisorComercial);

        var daGestora = await c.Servico().ListarCanaisAsync(gestora.Id, CancellationToken.None);
        var doSupervisor = await c.Servico().ListarCanaisAsync(supervisor.Id, CancellationToken.None);

        Assert.Contains(daGestora, x => x.Chave == "gestao");
        Assert.DoesNotContain(daGestora, x => x.Nome == "MG134");
        Assert.Equal(5, doSupervisor.Count); // geral, gestão, 2 regionais e 1 grupo
    }

    [Fact]
    public async Task ListarCanaisAsync_UsuarioInativoNaoVeNada()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var ana = await ConsultoraAsync(c, "Ana", c.Mg132);
        ana.Ativo = false;
        await c.Db.SaveChangesAsync();

        Assert.Empty(await c.Servico().ListarCanaisAsync(ana.Id, CancellationToken.None));
    }

    [Fact]
    public async Task ListarMensagensAsync_DeveRecusar_GrupoDeOutraRegional()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var ana = await ConsultoraAsync(c, "Ana", c.Mg132);

        await Assert.ThrowsAsync<CrmForbiddenException>(() =>
            c.Servico().ListarMensagensAsync(ana.Id, $"regional:{c.Mg134.Id}", null, CancellationToken.None));
    }

    [Fact]
    public async Task ListarMensagensAsync_DeveMostrarAsMensagensDoCanal()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var ana = await ConsultoraAsync(c, "Ana", c.Mg132);
        c.Servidor.Mensagens[c.CanalDe("geral")] = [Mensagem("1", "bom dia"), Mensagem("2", "bom dia, time")];

        var r = await c.Servico().ListarMensagensAsync(ana.Id, "geral", null, CancellationToken.None);

        Assert.Equal(["bom dia", "bom dia, time"], r.Mensagens.Select(m => m.Conteudo).ToList());
        Assert.False(r.ConteudoOculto);
        Assert.False(r.TemMais);
    }

    [Fact]
    public async Task ListarMensagensAsync_DeveAvisar_QuandoODiscordEntregaMensagensSemTexto()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var ana = await ConsultoraAsync(c, "Ana", c.Mg132);
        c.Servidor.Mensagens[c.CanalDe("geral")] = [Mensagem("1", ""), Mensagem("2", "")];

        var r = await c.Servico().ListarMensagensAsync(ana.Id, "geral", null, CancellationToken.None);

        Assert.True(r.ConteudoOculto);
    }

    [Fact]
    public async Task ListarMensagensAsync_VariosUsuariosNoMesmoGrupo_LeemUmaSoVezDoDiscord()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var ana = await ConsultoraAsync(c, "Ana", c.Mg132);
        var bia = await ConsultoraAsync(c, "Bia", c.Mg132);
        c.Servidor.Mensagens[c.CanalDe("geral")] = [Mensagem("1", "oi")];
        var servico = c.Servico();

        await servico.ListarMensagensAsync(ana.Id, "geral", null, CancellationToken.None);
        await servico.ListarMensagensAsync(bia.Id, "geral", null, CancellationToken.None);

        Assert.Equal(1, c.Servidor.LeiturasDeMensagens);
    }

    [Fact]
    public async Task ListarMensagensAsync_ComAntesDe_PaginaParaTrasSemUsarOCache()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var ana = await ConsultoraAsync(c, "Ana", c.Mg132);
        c.Servidor.Mensagens[c.CanalDe("geral")] = [Mensagem("1", "a"), Mensagem("2", "b"), Mensagem("3", "c")];

        var r = await c.Servico().ListarMensagensAsync(ana.Id, "geral", "3", CancellationToken.None);

        Assert.Equal(["a", "b"], r.Mensagens.Select(m => m.Conteudo).ToList());
    }

    [Fact]
    public async Task EnviarAsync_DevePublicarComONomeEAFotoDaPessoa_ELimparOCache()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var ana = await ConsultoraAsync(c, "Ana Melo", c.Mg132);
        ana.FotoUrl = "/uploads/consultores/ana.jpeg";
        await c.Db.SaveChangesAsync();
        var servico = c.Servico();
        await servico.ListarMensagensAsync(ana.Id, "geral", null, CancellationToken.None); // aquece o cache

        var enviada = await servico.EnviarAsync(ana.Id, "geral", "  oi, pessoal  ", CancellationToken.None);

        var envio = Assert.Single(c.Servidor.Enviadas);
        Assert.Equal(c.CanalDe("geral"), envio.CanalId);
        Assert.Equal("Ana Melo", envio.Nome);
        Assert.Equal("https://crm.exemplo.com/uploads/consultores/ana.jpeg", envio.FotoUrl);
        Assert.Equal("oi, pessoal", envio.Texto);
        Assert.Equal("oi, pessoal", enviada.Conteudo);

        // A leitura seguinte já traz a mensagem nova (o envio limpou o cache).
        var depois = await servico.ListarMensagensAsync(ana.Id, "geral", null, CancellationToken.None);
        Assert.Contains(depois.Mensagens, m => m.Conteudo == "oi, pessoal");
    }

    [Fact]
    public async Task EnviarAsync_DeveRecusar_TextoVazioOuLongo()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var ana = await ConsultoraAsync(c, "Ana", c.Mg132);

        var vazio = await Assert.ThrowsAsync<CrmBusinessException>(() => c.Servico().EnviarAsync(ana.Id, "geral", "   ", CancellationToken.None));
        var longo = await Assert.ThrowsAsync<CrmBusinessException>(() => c.Servico().EnviarAsync(ana.Id, "geral", new string('x', DiscordChatService.LimiteDoTexto + 1), CancellationToken.None));

        Assert.Equal("mensagem_vazia", vazio.Codigo);
        Assert.Equal("mensagem_longa", longo.Codigo);
        Assert.Empty(c.Servidor.Enviadas);
    }

    [Fact]
    public async Task EnviarAsync_DeveRecusar_GrupoQueAPessoaNaoParticipa()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var ana = await ConsultoraAsync(c, "Ana", c.Mg132);

        await Assert.ThrowsAsync<CrmForbiddenException>(() => c.Servico().EnviarAsync(ana.Id, "gestao", "oi", CancellationToken.None));
        Assert.Empty(c.Servidor.Enviadas);
    }

    [Fact]
    public async Task EnviarAsync_FalhaDoDiscord_ViraMensagemParaATela()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var ana = await ConsultoraAsync(c, "Ana", c.Mg132);
        c.Servidor.SemPermissao = true;

        var ex = await Assert.ThrowsAsync<CrmBusinessException>(() => c.Servico().EnviarAsync(ana.Id, "geral", "oi", CancellationToken.None));

        Assert.Equal("discord_indisponivel", ex.Codigo);
    }

    [Fact]
    public async Task ListarMensagensAsync_SemDiscordConfigurado_AvisaQueNaoFoiAtivado()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var ana = await ConsultoraAsync(c, "Ana", c.Mg132);

        var ex = await Assert.ThrowsAsync<CrmBusinessException>(() =>
            c.Servico(new DiscordOptions()).ListarMensagensAsync(ana.Id, "geral", null, CancellationToken.None));

        Assert.Equal("discord_nao_configurado", ex.Codigo);
    }
}
