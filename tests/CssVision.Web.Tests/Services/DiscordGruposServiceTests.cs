using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Authorization;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Discord;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Grupos da empresa no Discord: um canal + um cargo por regional/grupo, e os cargos de cada pessoa seguem o CRM.</summary>
public class DiscordGruposServiceTests
{
    private static DiscordOptions Configurado() => new()
    {
        BotToken = "bot", ClientId = "app-1", ClientSecret = "segredo", GuildId = "servidor-1", UrlPublica = "https://crm.exemplo.com/",
    };

    private static DiscordGruposService Montar(ApplicationDbContext db, DiscordServidorFalso servidor, DiscordOptions? opcoes = null) =>
        new(db, servidor, Options.Create(opcoes ?? Configurado()), NullLogger<DiscordGruposService>.Instance);

    private static async Task VincularAsync(ApplicationDbContext db, Guid usuarioId, string discordId)
    {
        db.CrmDiscordVinculos.Add(new CrmDiscordVinculo { UsuarioId = usuarioId, DiscordUserId = discordId, DiscordNome = $"conta-{discordId}", VinculadoEm = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
    }

    [Fact]
    public void Slug_DeveTirarAcentoEPontuacao()
    {
        Assert.Equal("mg132-growth-sales", DiscordGruposService.Slug("MG132 · Growth Sales"));
        Assert.Equal("gestao", DiscordGruposService.Slug("Gestão"));
        Assert.Equal("grupo", DiscordGruposService.Slug("···"));
    }

    [Fact]
    public async Task SincronizarAsync_DeveFalhar_QuandoDiscordNaoEstaConfigurado()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var servico = Montar(db, new DiscordServidorFalso(), new DiscordOptions());

        var ex = await Assert.ThrowsAsync<CrmBusinessException>(() => servico.SincronizarAsync(CancellationToken.None));
        Assert.Equal("discord_nao_configurado", ex.Codigo);
    }

    [Fact]
    public async Task SincronizarAsync_DeveCriarCanalECargoDeCadaGrupo()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var regional = await factory.CriarRegionalAsync(db, "MG132");
        await factory.CriarGrupoAsync(db, regional.Id, "Growth Sales");
        var servidor = new DiscordServidorFalso();

        var resultado = await Montar(db, servidor).SincronizarAsync(CancellationToken.None);

        // geral + gestão + 1 regional + 1 grupo
        Assert.Equal(4, resultado.CanaisCriados);
        Assert.Equal(4, resultado.CargosCriados);
        Assert.Empty(resultado.Falhas);
        var canais = await db.CrmDiscordCanais.AsNoTracking().Select(c => c.Chave).ToListAsync();
        Assert.Contains("geral", canais);
        Assert.Contains("gestao", canais);
        Assert.Contains($"regional:{regional.Id}", canais);
    }

    [Fact]
    public async Task SincronizarAsync_NaSegundaVez_NaoCriaNadaDeNovo()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        await factory.CriarRegionalAsync(db, "MG132");
        var servidor = new DiscordServidorFalso();
        var servico = Montar(db, servidor);

        await servico.SincronizarAsync(CancellationToken.None);
        var segunda = await servico.SincronizarAsync(CancellationToken.None);

