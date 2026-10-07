using CssVision.Web.Authorization;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Domain.Identity;
using CssVision.Web.Services.Crm;
using CssVision.Web.Services.Discord;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using CssVision.Web.Api.Contracts.Common;

namespace CssVision.Web.Tests.Services;

/// <summary>Quem está online no CRM: pessoas com o CRM aberto agora (não é a presença do Discord).</summary>
public class DiscordPresencaTests
{
    private sealed class RelogioFalso : TimeProvider
    {
        public DateTimeOffset Agora { get; set; } = new(2026, 10, 7, 15, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Agora;
    }

    [Fact]
    public void Presenca_EstaOnlineSoPorUmTempoDepoisDoUltimoAviso()
    {
        var relogio = new RelogioFalso();
        var presenca = new PresencaService(relogio);
        var ana = Guid.NewGuid();

        Assert.False(presenca.EstaOnline(ana));
        presenca.Marcar(ana);
        Assert.True(presenca.EstaOnline(ana));

        relogio.Agora += PresencaService.Validade - TimeSpan.FromSeconds(1);
        Assert.True(presenca.EstaOnline(ana));

        relogio.Agora += TimeSpan.FromSeconds(2);
        Assert.False(presenca.EstaOnline(ana));
        Assert.Empty(presenca.TodosOnline());

        presenca.Marcar(ana); // voltou
        Assert.Equal([ana], presenca.TodosOnline().ToList());
    }

    [Fact]
    public void Presenca_Online_FiltraSoQuemEstaOnline()
    {
        var presenca = new PresencaService(new RelogioFalso());
        var ana = Guid.NewGuid();
        var bia = Guid.NewGuid();
        presenca.Marcar(ana);

        Assert.Equal([ana], presenca.Online([ana, bia]).ToList());
    }

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
        public required CrmRegional Mg134 { get; init; }
        public required CrmGrupo Growth { get; init; }
        public required PresencaService Presenca { get; init; }

        public DiscordChatService Servico() =>
            new(Db, Servidor, new MemoryCache(new MemoryCacheOptions()), Options.Create(Configurado()), Presenca);
    }

    private static async Task<Cenario> MontarAsync(TestDbContextFactory factory)
    {
        var db = factory.CreateContext();
        var mg132 = await factory.CriarRegionalAsync(db, "MG132");
        var mg134 = await factory.CriarRegionalAsync(db, "MG134");
        var growth = await factory.CriarGrupoAsync(db, mg132.Id, "Growth Sales");
        var servidor = new DiscordServidorFalso();
        await new DiscordGruposService(db, servidor, Options.Create(Configurado()), NullLogger<DiscordGruposService>.Instance).SincronizarAsync(CancellationToken.None);
        return new Cenario { Factory = factory, Db = db, Servidor = servidor, Mg132 = mg132, Mg134 = mg134, Growth = growth, Presenca = new PresencaService(new RelogioFalso()) };
    }

    private static async Task<ApplicationUser> PessoaAsync(Cenario c, string nome, CrmRegional? regional = null, CrmGrupo? grupo = null, string papel = Roles.Comercial, bool vinculada = true)
    {
        var u = await c.Factory.CriarUsuarioAsync(c.Db, nome);
        u.RegionalId = (regional ?? c.Mg132).Id;
        u.GrupoId = grupo?.Id;
        await c.Db.SaveChangesAsync();
        await c.Factory.AtribuirPapelAsync(c.Db, u, papel);
        if (vinculada)
        {
            c.Db.CrmDiscordVinculos.Add(new CrmDiscordVinculo { UsuarioId = u.Id, DiscordUserId = $"d-{nome.ToLowerInvariant()}", DiscordNome = nome, NoServidor = true, VinculadoEm = DateTimeOffset.UtcNow });
            await c.Db.SaveChangesAsync();
        }

        return u;
    }

    [Fact]
    public async Task Contatos_QuemEstaOnlineVemPrimeiro()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var ana = await PessoaAsync(c, "Ana");
        await PessoaAsync(c, "Bia");
        var carla = await PessoaAsync(c, "Carla");
        c.Presenca.Marcar(carla.Id);

        var contatos = await c.Servico().ListarContatosAsync(ana.Id, null, CancellationToken.None);

