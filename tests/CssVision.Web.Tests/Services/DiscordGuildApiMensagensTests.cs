using System.Net;
using System.Text;
using CssVision.Web.Services.Discord;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Leitura e envio de mensagens de um canal: formato das chamadas ao Discord e leitura da resposta. Nenhuma chamada real.</summary>
public class DiscordGuildApiMensagensTests
{
    private sealed record Chamada(HttpMethod Metodo, string CaminhoEQuery, string? Autorizacao, string Corpo);

    private sealed class ManipuladorFalso(Func<Chamada, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<Chamada> Chamadas { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var corpo = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            var chamada = new Chamada(request.Method, request.RequestUri!.PathAndQuery, request.Headers.Authorization?.ToString(), corpo);
            Chamadas.Add(chamada);
            return responder(chamada);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static (DiscordGuildApi Api, ManipuladorFalso Manipulador) Montar(Func<Chamada, HttpResponseMessage> responder)
    {
        var manipulador = new ManipuladorFalso(responder);
        var http = new HttpClient(manipulador) { BaseAddress = new Uri(DiscordApi.UrlBase) };
        var opcoes = Options.Create(new DiscordOptions { BotToken = "token-do-bot", ClientId = "app-1", ClientSecret = "segredo-1", GuildId = "servidor-9" });
        return (new DiscordGuildApi(http, opcoes, NullLogger<DiscordGuildApi>.Instance), manipulador);
    }

    [Fact]
    public async Task ListarMensagens_DevolveDaMaisAntigaParaAMaisNova_ComMencoesEEmojisLegiveis()
    {
        // O Discord devolve da mais nova para a mais antiga.
        var (api, manipulador) = Montar(_ => Json(HttpStatusCode.OK, """
            [
              {"id":"2","content":"oi <@42> <:fogo:999> e <a:festa:555>","timestamp":"2026-10-07T18:00:02.000000+00:00",
               "author":{"id":"7","username":"ana_melo","global_name":"Ana Melo","avatar":"abc"},
               "mentions":[{"id":"42","username":"bia","global_name":"Bia"}],
               "attachments":[{"filename":"foto.png","url":"https://cdn/x/foto.png","content_type":"image/png"},{"filename":"plano.pdf","url":"https://cdn/x/plano.pdf","content_type":"application/pdf"}]},
              {"id":"1","content":"bom dia","timestamp":"2026-10-07T18:00:01.000000+00:00",
               "author":{"id":"8","username":"carlos","avatar":null}}
            ]
            """));

        var mensagens = await api.ListarMensagensAsync("canal-1", 50, null, CancellationToken.None);

        Assert.Equal(["1", "2"], mensagens.Select(m => m.Id).ToList());
        Assert.Equal("carlos", mensagens[0].AutorNome);
        Assert.Null(mensagens[0].AutorFotoUrl);
        Assert.Equal("Ana Melo", mensagens[1].AutorNome);
        Assert.Equal("https://cdn.discordapp.com/avatars/7/abc.png?size=64", mensagens[1].AutorFotoUrl);
        Assert.Equal("oi @Bia :fogo: e :festa:", mensagens[1].Conteudo);
        Assert.Equal(["foto.png", "plano.pdf"], mensagens[1].Anexos.Select(a => a.Nome).ToList());
        Assert.True(mensagens[1].Anexos[0].Imagem);
        Assert.False(mensagens[1].Anexos[1].Imagem);

        var chamada = Assert.Single(manipulador.Chamadas);
        Assert.Equal("/api/v10/channels/canal-1/messages?limit=50", chamada.CaminhoEQuery);
        Assert.Equal("Bot token-do-bot", chamada.Autorizacao);
    }

    [Fact]
    public async Task ListarMensagens_ComAntesDe_PedeAsAnterioresELimitaOTamanhoDaPagina()
    {
        var (api, manipulador) = Montar(_ => Json(HttpStatusCode.OK, "[]"));

        await api.ListarMensagensAsync("canal-1", 500, "123", CancellationToken.None);

        Assert.Equal("/api/v10/channels/canal-1/messages?limit=100&before=123", manipulador.Chamadas[0].CaminhoEQuery);
    }

    [Fact]
    public async Task ListarMensagens_SemPermissao_ExplicaOQueFazer()
    {
        var (api, _) = Montar(_ => Json(HttpStatusCode.Forbidden, """{"code":50013}"""));

        var ex = await Assert.ThrowsAsync<DiscordApiException>(() => api.ListarMensagensAsync("canal-1", 50, null, CancellationToken.None));

        Assert.Contains("ler as mensagens do canal", ex.Message);
        Assert.DoesNotContain("token-do-bot", ex.Message);
    }

    [Fact]
    public async Task EnviarMensagem_CriaOWebhookUmaVez_PublicaComNomeEFoto_ENuncaMarcaTodos()
    {
        var canal = $"canal-{Guid.NewGuid():N}"; // o guardado dos webhooks é estático: cada teste usa um canal só dele
        var (api, manipulador) = Montar(c =>
        {
            if (c.Metodo == HttpMethod.Get && c.CaminhoEQuery.EndsWith("/webhooks")) return Json(HttpStatusCode.OK, "[]");
            if (c.Metodo == HttpMethod.Post && c.CaminhoEQuery.EndsWith("/webhooks")) return Json(HttpStatusCode.OK, """{"id":"w1","token":"tk1"}""");
            return Json(HttpStatusCode.OK, """
                {"id":"m9","content":"oi @everyone","timestamp":"2026-10-07T18:00:00.000000+00:00","webhook_id":"w1",
                 "author":{"id":"w1","username":"Ana Melo","avatar":null}}
                """);
        });

        var primeira = await api.EnviarMensagemAsync(canal, "Ana Melo", "https://crm/ana.jpg", "oi @everyone", CancellationToken.None);
        await api.EnviarMensagemAsync(canal, "Ana Melo", null, "de novo", CancellationToken.None);

        Assert.True(primeira.DoCrm);
        Assert.Equal(1, manipulador.Chamadas.Count(c => c.Metodo == HttpMethod.Post && c.CaminhoEQuery.EndsWith("/webhooks")));
        var envio = manipulador.Chamadas.First(c => c.CaminhoEQuery.StartsWith("/api/v10/webhooks/w1/tk1"));
        Assert.Contains("wait=true", envio.CaminhoEQuery);
        Assert.Null(envio.Autorizacao); // o webhook se autentica pelo token na própria URL
        Assert.Contains("\"username\":\"Ana Melo\"", envio.Corpo);
        Assert.Contains("\"avatar_url\":\"https://crm/ana.jpg\"", envio.Corpo);
        Assert.Contains("\"parse\":[]", envio.Corpo);
    }

    [Fact]
    public async Task EnviarMensagem_ReusaOWebhookQueJaExisteNoCanal()
    {
        var canal = $"canal-{Guid.NewGuid():N}";
        var (api, manipulador) = Montar(c =>
        {
            if (c.Metodo == HttpMethod.Get)
            {
                return Json(HttpStatusCode.OK, """[{"id":"outro","name":"Outro","token":"x"},{"id":"w7","name":"CRM CSS Brasil","token":"tk7"}]""");
            }

            return Json(HttpStatusCode.OK, """{"id":"m1","content":"oi","timestamp":"2026-10-07T18:00:00+00:00","author":{"id":"w7","username":"Ana"}}""");
        });

        await api.EnviarMensagemAsync(canal, "Ana", null, "oi", CancellationToken.None);

        Assert.DoesNotContain(manipulador.Chamadas, c => c.Metodo == HttpMethod.Post && c.CaminhoEQuery.EndsWith("/webhooks"));
        Assert.Contains(manipulador.Chamadas, c => c.CaminhoEQuery.StartsWith("/api/v10/webhooks/w7/tk7"));
    }

    [Fact]
    public async Task EnviarMensagem_SeOWebhookFoiApagado_EsqueceEleParaRecriarNaProxima()
    {
        var canal = $"canal-{Guid.NewGuid():N}";
        var (api, manipulador) = Montar(c =>
        {
            if (c.Metodo == HttpMethod.Get) return Json(HttpStatusCode.OK, "[]");
            if (c.CaminhoEQuery.EndsWith("/webhooks")) return Json(HttpStatusCode.OK, """{"id":"w2","token":"tk2"}""");
            return Json(HttpStatusCode.NotFound, """{"code":10015}""");
        });

        await Assert.ThrowsAsync<DiscordApiException>(() => api.EnviarMensagemAsync(canal, "Ana", null, "oi", CancellationToken.None));
        await Assert.ThrowsAsync<DiscordApiException>(() => api.EnviarMensagemAsync(canal, "Ana", null, "oi", CancellationToken.None));

        // Sem o esquecimento, a segunda tentativa reusaria o webhook morto sem criar outro.
        Assert.Equal(2, manipulador.Chamadas.Count(c => c.Metodo == HttpMethod.Post && c.CaminhoEQuery.EndsWith("/webhooks")));
    }

    [Fact]
    public async Task CriarConversaPrivada_CriaThreadPrivadaQueNinguemPodeConvidarTerceiros()
    {
        var (api, manipulador) = Montar(_ => Json(HttpStatusCode.OK, """{"id":"t1"}"""));

        var id = await api.CriarConversaPrivadaAsync("pai-1", "dm-ana-bia", CancellationToken.None);

        Assert.Equal("t1", id);
        var chamada = Assert.Single(manipulador.Chamadas);
        Assert.Equal("/api/v10/channels/pai-1/threads", chamada.CaminhoEQuery);
        Assert.Contains("\"type\":12", chamada.Corpo);
        Assert.Contains("\"invitable\":false", chamada.Corpo);
        Assert.Contains("\"name\":\"dm-ana-bia\"", chamada.Corpo);
    }

    [Fact]
    public async Task AdicionarAThread_ChamaOEndpointDeMembroDaThread()
    {
        var (api, manipulador) = Montar(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

        await api.AdicionarAThreadAsync("t1", "u9", CancellationToken.None);

        var chamada = Assert.Single(manipulador.Chamadas);
        Assert.Equal(HttpMethod.Put, chamada.Metodo);
        Assert.Equal("/api/v10/channels/t1/thread-members/u9", chamada.CaminhoEQuery);
    }

    [Fact]
    public async Task CriarCanalDeConversas_EscondeDoEveryone_PermiteAoCargoEscreverNasThreads_ENegaEscreverNoCanal()
    {
        var (api, manipulador) = Montar(_ => Json(HttpStatusCode.OK, """{"id":"c1"}"""));

        await api.CriarCanalDeConversasAsync("conversas-diretas", "cat-1", "cargo-geral", CancellationToken.None);

        var corpo = Assert.Single(manipulador.Chamadas).Corpo;
        Assert.Contains("\"parent_id\":\"cat-1\"", corpo);
        Assert.Contains("\"id\":\"cargo-geral\"", corpo);
        Assert.Contains("\"deny\":\"2048\"", corpo);          // ninguém escreve no canal em si
        Assert.Contains("\"id\":\"app-1\",\"type\":1", corpo);  // o bot tem permissão própria
    }

    [Fact]
    public async Task EnviarMensagem_NaThread_UsaOWebhookDoCanalPaiComThreadId()
    {
        var canal = $"canal-{Guid.NewGuid():N}";
        var (api, manipulador) = Montar(c =>
        {
            if (c.Metodo == HttpMethod.Get) return Json(HttpStatusCode.OK, "[]");
            if (c.CaminhoEQuery.EndsWith("/webhooks")) return Json(HttpStatusCode.OK, """{"id":"w3","token":"tk3"}""");
            return Json(HttpStatusCode.OK, """{"id":"m1","content":"oi","timestamp":"2026-10-07T18:00:00+00:00","webhook_id":"w3","author":{"id":"w3","username":"Ana"}}""");
        });

        await api.EnviarMensagemAsync(canal, "Ana", null, "oi", CancellationToken.None, "thread-7");

        var envio = manipulador.Chamadas.Single(c => c.CaminhoEQuery.StartsWith("/api/v10/webhooks/w3/tk3"));
        Assert.Contains("thread_id=thread-7", envio.CaminhoEQuery);
    }

    [Fact]
    public async Task EnviarMensagem_NaThread_SeOWebhookNaoServe_OBotPublicaComONomeDeQuemEscreveu()
    {
        var canal = $"canal-{Guid.NewGuid():N}";
        var (api, manipulador) = Montar(c =>
        {
            if (c.Metodo == HttpMethod.Get) return Json(HttpStatusCode.OK, "[]");
            if (c.CaminhoEQuery.EndsWith("/webhooks")) return Json(HttpStatusCode.OK, """{"id":"w4","token":"tk4"}""");
            if (c.CaminhoEQuery.StartsWith("/api/v10/webhooks/")) return Json(HttpStatusCode.BadRequest, """{"code":220003}""");
            return Json(HttpStatusCode.OK, """{"id":"m2","content":"**Ana:** oi","timestamp":"2026-10-07T18:00:00+00:00","author":{"id":"bot","username":"CRM"}}""");
        });

        var enviada = await api.EnviarMensagemAsync(canal, "Ana", null, "oi", CancellationToken.None, "thread-8");

        Assert.Equal("**Ana:** oi", enviada.Conteudo);
        var plano = manipulador.Chamadas.Last();
        Assert.Equal("/api/v10/channels/thread-8/messages", plano.CaminhoEQuery);
        Assert.Equal("Bot token-do-bot", plano.Autorizacao);
        Assert.Contains("\"parse\":[]", plano.Corpo);
    }
}
