using System.Net;
using System.Text;
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

/// <summary>Arquivos no chat: validação, destino certo e o corpo "multipart" enviado ao Discord.</summary>
public class DiscordAnexosTests
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

        public DiscordChatService Servico() => new(Db, Servidor, new MemoryCache(new MemoryCacheOptions()), Options.Create(Configurado()));
    }

    private static async Task<Cenario> MontarAsync(TestDbContextFactory factory)
    {
        var db = factory.CreateContext();
        var mg132 = await factory.CriarRegionalAsync(db, "MG132");
        var servidor = new DiscordServidorFalso();
        await new DiscordGruposService(db, servidor, Options.Create(Configurado()), NullLogger<DiscordGruposService>.Instance).SincronizarAsync(CancellationToken.None);
        return new Cenario { Factory = factory, Db = db, Servidor = servidor, Mg132 = mg132 };
    }

    private static async Task<ApplicationUser> PessoaAsync(Cenario c, string nome)
    {
        var u = await c.Factory.CriarUsuarioAsync(c.Db, nome);
        u.RegionalId = c.Mg132.Id;
        await c.Db.SaveChangesAsync();
        await c.Factory.AtribuirPapelAsync(c.Db, u, Roles.Comercial);
        c.Db.CrmDiscordVinculos.Add(new CrmDiscordVinculo { UsuarioId = u.Id, DiscordUserId = $"d-{nome.ToLowerInvariant()}", DiscordNome = nome, NoServidor = true, VinculadoEm = DateTimeOffset.UtcNow });
        await c.Db.SaveChangesAsync();
        return u;
    }

    private static readonly byte[] Conteudo = Encoding.UTF8.GetBytes("conteudo do arquivo");

    [Fact]
    public async Task EnviaOArquivoComLegenda_NoCanalDoGrupo()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var ana = await PessoaAsync(c, "Ana");

        var enviada = await c.Servico().EnviarArquivoAsync(ana.Id, "geral", "  segue a proposta  ", "proposta.pdf", "application/pdf", Conteudo, CancellationToken.None);

        var envio = Assert.Single(c.Servidor.ArquivosEnviados);
        Assert.Equal(c.Db.CrmDiscordCanais.Single(x => x.Chave == "geral").DiscordCanalId, envio.CanalId);
        Assert.Equal("Ana", envio.Nome);
        Assert.Equal("segue a proposta", envio.Texto);
        Assert.Equal("proposta.pdf", envio.Arquivo.Nome);
        Assert.Equal(Conteudo, envio.Arquivo.Conteudo);
        Assert.Equal("proposta.pdf", Assert.Single(enviada.Anexos).Nome);
    }

    [Fact]
    public async Task EnviaOArquivoSemLegenda()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var ana = await PessoaAsync(c, "Ana");

        await c.Servico().EnviarArquivoAsync(ana.Id, "geral", null, "foto.PNG", "image/png", Conteudo, CancellationToken.None);

        Assert.Equal("", Assert.Single(c.Servidor.ArquivosEnviados).Texto);
    }

    [Fact]
    public async Task EnviaPelaConversaDireta_ParaAThread()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var ana = await PessoaAsync(c, "Ana");
        var bia = await PessoaAsync(c, "Bia");
        var servico = c.Servico();
        var conversa = await servico.IniciarConversaAsync(ana.Id, bia.Id, CancellationToken.None);

        await servico.EnviarArquivoAsync(ana.Id, conversa.Chave, null, "comprovante.pdf", "application/pdf", Conteudo, CancellationToken.None);

        Assert.Equal(c.Servidor.Threads.Keys.Single(), Assert.Single(c.Servidor.ArquivosEnviados).CanalId);
    }

    [Theory]
    [InlineData("virus.exe")]
    [InlineData("script.js")]
    [InlineData("pagina.html")]
    [InlineData("atalho.lnk")]
    [InlineData("semextensao")]
    public async Task Recusa_TiposQueNaoSaoPermitidos(string nome)
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var ana = await PessoaAsync(c, "Ana");

        var ex = await Assert.ThrowsAsync<CrmBusinessException>(() => c.Servico().EnviarArquivoAsync(ana.Id, "geral", null, nome, "application/octet-stream", Conteudo, CancellationToken.None));

        Assert.Equal("arquivo_tipo_invalido", ex.Codigo);
        Assert.Empty(c.Servidor.ArquivosEnviados);
    }

    [Fact]
    public async Task Recusa_ArquivoVazioGrandeEOuLegendaLonga()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var ana = await PessoaAsync(c, "Ana");
        var servico = c.Servico();

        var vazio = await Assert.ThrowsAsync<CrmBusinessException>(() => servico.EnviarArquivoAsync(ana.Id, "geral", null, "a.pdf", "application/pdf", [], CancellationToken.None));
        var grande = await Assert.ThrowsAsync<CrmBusinessException>(() => servico.EnviarArquivoAsync(ana.Id, "geral", null, "a.pdf", "application/pdf", new byte[DiscordChatService.LimiteDoArquivo + 1], CancellationToken.None));
        var longa = await Assert.ThrowsAsync<CrmBusinessException>(() => servico.EnviarArquivoAsync(ana.Id, "geral", new string('x', DiscordChatService.LimiteDoTexto + 1), "a.pdf", "application/pdf", Conteudo, CancellationToken.None));

        Assert.Equal("arquivo_vazio", vazio.Codigo);
        Assert.Equal("arquivo_grande", grande.Codigo);
        Assert.Equal("mensagem_longa", longa.Codigo);
        Assert.Empty(c.Servidor.ArquivosEnviados);
    }

    [Fact]
    public async Task Recusa_GrupoQueAPessoaNaoParticipa()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var ana = await PessoaAsync(c, "Ana");

        await Assert.ThrowsAsync<CrmForbiddenException>(() => c.Servico().EnviarArquivoAsync(ana.Id, "gestao", null, "a.pdf", "application/pdf", Conteudo, CancellationToken.None));
        Assert.Empty(c.Servidor.ArquivosEnviados);
    }

    [Theory]
    [InlineData("proposta.pdf", "proposta.pdf")]
    [InlineData(@"C:\Users\ana\Documentos\proposta final.pdf", "proposta final.pdf")]
    [InlineData("../../etc/senha.txt", "senha.txt")]
    [InlineData("relatório<>|?.xlsx", "relatório____.xlsx")]
    [InlineData("", "arquivo")]
    [InlineData("   ", "arquivo")]
    public void NomeSeguro_TiraPastasECaracteresEstranhos(string entrada, string esperado) =>
        Assert.Equal(esperado, DiscordChatService.NomeSeguro(entrada));

    [Fact]
    public void NomeSeguro_CortaEm100CaracteresSemPerderAExtensao()
    {
        var nome = DiscordChatService.NomeSeguro(new string('a', 300) + ".pdf");

        Assert.Equal(100, nome.Length);
        Assert.EndsWith(".pdf", nome);
    }

    // ---- camada HTTP

    private sealed class ManipuladorFalso(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<(HttpMethod Metodo, string Caminho, string? Autorizacao, string Corpo, string? TipoDoCorpo)> Chamadas { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var corpo = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Chamadas.Add((request.Method, request.RequestUri!.PathAndQuery, request.Headers.Authorization?.ToString(), corpo, request.Content?.Headers.ContentType?.MediaType));
            return responder(request);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static (DiscordGuildApi Api, ManipuladorFalso Manipulador) MontarApi(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var manipulador = new ManipuladorFalso(responder);
        var http = new HttpClient(manipulador) { BaseAddress = new Uri(DiscordApi.UrlBase) };
        var opcoes = Options.Create(new DiscordOptions { BotToken = "token-do-bot", ClientId = "app-1", ClientSecret = "s", GuildId = "servidor-9" });
        return (new DiscordGuildApi(http, opcoes, NullLogger<DiscordGuildApi>.Instance), manipulador);
    }

    private const string MensagemOk = """{"id":"m1","content":"oi","timestamp":"2026-10-07T18:00:00+00:00","webhook_id":"wx","author":{"id":"wx","username":"Ana"},"attachments":[{"filename":"a.pdf","url":"https://cdn/a.pdf","content_type":"application/pdf"}]}""";

    [Fact]
    public async Task Http_EnviaMultipartComPayloadJsonEOArquivo_PeloWebhook()
    {
        var canal = $"canal-{Guid.NewGuid():N}";
        var (api, manipulador) = MontarApi(r => r.Method == HttpMethod.Get ? Json(HttpStatusCode.OK, "[]")
            : r.RequestUri!.AbsolutePath.EndsWith("/webhooks") ? Json(HttpStatusCode.OK, """{"id":"wx","token":"tkx"}""")
            : Json(HttpStatusCode.OK, MensagemOk));

        var m = await api.EnviarArquivoAsync(canal, "Ana", "https://crm/ana.jpg", "legenda", new DiscordArquivo("a.pdf", "application/pdf", Conteudo), CancellationToken.None);

        var envio = manipulador.Chamadas.Single(c => c.Caminho.StartsWith("/api/v10/webhooks/wx/tkx"));
        Assert.Equal("multipart/form-data", envio.TipoDoCorpo);
        Assert.Contains("name=payload_json", envio.Corpo);
        Assert.Contains("\"username\":\"Ana\"", envio.Corpo);
        Assert.Contains("\"parse\":[]", envio.Corpo);
        Assert.Contains("name=\"files[0]\"", envio.Corpo);
        Assert.Contains("filename=a.pdf", envio.Corpo);
        Assert.Contains("conteudo do arquivo", envio.Corpo);
        Assert.Equal("a.pdf", Assert.Single(m.Anexos).Nome);
    }

    [Fact]
    public async Task Http_NaThread_SeOWebhookNaoServe_OBotEnviaOArquivoComALegenda()
    {
        var canal = $"canal-{Guid.NewGuid():N}";
        var (api, manipulador) = MontarApi(r => r.Method == HttpMethod.Get ? Json(HttpStatusCode.OK, "[]")
            : r.RequestUri!.AbsolutePath.EndsWith("/webhooks") ? Json(HttpStatusCode.OK, """{"id":"wy","token":"tky"}""")
            : r.RequestUri!.AbsolutePath.Contains("/webhooks/") ? Json(HttpStatusCode.BadRequest, """{"code":220003}""")
            : Json(HttpStatusCode.OK, MensagemOk));

        await api.EnviarArquivoAsync(canal, "Ana", null, "", new DiscordArquivo("a.pdf", "application/pdf", Conteudo), CancellationToken.None, "thread-1");

        var plano = manipulador.Chamadas.Last();
        Assert.Equal("/api/v10/channels/thread-1/messages", plano.Caminho);
        Assert.Equal("Bot token-do-bot", plano.Autorizacao);
        Assert.Contains("**Ana** enviou um arquivo", plano.Corpo);
        Assert.Contains("name=\"files[0]\"", plano.Corpo);
    }
}
