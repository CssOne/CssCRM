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

/// <summary>Chamada de voz: o CRM não embute a chamada; abre o canal de voz no Discord e avisa a conversa.</summary>
public class DiscordChamadaTests
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
        public required CrmRegional Mg134 { get; init; }
        public required IMemoryCache Cache { get; init; }

        public DiscordChatService Servico() => new(Db, Servidor, Cache, Options.Create(Configurado()));

        public DiscordGruposService Grupos() => new(Db, Servidor, Options.Create(Configurado()), NullLogger<DiscordGruposService>.Instance);
    }

    private static async Task<Cenario> MontarAsync(TestDbContextFactory factory, bool sincronizar = true)
    {
        var db = factory.CreateContext();
        var mg132 = await factory.CriarRegionalAsync(db, "MG132");
        var mg134 = await factory.CriarRegionalAsync(db, "MG134");
        var c = new Cenario
        {
            Factory = factory, Db = db, Servidor = new DiscordServidorFalso(), Mg132 = mg132, Mg134 = mg134,
            Cache = new MemoryCache(new MemoryCacheOptions()),
        };
        if (sincronizar) await c.Grupos().SincronizarAsync(CancellationToken.None);
        return c;
    }

    private static async Task<ApplicationUser> PessoaAsync(Cenario c, string nome, CrmRegional? regional = null, bool vinculada = true)
    {
        var u = await c.Factory.CriarUsuarioAsync(c.Db, nome);
        u.RegionalId = (regional ?? c.Mg132).Id;
        await c.Db.SaveChangesAsync();
        await c.Factory.AtribuirPapelAsync(c.Db, u, Roles.Comercial);
        if (vinculada)
        {
            c.Db.CrmDiscordVinculos.Add(new CrmDiscordVinculo { UsuarioId = u.Id, DiscordUserId = $"d-{nome.ToLowerInvariant()}", DiscordNome = nome, NoServidor = true, VinculadoEm = DateTimeOffset.UtcNow });
            await c.Db.SaveChangesAsync();
        }

        return u;
    }

    [Fact]
    public async Task Sincronizar_CriaUmCanalDeVozPorGrupo_VisivelSoParaOCargoDele()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);

        // geral, gestão e as duas regionais
        Assert.Equal(4, c.Servidor.VozesCriadas.Count);
        Assert.Contains(c.Servidor.VozesCriadas, v => v.Nome == "Voz · Geral");
        Assert.Contains(c.Servidor.VozesCriadas, v => v.Nome == "Voz · MG132");
        foreach (var canal in await c.Db.CrmDiscordCanais.Where(x => x.Chave != "conversas").ToListAsync())
        {
            Assert.False(string.IsNullOrEmpty(canal.DiscordVozId));
            var voz = c.Servidor.VozesCriadas.Single(v => v.Nome.EndsWith(canal.Nome));
            var permitido = Assert.Single(voz.Permitidos);
            Assert.Equal(canal.DiscordCargoId, permitido.Id);
            Assert.False(permitido.Pessoa);
        }
    }

    [Fact]
    public async Task Sincronizar_NaSegundaVezNaoRecriaAVoz_MasRecriaSeFoiApagada_ECriaAsQueFaltavam()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var segunda = await c.Grupos().SincronizarAsync(CancellationToken.None);
        Assert.Equal(0, segunda.CanaisDeVozCriados);

        var geral = await c.Db.CrmDiscordCanais.SingleAsync(x => x.Chave == "geral");
        c.Servidor.Canais.Remove(geral.DiscordVozId!);
        var terceira = await c.Grupos().SincronizarAsync(CancellationToken.None);
        Assert.Equal(1, terceira.CanaisDeVozCriados);

        // Grupo que já existia antes das chamadas (sem voz guardada) ganha a voz na próxima sincronização.
        var mg = await c.Db.CrmDiscordCanais.SingleAsync(x => x.Chave == $"regional:{c.Mg132.Id}");
        mg.DiscordVozId = null;
        await c.Db.SaveChangesAsync();
        var quarta = await c.Grupos().SincronizarAsync(CancellationToken.None);
        Assert.Equal(1, quarta.CanaisDeVozCriados);
    }

    [Fact]
    public async Task ChamadaDoGrupo_DevolveOLinkDaVozNoDiscord_EAvisaOGrupo()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var ana = await PessoaAsync(c, "Ana");
        var geral = await c.Db.CrmDiscordCanais.AsNoTracking().SingleAsync(x => x.Chave == "geral");

        var r = await c.Servico().IniciarChamadaAsync(ana.Id, "geral", CancellationToken.None);

        Assert.Equal($"https://discord.com/channels/servidor-1/{geral.DiscordVozId}", r.Url);
        Assert.True(r.Avisou);
        var aviso = Assert.Single(c.Servidor.Enviadas);
        Assert.Equal(geral.DiscordCanalId, aviso.CanalId);
        Assert.Contains("Ana está numa chamada de voz", aviso.Texto);
        Assert.Contains(r.Url, aviso.Texto);
    }

    [Fact]
    public async Task ChamadaDoGrupo_NaoRepeteOAvisoSeAPessoaClicaDeNovoLogoEmSeguida()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var ana = await PessoaAsync(c, "Ana");
        var servico = c.Servico();

        var primeira = await servico.IniciarChamadaAsync(ana.Id, "geral", CancellationToken.None);
        var segunda = await servico.IniciarChamadaAsync(ana.Id, "geral", CancellationToken.None);

        Assert.True(primeira.Avisou);
        Assert.False(segunda.Avisou);
        Assert.Equal(primeira.Url, segunda.Url); // o link vem sempre, só o aviso não se repete
        Assert.Single(c.Servidor.Enviadas);
    }

    [Fact]
    public async Task ChamadaDoGrupo_Recusa_QuemNaoParticipaDoGrupo()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var ana = await PessoaAsync(c, "Ana");

        await Assert.ThrowsAsync<CrmForbiddenException>(() =>
            c.Servico().IniciarChamadaAsync(ana.Id, $"regional:{c.Mg134.Id}", CancellationToken.None));
        Assert.Empty(c.Servidor.Enviadas);
    }

    [Fact]
    public async Task ChamadaDoGrupo_SemAVozCriada_PedeParaSincronizar()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var ana = await PessoaAsync(c, "Ana");
        var geral = await c.Db.CrmDiscordCanais.SingleAsync(x => x.Chave == "geral");
        geral.DiscordVozId = null; // grupo criado antes das chamadas existirem
        await c.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<CrmBusinessException>(() => c.Servico().IniciarChamadaAsync(ana.Id, "geral", CancellationToken.None));

        Assert.Equal("chamada_nao_criada", ex.Codigo);
    }

    [Fact]
    public async Task ChamadaDireta_CriaUmCanalDeVozPrivadoDasDuasPessoas_UmaSoVez()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var ana = await PessoaAsync(c, "Ana");
        var bia = await PessoaAsync(c, "Bia");
        var servico = c.Servico();
        var conversa = await servico.IniciarConversaAsync(ana.Id, bia.Id, CancellationToken.None);
        var antes = c.Servidor.VozesCriadas.Count;

        var daAna = await servico.IniciarChamadaAsync(ana.Id, conversa.Chave, CancellationToken.None);
        var daBia = await servico.IniciarChamadaAsync(bia.Id, conversa.Chave, CancellationToken.None);

        var nova = Assert.Single(c.Servidor.VozesCriadas.Skip(antes));
        Assert.Equal(["d-ana", "d-bia"], nova.Permitidos.Select(p => p.Id).OrderBy(x => x).ToList());
        Assert.All(nova.Permitidos, p => Assert.True(p.Pessoa)); // as pessoas, não cargos: só as duas entram
        Assert.StartsWith("voz-", nova.Nome);
        Assert.Equal(daAna.Url, daBia.Url); // as duas caem no mesmo canal
        var guardada = (await c.Db.CrmDiscordConversas.AsNoTracking().SingleAsync()).DiscordVozId;
        Assert.EndsWith(guardada!, daAna.Url);
    }

    [Fact]
    public async Task ChamadaDireta_AvisaNaThread_ERecriaOCanalSeFoiApagado()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var ana = await PessoaAsync(c, "Ana");
        var bia = await PessoaAsync(c, "Bia");
        var servico = c.Servico();
        var conversa = await servico.IniciarConversaAsync(ana.Id, bia.Id, CancellationToken.None);

        var primeira = await servico.IniciarChamadaAsync(ana.Id, conversa.Chave, CancellationToken.None);
        Assert.Contains("Ana está numa chamada de voz", c.Servidor.Enviadas.Single().Texto);
        Assert.Equal(c.Servidor.Threads.Keys.Single(), c.Servidor.Enviadas.Single().CanalId); // o aviso vai para a thread

        var voz = (await c.Db.CrmDiscordConversas.AsNoTracking().SingleAsync()).DiscordVozId!;
        c.Servidor.Canais.Remove(voz);
        var depois = await servico.IniciarChamadaAsync(bia.Id, conversa.Chave, CancellationToken.None);

        Assert.NotEqual(primeira.Url, depois.Url);
    }

    [Fact]
    public async Task ChamadaDireta_Recusa_TerceirosENemOAdmin()
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

        await Assert.ThrowsAsync<CrmForbiddenException>(() => servico.IniciarChamadaAsync(intrusa.Id, conversa.Chave, CancellationToken.None));
        await Assert.ThrowsAsync<CrmForbiddenException>(() => servico.IniciarChamadaAsync(admin.Id, conversa.Chave, CancellationToken.None));
        await Assert.ThrowsAsync<CrmForbiddenException>(() => servico.IniciarChamadaAsync(ana.Id, "dm:isso-nao-e-um-guid", CancellationToken.None));
        Assert.Empty(c.Servidor.VozesCriadas.Where(v => v.Nome.StartsWith("voz-")));
    }

    [Fact]
    public async Task ChamadaDireta_SeAlguemDesvinculouODiscord_ExplicaOMotivo()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var ana = await PessoaAsync(c, "Ana");
        var bia = await PessoaAsync(c, "Bia");
        var servico = c.Servico();
        var conversa = await servico.IniciarConversaAsync(ana.Id, bia.Id, CancellationToken.None);
        c.Db.CrmDiscordVinculos.Remove(await c.Db.CrmDiscordVinculos.SingleAsync(v => v.UsuarioId == bia.Id));
        await c.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<CrmBusinessException>(() => servico.IniciarChamadaAsync(ana.Id, conversa.Chave, CancellationToken.None));

        Assert.Equal("contato_sem_discord", ex.Codigo);
    }

    [Fact]
    public async Task Chamada_SemDiscordConfigurado_AvisaQueNaoFoiAtivado()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var ana = await PessoaAsync(c, "Ana");
        var servico = new DiscordChatService(c.Db, c.Servidor, c.Cache, Options.Create(new DiscordOptions()));

        var ex = await Assert.ThrowsAsync<CrmBusinessException>(() => servico.IniciarChamadaAsync(ana.Id, "geral", CancellationToken.None));

        Assert.Equal("discord_nao_configurado", ex.Codigo);
    }
}
