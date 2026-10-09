using CssVision.Web.Api.Contracts.Common;
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

/// <summary>Conversa 1:1: uma thread privada do Discord só das duas pessoas, aberta pelo CRM.</summary>
public class DiscordConversasDiretasTests
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

        public DiscordChatService Servico(DiscordOptions? opcoes = null) =>
            new(Db, Servidor, new MemoryCache(new MemoryCacheOptions()), Options.Create(opcoes ?? Configurado()));
    }

    private static async Task<Cenario> MontarAsync(TestDbContextFactory factory, bool sincronizar = true)
    {
        var db = factory.CreateContext();
        var mg132 = await factory.CriarRegionalAsync(db, "MG132");
        var servidor = new DiscordServidorFalso();
        if (sincronizar)
        {
            await new DiscordGruposService(db, servidor, Options.Create(Configurado()), NullLogger<DiscordGruposService>.Instance).SincronizarAsync(CancellationToken.None);
        }

        return new Cenario { Factory = factory, Db = db, Servidor = servidor, Mg132 = mg132 };
    }

    /// <summary>Usuário ativo da MG132 com o Discord vinculado (<paramref name="noServidor"/>: já entrou no servidor).</summary>
    private static async Task<ApplicationUser> PessoaAsync(Cenario c, string nome, bool vinculada = true, bool noServidor = true)
    {
        var u = await c.Factory.CriarUsuarioAsync(c.Db, nome);
        u.RegionalId = c.Mg132.Id;
        await c.Db.SaveChangesAsync();
        await c.Factory.AtribuirPapelAsync(c.Db, u, Roles.Comercial);
        if (vinculada)
        {
            c.Db.CrmDiscordVinculos.Add(new CrmDiscordVinculo
            {
                UsuarioId = u.Id, DiscordUserId = $"d-{nome.ToLowerInvariant()}", DiscordNome = nome, NoServidor = noServidor, VinculadoEm = DateTimeOffset.UtcNow,
            });
            await c.Db.SaveChangesAsync();
        }

        return u;
    }

    [Fact]
    public async Task Sincronizar_CriaOCanalDeConversas_ENaoOMostraComoGrupo()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var ana = await PessoaAsync(c, "Ana");

        var canal = await c.Db.CrmDiscordCanais.AsNoTracking().SingleAsync(x => x.Chave == DiscordGruposService.ChaveConversas);
        Assert.True(canal.Ativo);
        Assert.Contains(canal.DiscordCanalId, c.Servidor.Canais);

        var grupos = await new DiscordGruposService(c.Db, c.Servidor, Options.Create(Configurado()), NullLogger<DiscordGruposService>.Instance).ListarCanaisAsync(CancellationToken.None);
        Assert.DoesNotContain(grupos, g => g.Chave == DiscordGruposService.ChaveConversas);
        var doChat = await c.Servico().ListarCanaisAsync(ana.Id, CancellationToken.None);
        Assert.DoesNotContain(doChat, g => g.Chave == DiscordGruposService.ChaveConversas);
    }

    [Fact]
    public async Task Sincronizar_NaSegundaVez_NaoRecriaOCanalDeConversas_MasRecriaSeFoiApagado()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var grupos = new DiscordGruposService(c.Db, c.Servidor, Options.Create(Configurado()), NullLogger<DiscordGruposService>.Instance);

        var segunda = await grupos.SincronizarAsync(CancellationToken.None);
        Assert.Equal(0, segunda.CanaisCriados);

        var canal = await c.Db.CrmDiscordCanais.SingleAsync(x => x.Chave == DiscordGruposService.ChaveConversas);
        c.Servidor.Canais.Remove(canal.DiscordCanalId);
        var terceira = await grupos.SincronizarAsync(CancellationToken.None);

        Assert.Equal(1, terceira.CanaisCriados);
    }

    [Fact]
    public async Task ListarContatos_MostraQuemVinculouEEstaNoServidor_SemMimEsemInativos()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var ana = await PessoaAsync(c, "Ana");
        await PessoaAsync(c, "Bia");
        await PessoaAsync(c, "Carla", noServidor: false);
        await PessoaAsync(c, "Dani", vinculada: false);
        var eva = await PessoaAsync(c, "Eva");
        eva.Ativo = false;
        await c.Db.SaveChangesAsync();

        var contatos = await c.Servico().ListarContatosAsync(ana.Id, null, CancellationToken.None);

        Assert.Equal(["Bia"], contatos.Select(x => x.Nome).ToList());
        Assert.Equal("MG132", contatos[0].Regional);
    }

    [Fact]
    public async Task ListarContatos_FiltraPeloNomeSemDiferenciarMaiusculas()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var ana = await PessoaAsync(c, "Ana");
        await PessoaAsync(c, "Beatriz");
        await PessoaAsync(c, "Bruno");

        var contatos = await c.Servico().ListarContatosAsync(ana.Id, "  BEA ", CancellationToken.None);

        Assert.Equal(["Beatriz"], contatos.Select(x => x.Nome).ToList());
    }

    [Fact]
    public async Task IniciarConversa_CriaAThreadPrivada_AdicionaAsDuasPessoas_EAparecenaListaDosDois()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var ana = await PessoaAsync(c, "Ana");
        var bia = await PessoaAsync(c, "Bia");

        var conversa = await c.Servico().IniciarConversaAsync(ana.Id, bia.Id, CancellationToken.None);

        Assert.Equal("Bia", conversa.Nome);
        Assert.Equal("direta", conversa.Tipo);
        Assert.StartsWith("dm:", conversa.Chave);
        var thread = Assert.Single(c.Servidor.Threads);
        Assert.Equal(["d-ana", "d-bia"], thread.Value.OrderBy(x => x).ToList());
        var canalPai = await c.Db.CrmDiscordCanais.AsNoTracking().SingleAsync(x => x.Chave == DiscordGruposService.ChaveConversas);
        Assert.Equal(canalPai.DiscordCanalId, Assert.Single(c.Servidor.ThreadsCriadas).CanalPaiId);

        var listaDaAna = await c.Servico().ListarCanaisAsync(ana.Id, CancellationToken.None);
        var listaDaBia = await c.Servico().ListarCanaisAsync(bia.Id, CancellationToken.None);
        Assert.Contains(listaDaAna, x => x.Chave == conversa.Chave && x.Nome == "Bia");
        Assert.Contains(listaDaBia, x => x.Chave == conversa.Chave && x.Nome == "Ana"); // cada um vê o nome do outro
    }

    [Fact]
    public async Task IniciarConversa_ComAMesmaPessoa_ReusaAThread_NaOrdemQueForOuPeloOutroLado()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var ana = await PessoaAsync(c, "Ana");
        var bia = await PessoaAsync(c, "Bia");

        var primeira = await c.Servico().IniciarConversaAsync(ana.Id, bia.Id, CancellationToken.None);
        var segunda = await c.Servico().IniciarConversaAsync(ana.Id, bia.Id, CancellationToken.None);
        var doOutroLado = await c.Servico().IniciarConversaAsync(bia.Id, ana.Id, CancellationToken.None);

        Assert.Equal(primeira.Chave, segunda.Chave);
        Assert.Equal(primeira.Chave, doOutroLado.Chave);
        Assert.Equal("Ana", doOutroLado.Nome);
        Assert.Single(c.Servidor.ThreadsCriadas);
    }

    [Fact]
    public async Task IniciarConversa_Recusa_QuemNaoVinculouOuNaoEntrouNoServidor()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var ana = await PessoaAsync(c, "Ana");
        var semDiscord = await PessoaAsync(c, "Sem", vinculada: false);
        var fora = await PessoaAsync(c, "Fora", noServidor: false);
        var eu = await PessoaAsync(c, "Eu", vinculada: false);

        var contato = await Assert.ThrowsAsync<CrmBusinessException>(() => c.Servico().IniciarConversaAsync(ana.Id, semDiscord.Id, CancellationToken.None));
        var contatoFora = await Assert.ThrowsAsync<CrmBusinessException>(() => c.Servico().IniciarConversaAsync(ana.Id, fora.Id, CancellationToken.None));
        var voce = await Assert.ThrowsAsync<CrmBusinessException>(() => c.Servico().IniciarConversaAsync(eu.Id, ana.Id, CancellationToken.None));

        Assert.Equal("contato_sem_discord", contato.Codigo);
        Assert.Equal("contato_sem_discord", contatoFora.Codigo);
        Assert.Equal("voce_sem_discord", voce.Codigo);
        Assert.Empty(c.Servidor.ThreadsCriadas);
    }

    [Fact]
    public async Task IniciarConversa_Recusa_ConversaComSiMesmo_EComInativo()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var ana = await PessoaAsync(c, "Ana");
        var inativa = await PessoaAsync(c, "Inativa");
        inativa.Ativo = false;
        await c.Db.SaveChangesAsync();

        var consigo = await Assert.ThrowsAsync<CrmBusinessException>(() => c.Servico().IniciarConversaAsync(ana.Id, ana.Id, CancellationToken.None));
        await Assert.ThrowsAsync<CrmNotFoundException>(() => c.Servico().IniciarConversaAsync(ana.Id, inativa.Id, CancellationToken.None));

        Assert.Equal("conversa_consigo", consigo.Codigo);
    }

    [Fact]
    public async Task IniciarConversa_SemOCanalDeConversasCriado_PedeParaSincronizar()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory, sincronizar: false);
        var ana = await PessoaAsync(c, "Ana");
        var bia = await PessoaAsync(c, "Bia");

        var ex = await Assert.ThrowsAsync<CrmBusinessException>(() => c.Servico().IniciarConversaAsync(ana.Id, bia.Id, CancellationToken.None));

        Assert.Equal("conversas_nao_criadas", ex.Codigo);
    }

    [Fact]
    public async Task IniciarConversa_FalhaDoDiscordNaoDeixaConversaPelaMetade()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var ana = await PessoaAsync(c, "Ana");
        var bia = await PessoaAsync(c, "Bia");
        c.Servidor.SemPermissaoParaThreads = true;

        var ex = await Assert.ThrowsAsync<CrmBusinessException>(() => c.Servico().IniciarConversaAsync(ana.Id, bia.Id, CancellationToken.None));

        Assert.Equal("discord_indisponivel", ex.Codigo);
        Assert.Empty(await c.Db.CrmDiscordConversas.ToListAsync());
    }

    [Fact]
    public async Task Mensagens_DaConversa_LeEEscrevemNaThread_ViaWebhookDoCanalPai()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var ana = await PessoaAsync(c, "Ana");
        var bia = await PessoaAsync(c, "Bia");
        var servico = c.Servico();
        var conversa = await servico.IniciarConversaAsync(ana.Id, bia.Id, CancellationToken.None);

        await servico.EnviarAsync(ana.Id, conversa.Chave, "oi, Bia", CancellationToken.None);
        var lidaPelaBia = await servico.ListarMensagensAsync(bia.Id, conversa.Chave, null, CancellationToken.None);

        Assert.Equal(["oi, Bia"], lidaPelaBia.Mensagens.Select(m => m.Conteudo).ToList());
        var threadId = c.Servidor.Threads.Keys.Single();
        Assert.Equal(threadId, c.Servidor.Enviadas.Single().CanalId); // o falso registra a thread como destino
        Assert.Equal("Ana", c.Servidor.Enviadas.Single().Nome);
    }

    [Fact]
    public async Task Mensagens_DaConversa_NaoAbreParaTerceiros_NemParaAdmin()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var ana = await PessoaAsync(c, "Ana");
        var bia = await PessoaAsync(c, "Bia");
        var intrusa = await PessoaAsync(c, "Intrusa");
        var admin = await c.Factory.CriarUsuarioAsync(c.Db, "Admin");
        await c.Factory.AtribuirPapelAsync(c.Db, admin, Roles.Admin);
        var servico = c.Servico();
        var conversa = await servico.IniciarConversaAsync(ana.Id, bia.Id, CancellationToken.None);

        await Assert.ThrowsAsync<CrmForbiddenException>(() => servico.ListarMensagensAsync(intrusa.Id, conversa.Chave, null, CancellationToken.None));
        await Assert.ThrowsAsync<CrmForbiddenException>(() => servico.EnviarAsync(intrusa.Id, conversa.Chave, "oi", CancellationToken.None));
        await Assert.ThrowsAsync<CrmForbiddenException>(() => servico.ListarMensagensAsync(admin.Id, conversa.Chave, null, CancellationToken.None));
        await Assert.ThrowsAsync<CrmForbiddenException>(() => servico.ListarMensagensAsync(ana.Id, "dm:isso-nao-e-um-guid", null, CancellationToken.None));
        Assert.Empty(c.Servidor.Enviadas);
    }
}