        Assert.Equal(0, segunda.CanaisCriados);
        Assert.Equal(0, segunda.CargosCriados);
        Assert.Equal(3, await db.CrmDiscordCanais.CountAsync());
    }

    [Fact]
    public async Task SincronizarAsync_DeveRecriarCanalApagadoNoDiscord()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        await factory.CriarRegionalAsync(db, "MG132");
        var servidor = new DiscordServidorFalso();
        var servico = Montar(db, servidor);
        await servico.SincronizarAsync(CancellationToken.None);

        var geral = await db.CrmDiscordCanais.SingleAsync(c => c.Chave == "geral");
        servidor.Canais.Remove(geral.DiscordCanalId);

        var resultado = await servico.SincronizarAsync(CancellationToken.None);

        Assert.Equal(1, resultado.CanaisCriados);
        await db.Entry(geral).ReloadAsync();
        Assert.Contains(geral.DiscordCanalId, servidor.Canais);
    }

    [Fact]
    public async Task SincronizarAsync_DeveDarCargoGeralERegionalEGrupo_AQuemVinculou()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var regional = await factory.CriarRegionalAsync(db, "MG132");
        var grupo = await factory.CriarGrupoAsync(db, regional.Id, "Growth Sales");
        var ana = await factory.CriarUsuarioAsync(db, "Ana");
        ana.RegionalId = regional.Id;
        ana.GrupoId = grupo.Id;
        await db.SaveChangesAsync();
        await VincularAsync(db, ana.Id, "d-ana");
        var servidor = new DiscordServidorFalso();
        servidor.Membros["d-ana"] = [];

        var resultado = await Montar(db, servidor).SincronizarAsync(CancellationToken.None);

        Assert.Equal(1, resultado.MembrosAtualizados);
        var mapa = await db.CrmDiscordCanais.AsNoTracking().ToDictionaryAsync(c => c.Chave, c => c.DiscordCargoId);
        Assert.Equal(
            new[] { mapa["geral"], mapa[$"regional:{regional.Id}"], mapa[$"grupo:{grupo.Id}"] }.OrderBy(x => x),
            servidor.Membros["d-ana"].OrderBy(x => x));
        Assert.DoesNotContain(mapa["gestao"], servidor.Membros["d-ana"]);
    }

    [Fact]
    public async Task SincronizarAsync_DeveTirarOsCargosDoCrm_DeQuemFoiInativado_MantendoOsCargosDeFora()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var regional = await factory.CriarRegionalAsync(db, "MG132");
        var ana = await factory.CriarUsuarioAsync(db, "Ana");
        ana.RegionalId = regional.Id;
        await db.SaveChangesAsync();
        await VincularAsync(db, ana.Id, "d-ana");
        var servidor = new DiscordServidorFalso();
        servidor.Membros["d-ana"] = ["cargo-de-fora-do-crm"];
        var servico = Montar(db, servidor);
        await servico.SincronizarAsync(CancellationToken.None);
        Assert.Contains("cargo-de-fora-do-crm", servidor.Membros["d-ana"]);
        Assert.Equal(3, servidor.Membros["d-ana"].Count);

        ana.Ativo = false;
        await db.SaveChangesAsync();
        await servico.SincronizarAsync(CancellationToken.None);

        // Só os cargos que o CRM gerencia saem; um cargo que alguém deu à mão no Discord fica.
        Assert.Equal(["cargo-de-fora-do-crm"], servidor.Membros["d-ana"].ToList());
    }

    [Fact]
    public async Task SincronizarAsync_DeveMarcarQuemAindaNaoEntrouNoServidor()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var ana = await factory.CriarUsuarioAsync(db, "Ana");
        await VincularAsync(db, ana.Id, "d-ana");
        var servidor = new DiscordServidorFalso(); // d-ana não é membro

        var resultado = await Montar(db, servidor).SincronizarAsync(CancellationToken.None);

        Assert.Equal(1, resultado.MembrosForaDoServidor);
        Assert.False((await db.CrmDiscordVinculos.AsNoTracking().SingleAsync()).NoServidor);
    }

    [Fact]
    public async Task SincronizarAsync_DeveDarCargoDeGestao_AosGestores()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var gestora = await factory.CriarUsuarioAsync(db, "Gestora");
        await factory.AtribuirPapelAsync(db, gestora, Roles.GestorComercial);
        await VincularAsync(db, gestora.Id, "d-gestora");
        var servidor = new DiscordServidorFalso();
        servidor.Membros["d-gestora"] = [];

        await Montar(db, servidor).SincronizarAsync(CancellationToken.None);

        var gestao = await db.CrmDiscordCanais.AsNoTracking().SingleAsync(c => c.Chave == "gestao");
        Assert.Contains(gestao.DiscordCargoId, servidor.Membros["d-gestora"]);
    }

    [Fact]
    public async Task SincronizarAsync_DeveAvisarDoMotivo_QuandoOBotNaoTemPermissao()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        await factory.CriarRegionalAsync(db, "MG132");
        var servidor = new DiscordServidorFalso { SemPermissao = true };

        var resultado = await Montar(db, servidor).SincronizarAsync(CancellationToken.None);

        Assert.Equal(0, resultado.CanaisCriados);
        Assert.NotEmpty(resultado.Falhas);
        Assert.Contains("permissão", resultado.Falhas[0]);
    }

    [Fact]
    public async Task SincronizarAsync_DeveDesativarGrupoQueDeixouDeExistirNoCrm()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var regional = await factory.CriarRegionalAsync(db, "MG132");
        var grupo = await factory.CriarGrupoAsync(db, regional.Id, "Growth Sales");
        var servico = Montar(db, new DiscordServidorFalso());
        await servico.SincronizarAsync(CancellationToken.None);

        grupo.Ativo = false;
        await db.SaveChangesAsync();
        await servico.SincronizarAsync(CancellationToken.None);

        var canal = await db.CrmDiscordCanais.AsNoTracking().SingleAsync(c => c.Chave == $"grupo:{grupo.Id}");
        Assert.False(canal.Ativo);
        Assert.Contains((await servico.ListarCanaisAsync(CancellationToken.None)), c => c.Chave == canal.Chave && !c.Ativo);
    }
}
