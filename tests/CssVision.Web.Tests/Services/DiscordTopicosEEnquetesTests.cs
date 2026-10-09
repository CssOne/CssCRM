using System.Net;
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

/// <summary>"Criar tópico" e "Criar enquete" do chat, e a leitura das enquetes que chegam do Discord.</summary>
public class DiscordTopicosEEnquetesTests
{
    private static DiscordOptions Configurado() => new()
    {
        BotToken = "bot", ClientId = "app-1", ClientSecret = "segredo", GuildId = "servidor-1", UrlPublica = "https://crm.exemplo.com/",
    };

    private sealed record Cenario(ApplicationDbContext Db, DiscordServidorFalso Servidor, DiscordChatService Servico, ApplicationUser Ana, ApplicationUser Beto, string DiscordCanalId);

    private static async Task<Cenario> MontarAsync(TestDbContextFactory factory)
    {
        var db = factory.CreateContext();
        var regional = await factory.CriarRegionalAsync(db, "MG132");
        var servidor = new DiscordServidorFalso();
        var grupos = new DiscordGruposService(db, servidor, Options.Create(Configurado()), NullLogger<DiscordGruposService>.Instance);
        await grupos.SincronizarAsync(CancellationToken.None);

        async Task<ApplicationUser> Pessoa(string nome)
        {
            var u = await factory.CriarUsuarioAsync(db, nome);
            u.RegionalId = regional.Id;
            await db.SaveChangesAsync();
            await factory.AtribuirPapelAsync(db, u, Roles.Comercial);
            db.CrmDiscordVinculos.Add(new CrmDiscordVinculo { UsuarioId = u.Id, DiscordUserId = $"d-{nome.ToLowerInvariant()}", DiscordNome = nome, NoServidor = true, VinculadoEm = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
            return u;
        }

        var ana = await Pessoa("Ana");
        var beto = await Pessoa("Beto");
        var geral = await db.CrmDiscordCanais.AsNoTracking().SingleAsync(c => c.Chave == "geral");
        var servico = new DiscordChatService(db, servidor, new MemoryCache(new MemoryCacheOptions()), Options.Create(Configurado()));
        return new Cenario(db, servidor, servico, ana, beto, geral.DiscordCanalId);
    }

    [Fact]
    public async Task CriarTopico_AbreAThreadNoCanal_EAvisaOGrupoNoNomeDaPessoa()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);

        var aviso = await c.Servico.CriarTopicoAsync(c.Ana.Id, "geral", "  Metas   de outubro ", CancellationToken.None);

        var topico = Assert.Single(c.Servidor.TopicosCriados);
        Assert.Equal(c.DiscordCanalId, topico.CanalId);
        Assert.Equal("Metas de outubro", topico.Nome);
        Assert.Contains("Metas de outubro", aviso.Conteudo);
        Assert.Contains("<#topico-1>", aviso.Conteudo);
    }

