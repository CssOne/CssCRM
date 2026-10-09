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

/// <summary>Apagar e arquivar canais extras, e criar canais de voz extras.</summary>
public class DiscordApagarEVozExtraTests
{
    private static DiscordOptions Configurado() => new()
    {
        BotToken = "bot", ClientId = "app-1", ClientSecret = "segredo", GuildId = "servidor-1", UrlPublica = "https://crm.exemplo.com/",
    };

    private sealed class Cenario
    {
        public required ApplicationDbContext Db { get; init; }
        public required DiscordServidorFalso Servidor { get; init; }
        public required ApplicationUser Ana { get; init; }
        public DiscordGruposService Grupos() => new(Db, Servidor, Options.Create(Configurado()), NullLogger<DiscordGruposService>.Instance);
        public DiscordChatService Chat() => new(Db, Servidor, new MemoryCache(new MemoryCacheOptions()), Options.Create(Configurado()));
    }

    private static async Task<Cenario> MontarAsync(TestDbContextFactory factory)
    {
        var db = factory.CreateContext();
        var regional = await factory.CriarRegionalAsync(db, "MG132");
        var servidor = new DiscordServidorFalso();
        await new DiscordGruposService(db, servidor, Options.Create(Configurado()), NullLogger<DiscordGruposService>.Instance).SincronizarAsync(CancellationToken.None);
        var ana = await factory.CriarUsuarioAsync(db, "Ana");
        ana.RegionalId = regional.Id;
        await db.SaveChangesAsync();
        await factory.AtribuirPapelAsync(db, ana, Roles.Comercial);
        return new Cenario { Db = db, Servidor = servidor, Ana = ana };
    }

    [Fact]
    public async Task Apagar_ComONomeCerto_ApagaNoDiscordENoCrm_ELimpaReacoesELeituras()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var criado = await c.Grupos().CriarCanalAsync("Avisos", "geral", null, CancellationToken.None);
        var discordId = (await c.Db.CrmDiscordCanais.AsNoTracking().SingleAsync(x => x.Chave == criado.Chave)).DiscordCanalId;
        c.Db.CrmDiscordReacoes.Add(new CrmDiscordReacao { MensagemId = "1", LeituraId = discordId, UsuarioId = c.Ana.Id, Emoji = "👍" });
        c.Db.CrmDiscordLeituras.Add(new CrmDiscordLeitura { UsuarioId = c.Ana.Id, Chave = criado.Chave, UltimaLidaId = "1" });
        await c.Db.SaveChangesAsync();

        await c.Grupos().ApagarCanalAsync(criado.Chave, "  avisos ", CancellationToken.None);