        Assert.Equal(["Carla", "Bia"], contatos.Select(x => x.Nome).ToList());
        Assert.True(contatos[0].Online);
        Assert.False(contatos[1].Online);
    }

    [Fact]
    public async Task ConversaDireta_MostraSeAOutraPessoaEstaOnline()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var ana = await PessoaAsync(c, "Ana");
        var bia = await PessoaAsync(c, "Bia");
        var servico = c.Servico();
        var conversa = await servico.IniciarConversaAsync(ana.Id, bia.Id, CancellationToken.None);

        Assert.False((await servico.ListarCanaisAsync(ana.Id, CancellationToken.None)).Single(x => x.Chave == conversa.Chave).Online);

        c.Presenca.Marcar(bia.Id);
        Assert.True((await servico.ListarCanaisAsync(ana.Id, CancellationToken.None)).Single(x => x.Chave == conversa.Chave).Online);
        // Cada um vê a marca da OUTRA pessoa, nunca a própria: a Bia só aparece online para a Ana se a Bia marcou presença.
        c.Presenca.Marcar(ana.Id);
        Assert.True((await servico.ListarCanaisAsync(bia.Id, CancellationToken.None)).Single(x => x.Chave == conversa.Chave).Online); // a Ana está online para a Bia
        Assert.Equal(["Bia"], (await servico.ListarOnlineAsync(ana.Id, conversa.Chave, CancellationToken.None)).Pessoas.Select(p => p.Nome).ToList());
    }

    [Fact]
    public async Task OnlineDoGrupo_SoQuemParticipaDeleEstaOnline_ESemAPropriaPessoa()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var ana = await PessoaAsync(c, "Ana", c.Mg132, c.Growth);
        var bia = await PessoaAsync(c, "Bia", c.Mg132, c.Growth);
        var carla = await PessoaAsync(c, "Carla", c.Mg132); // regional, sem o grupo Growth
        var dani = await PessoaAsync(c, "Dani", c.Mg134);   // outra regional
        foreach (var p in new[] { ana, bia, carla, dani }) c.Presenca.Marcar(p.Id);
        var servico = c.Servico();

        var geral = await servico.ListarOnlineAsync(ana.Id, "geral", CancellationToken.None);
        var daRegional = await servico.ListarOnlineAsync(ana.Id, $"regional:{c.Mg132.Id}", CancellationToken.None);
        var doGrupo = await servico.ListarOnlineAsync(ana.Id, $"grupo:{c.Growth.Id}", CancellationToken.None);

        Assert.Equal(["Bia", "Carla", "Dani"], geral.Pessoas.Select(p => p.Nome).ToList());
        Assert.Equal(["Bia", "Carla"], daRegional.Pessoas.Select(p => p.Nome).ToList());
        Assert.Equal(["Bia"], doGrupo.Pessoas.Select(p => p.Nome).ToList());
    }

    [Fact]
    public async Task OnlineDoGrupo_GestorEVisaoTotalAparecemOnde_PelasMesmasRegrasDoChat()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var ana = await PessoaAsync(c, "Ana", c.Mg132);
        var gestora = await PessoaAsync(c, "Gestora", c.Mg134, papel: Roles.GestorComercial);
        var supervisor = await PessoaAsync(c, "Supervisor", c.Mg134, papel: Roles.SupervisorComercial);
        foreach (var p in new[] { ana, gestora, supervisor }) c.Presenca.Marcar(p.Id);
        var servico = c.Servico();

        var gestao = await servico.ListarOnlineAsync(supervisor.Id, "gestao", CancellationToken.None);
        var daMg132 = await servico.ListarOnlineAsync(ana.Id, $"regional:{c.Mg132.Id}", CancellationToken.None);

        Assert.Equal(["Gestora"], gestao.Pessoas.Select(p => p.Nome).ToList()); // o supervisor (visão total) também participa, mas é quem pergunta
        Assert.Contains("Supervisor", daMg132.Pessoas.Select(p => p.Nome)); // quem tem visão total vê todos os grupos
        Assert.DoesNotContain("Gestora", daMg132.Pessoas.Select(p => p.Nome));
    }

    [Fact]
    public async Task OnlineDoGrupo_Recusa_QuemNaoParticipaDoGrupo()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var ana = await PessoaAsync(c, "Ana", c.Mg132);
        var outra = await PessoaAsync(c, "Outra", c.Mg134);
        c.Presenca.Marcar(outra.Id);

        await Assert.ThrowsAsync<CrmForbiddenException>(() => c.Servico().ListarOnlineAsync(ana.Id, $"regional:{c.Mg134.Id}", CancellationToken.None));
        await Assert.ThrowsAsync<CrmForbiddenException>(() => c.Servico().ListarOnlineAsync(ana.Id, "dm:isso-nao-e-um-guid", CancellationToken.None));
    }

    [Fact]
    public async Task Online_SemNinguemOnline_DevolveListaVazia()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var ana = await PessoaAsync(c, "Ana", c.Mg132);

        Assert.Empty((await c.Servico().ListarOnlineAsync(ana.Id, "geral", CancellationToken.None)).Pessoas);
    }
}
