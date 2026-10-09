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

/// <summary>Emojis e figurinhas do servidor no chat, e o aviso de "texto oculto" (Message Content Intent desligado).</summary>
public class DiscordEmojisEFigurinhasTests
{
    private static DiscordOptions Configurado() => new()
    {
        BotToken = "bot", ClientId = "app-1", ClientSecret = "segredo", GuildId = "servidor-1", UrlPublica = "https://crm.exemplo.com/",
    };

    private sealed record Cenario(TestDbContextFactory Factory, ApplicationDbContext Db, DiscordServidorFalso Servidor, DiscordChatService Servico, ApplicationUser Ana, string DiscordCanalId);

    private static async Task<Cenario> MontarAsync(TestDbContextFactory factory)
    {
        var db = factory.CreateContext();
        var regional = await factory.CriarRegionalAsync(db, "MG132");
        var servidor = new DiscordServidorFalso();
        var grupos = new DiscordGruposService(db, servidor, Options.Create(Configurado()), NullLogger<DiscordGruposService>.Instance);
        await grupos.SincronizarAsync(CancellationToken.None);
        var ana = await factory.CriarUsuarioAsync(db, "Ana");
        ana.RegionalId = regional.Id;
        await db.SaveChangesAsync();
        await factory.AtribuirPapelAsync(db, ana, Roles.Comercial);
        db.CrmDiscordVinculos.Add(new CrmDiscordVinculo { UsuarioId = ana.Id, DiscordUserId = "d-ana", DiscordNome = "Ana", NoServidor = true, VinculadoEm = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        var geral = await db.CrmDiscordCanais.AsNoTracking().SingleAsync(c => c.Chave == "geral");
        var servico = new DiscordChatService(db, servidor, new MemoryCache(new MemoryCacheOptions()), Options.Create(Configurado()));
        return new Cenario(factory, db, servidor, servico, ana, geral.DiscordCanalId);
    }

    [Fact]
    public async Task Extras_ListaEmojisEFigurinhasDoServidor()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        c.Servidor.EmojisDoServidor.Add(new DiscordEmoji("111", "parabens", false, "https://cdn.exemplo.com/emojis/111.png"));
        c.Servidor.FigurinhasDoServidor.Add(new DiscordSticker("222", "venda", null, "https://cdn.exemplo.com/stickers/222.png"));

        var extras = await c.Servico.ListarExtrasAsync(CancellationToken.None);

        Assert.Equal("parabens", Assert.Single(extras.Emojis).Nome);
        Assert.Equal("venda", Assert.Single(extras.Figurinhas).Nome);
    }

    [Fact]
    public async Task EnviarFigurinha_PublicaAImagemDoServidor_ComONomeDaPessoa()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        c.Servidor.FigurinhasDoServidor.Add(new DiscordSticker("222", "venda", null, "https://cdn.exemplo.com/stickers/222.png"));

        var enviada = await c.Servico.EnviarFigurinhaAsync(c.Ana.Id, "geral", "222", CancellationToken.None);

        var imagem = Assert.Single(c.Servidor.ImagensEnviadas);
        Assert.Equal(c.DiscordCanalId, imagem.CanalId);
        Assert.Equal(c.Ana.NomeCompleto, imagem.Nome);
        Assert.Equal("https://cdn.exemplo.com/stickers/222.png", imagem.ImagemUrl);
        Assert.Equal("https://cdn.exemplo.com/stickers/222.png", Assert.Single(enviada.Anexos).Url);
    }

    [Fact]
    public async Task EnviarFigurinha_ComIdQueOServidorNaoTem_Falha_ENadaEPublicado()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);

        var erro = await Assert.ThrowsAsync<CrmBusinessException>(() => c.Servico.EnviarFigurinhaAsync(c.Ana.Id, "geral", "999", CancellationToken.None));

        Assert.Equal("figurinha_invalida", erro.Codigo);
        Assert.Empty(c.Servidor.ImagensEnviadas);
    }

    [Fact]
    public async Task ConteudoOculto_AvisaQuandoUmaPessoaDeVerdadeEscreveuEChegouSemTexto_MesmoSeOBotChegaComTexto()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var agora = DateTimeOffset.UtcNow;
        c.Servidor.Mensagens[c.DiscordCanalId] =
        [
            new DiscordMensagem("1", "CssBrasilCRM", null, "Boas-vindas ao servidor", agora, [], false, false, true),
            new DiscordMensagem("2", "Jonathan", null, "", agora.AddMinutes(1), [], false, false, false),
        ];

        var r = await c.Servico.ListarMensagensAsync(c.Ana.Id, "geral", null, CancellationToken.None);

        Assert.True(r.ConteudoOculto);
    }

    [Fact]
    public async Task ConteudoOculto_NaoAvisaComTextoNormal_NemComFigurinhaOuCartaoDoBotSemTexto()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var agora = DateTimeOffset.UtcNow;
        c.Servidor.Mensagens[c.DiscordCanalId] =
        [
            new DiscordMensagem("1", "Jonathan", null, "Bom dia", agora, [], false, false, false),
            new DiscordMensagem("2", "Samys", null, "", agora.AddMinutes(1), [new DiscordAnexo("venda", "https://cdn.exemplo.com/stickers/222.png", true)], false, false, false),
            new DiscordMensagem("3", "Css Brasil", null, "", agora.AddMinutes(2), [], true, false, true),
        ];

        var r = await c.Servico.ListarMensagensAsync(c.Ana.Id, "geral", null, CancellationToken.None);

        Assert.False(r.ConteudoOculto);
    }
}