    [Theory]
    [InlineData("", "topico_sem_nome")]
    [InlineData("   ", "topico_sem_nome")]
    public async Task CriarTopico_SemNome_Falha(string nome, string codigo)
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);

        var erro = await Assert.ThrowsAsync<CrmBusinessException>(() => c.Servico.CriarTopicoAsync(c.Ana.Id, "geral", nome, CancellationToken.None));

        Assert.Equal(codigo, erro.Codigo);
        Assert.Empty(c.Servidor.TopicosCriados);
    }

    [Fact]
    public async Task CriarTopico_DentroDeUmaConversaDireta_Falha()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var conversa = await c.Servico.IniciarConversaAsync(c.Ana.Id, c.Beto.Id, CancellationToken.None);

        var erro = await Assert.ThrowsAsync<CrmBusinessException>(() => c.Servico.CriarTopicoAsync(c.Ana.Id, conversa.Chave, "Assunto", CancellationToken.None));

        Assert.Equal("topico_so_em_grupo", erro.Codigo);
        Assert.Empty(c.Servidor.TopicosCriados);
    }

    [Fact]
    public async Task CriarEnquete_PublicaPergunta_RespostasEDuracao_ENaMensagemVoltaAEnquete()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);

        var enviada = await c.Servico.CriarEnqueteAsync(c.Ana.Id, "geral", " Almoço de sexta? ", [" Sim ", "Não", ""], 48, false, CancellationToken.None);

        var enquete = Assert.Single(c.Servidor.EnquetesEnviadas);
        Assert.Equal(c.DiscordCanalId, enquete.Onde);
        Assert.Equal(c.Ana.NomeCompleto, enquete.Autor);
        Assert.Equal("Almoço de sexta?", enquete.Pergunta);
        Assert.Equal(["Sim", "Não"], enquete.Respostas);
        Assert.Equal(48, enquete.Horas);
        Assert.NotNull(enviada.Enquete);
        Assert.Equal(2, enviada.Enquete!.Respostas.Count);
    }

    [Theory]
    [InlineData("", new[] { "a", "b" }, 24, "enquete_sem_pergunta")]
    [InlineData("P", new[] { "a" }, 24, "enquete_respostas")]
    [InlineData("P", new[] { "a", " " }, 24, "enquete_respostas")]
    [InlineData("P", new[] { "a", "b" }, 0, "enquete_duracao")]
    [InlineData("P", new[] { "a", "b" }, 769, "enquete_duracao")]
    public async Task CriarEnquete_ComDadosInvalidos_Falha_ENadaEPublicado(string pergunta, string[] respostas, int horas, string codigo)
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);

        var erro = await Assert.ThrowsAsync<CrmBusinessException>(() => c.Servico.CriarEnqueteAsync(c.Ana.Id, "geral", pergunta, respostas, horas, false, CancellationToken.None));

        Assert.Equal(codigo, erro.Codigo);
        Assert.Empty(c.Servidor.EnquetesEnviadas);
    }

    [Fact]
    public async Task CriarEnquete_ComMaisDeDezRespostasOuRespostaLonga_Falha()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var onze = Enumerable.Range(1, 11).Select(i => $"R{i}").ToList();

        var muitas = await Assert.ThrowsAsync<CrmBusinessException>(() => c.Servico.CriarEnqueteAsync(c.Ana.Id, "geral", "P", onze, 24, false, CancellationToken.None));
        var longa = await Assert.ThrowsAsync<CrmBusinessException>(() => c.Servico.CriarEnqueteAsync(c.Ana.Id, "geral", "P", ["a", new string('x', 56)], 24, false, CancellationToken.None));

        Assert.Equal("enquete_respostas", muitas.Codigo);
        Assert.Equal("enquete_resposta_longa", longa.Codigo);
    }

    [Fact]
    public void LerEnquete_DevolvePerguntaRespostasEVotos_EMarcaEncerrada()
    {
        using var doc = System.Text.Json.JsonDocument.Parse("""
            {"poll":{"question":{"text":"Almoço?"},
              "answers":[{"answer_id":1,"poll_media":{"text":"Sim"}},{"answer_id":2,"poll_media":{"text":"Não"}}],
              "allow_multiselect":true,"expiry":"2020-01-01T00:00:00.000000+00:00",
              "results":{"is_finalized":false,"answer_counts":[{"id":1,"count":3,"me_voted":false}]}}}
            """);

        var enquete = DiscordGuildApi.LerEnquete(doc.RootElement);

        Assert.NotNull(enquete);
        Assert.Equal("Almoço?", enquete!.Pergunta);
        Assert.True(enquete.VariasEscolhas);
        Assert.Equal([("Sim", 3), ("Não", 0)], enquete.Respostas.Select(r => (r.Texto, r.Votos)).ToArray());
        Assert.True(enquete.Encerrada); // a data de término já passou
    }

    [Fact]
    public void LerEnquete_SemEnquete_DevolveNulo()
    {
        using var doc = System.Text.Json.JsonDocument.Parse("""{"content":"oi"}""");
        Assert.Null(DiscordGuildApi.LerEnquete(doc.RootElement));
    }
}