        Assert.Contains(discordId, c.Servidor.CanaisApagados);
        Assert.False(await c.Db.CrmDiscordCanais.AnyAsync(x => x.Chave == criado.Chave));
        Assert.False(await c.Db.CrmDiscordReacoes.AnyAsync(x => x.LeituraId == discordId));
        Assert.False(await c.Db.CrmDiscordLeituras.AnyAsync(x => x.Chave == criado.Chave));
        Assert.DoesNotContain(await c.Chat().ListarCanaisAsync(c.Ana.Id, CancellationToken.None), x => x.Chave == criado.Chave);
    }

    [Fact]
    public async Task Apagar_ComNomeErrado_OuSemConfirmar_NaoApagaNada()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var criado = await c.Grupos().CriarCanalAsync("Avisos", "geral", null, CancellationToken.None);

        var errado = await Assert.ThrowsAsync<CrmBusinessException>(() => c.Grupos().ApagarCanalAsync(criado.Chave, "Outro", CancellationToken.None));
        var vazio = await Assert.ThrowsAsync<CrmBusinessException>(() => c.Grupos().ApagarCanalAsync(criado.Chave, "", CancellationToken.None));

        Assert.Equal("canal_confirmacao", errado.Codigo);
        Assert.Equal("canal_confirmacao", vazio.Codigo);
        Assert.Empty(c.Servidor.CanaisApagados);
        Assert.True(await c.Db.CrmDiscordCanais.AnyAsync(x => x.Chave == criado.Chave));
    }

    [Fact]
    public async Task Apagar_CanalDeGrupoOuDeConversas_NuncaE()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);

        var geral = await Assert.ThrowsAsync<CrmBusinessException>(() => c.Grupos().ApagarCanalAsync("geral", "Geral", CancellationToken.None));
        var conversas = await Assert.ThrowsAsync<CrmBusinessException>(() => c.Grupos().ApagarCanalAsync("conversas", "Conversas diretas", CancellationToken.None));
        var inexistente = await Assert.ThrowsAsync<CrmBusinessException>(() => c.Grupos().ApagarCanalAsync("extra:nao-existe", "x", CancellationToken.None));

        Assert.All([geral, conversas, inexistente], e => Assert.Equal("canal_nao_apagavel", e.Codigo));
        Assert.Empty(c.Servidor.CanaisApagados);
    }

    [Fact]
    public async Task Apagar_ComFalhaDoDiscord_MantemOCanalNoCrm()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var criado = await c.Grupos().CriarCanalAsync("Avisos", "geral", null, CancellationToken.None);
        c.Servidor.SemPermissao = true;

        var erro = await Assert.ThrowsAsync<CrmBusinessException>(() => c.Grupos().ApagarCanalAsync(criado.Chave, "Avisos", CancellationToken.None));

        Assert.Equal("discord_indisponivel", erro.Codigo);
        Assert.True(await c.Db.CrmDiscordCanais.AnyAsync(x => x.Chave == criado.Chave));
    }

    [Fact]
    public async Task Arquivar_EscondeNoChatSemMexerNoDiscord_EDesarquivarMostraDeNovo_ASincronizacaoNaoReativa()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var criado = await c.Grupos().CriarCanalAsync("Avisos", "geral", null, CancellationToken.None);

        var arquivado = await c.Grupos().ArquivarCanalAsync(criado.Chave, true, CancellationToken.None);
        Assert.False(arquivado.Ativo);
        Assert.DoesNotContain(await c.Chat().ListarCanaisAsync(c.Ana.Id, CancellationToken.None), x => x.Chave == criado.Chave);
        Assert.Empty(c.Servidor.CanaisApagados);

        await c.Grupos().SincronizarAsync(CancellationToken.None);
        Assert.False((await c.Db.CrmDiscordCanais.AsNoTracking().SingleAsync(x => x.Chave == criado.Chave)).Ativo);

        await c.Grupos().ArquivarCanalAsync(criado.Chave, false, CancellationToken.None);
        Assert.Contains(await c.Chat().ListarCanaisAsync(c.Ana.Id, CancellationToken.None), x => x.Chave == criado.Chave);
    }

    [Fact]
    public async Task Arquivar_CanalDeGrupo_NaoEPermitido()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);

        var erro = await Assert.ThrowsAsync<CrmBusinessException>(() => c.Grupos().ArquivarCanalAsync("geral", true, CancellationToken.None));

        Assert.Equal("canal_nao_arquivavel", erro.Codigo);
    }

    [Fact]
    public async Task CriarCanalDeVoz_FazOCanalDeVozComOCargoDoGrupo_ENaoApareceNoChatDeTexto()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var cargoGeral = (await c.Db.CrmDiscordCanais.AsNoTracking().SingleAsync(x => x.Chave == "geral")).DiscordCargoId;
        var antes = c.Servidor.VozesCriadas.Count;

        var criado = await c.Grupos().CriarCanalAsync("Reunião semanal", "geral", null, CancellationToken.None, voz: true);

        Assert.True(criado.Voz);
        Assert.StartsWith("extra-voz:", criado.Chave);
        var voz = c.Servidor.VozesCriadas[antes];
        Assert.Equal("Reunião semanal", voz.Nome); // canal de voz mantém o nome como digitado
        Assert.Equal(cargoGeral, Assert.Single(voz.Permitidos).Id);
        Assert.DoesNotContain(await c.Chat().ListarCanaisAsync(c.Ana.Id, CancellationToken.None), x => x.Chave == criado.Chave);
        Assert.Contains(await c.Grupos().ListarCanaisAsync(CancellationToken.None), x => x.Chave == criado.Chave && x.Voz && x.Extra);
    }

    [Fact]
    public async Task RenomearCanalDeVoz_NaoVirouSlug_ESincronizacaoNaoApaga()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var criado = await c.Grupos().CriarCanalAsync("Reunião", "geral", null, CancellationToken.None, voz: true);

        await c.Grupos().RenomearCanalAsync(criado.Chave, "Reunião de equipe", CancellationToken.None);
        await c.Grupos().SincronizarAsync(CancellationToken.None);

        Assert.Contains(c.Servidor.Renomeados, x => x.Nome == "Reunião de equipe");
        Assert.True((await c.Db.CrmDiscordCanais.AsNoTracking().SingleAsync(x => x.Chave == criado.Chave)).Ativo);
    }

    [Fact]
    public async Task ApagarCanalDeVoz_Funciona()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var criado = await c.Grupos().CriarCanalAsync("Reunião", "geral", null, CancellationToken.None, voz: true);

        await c.Grupos().ApagarCanalAsync(criado.Chave, "Reunião", CancellationToken.None);

        Assert.False(await c.Db.CrmDiscordCanais.AnyAsync(x => x.Chave == criado.Chave));
    }
}
