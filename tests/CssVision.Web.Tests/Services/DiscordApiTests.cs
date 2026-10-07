using System.Net;
using System.Text;
using System.Text.Json;
using CssVision.Web.Services.Discord;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Cliente HTTP do Discord: formato das chamadas, tratamento de erro e limite de requisições. Nenhuma chamada real ao Discord.</summary>
public class DiscordApiTests
{
    private sealed record Chamada(HttpMethod Metodo, string Caminho, string? Autorizacao, string Corpo);

    private sealed class ManipuladorFalso(Func<Chamada, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<Chamada> Chamadas { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var corpo = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            var chamada = new Chamada(request.Method, request.RequestUri!.AbsolutePath, request.Headers.Authorization?.ToString(), corpo);
            Chamadas.Add(chamada);
            return responder(chamada);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static (DiscordApi Api, ManipuladorFalso Manipulador) Montar(Func<Chamada, HttpResponseMessage> responder)
    {
        var manipulador = new ManipuladorFalso(responder);
        var http = new HttpClient(manipulador) { BaseAddress = new Uri(DiscordApi.UrlBase) };
        var opcoes = Options.Create(new DiscordOptions { BotToken = "token-do-bot", ClientId = "app-1", ClientSecret = "segredo-1", GuildId = "servidor-9" });
        return (new DiscordApi(http, opcoes, NullLogger<DiscordApi>.Instance), manipulador);
    }

    [Fact]
    public async Task TrocarCodigo_EnviaOFormularioOAuth2_EDevolveOToken()
    {
        var (api, manipulador) = Montar(_ => Json(HttpStatusCode.OK, """{"access_token":"abc123","token_type":"Bearer"}"""));

        var token = await api.TrocarCodigoAsync("o-codigo", "https://crm/retorno", CancellationToken.None);

        Assert.Equal("abc123", token);
        var chamada = Assert.Single(manipulador.Chamadas);
        Assert.Equal("/api/v10/oauth2/token", chamada.Caminho);
        Assert.Contains("grant_type=authorization_code", chamada.Corpo);
        Assert.Contains("code=o-codigo", chamada.Corpo);
        Assert.Contains("client_id=app-1", chamada.Corpo);
        Assert.Contains("client_secret=segredo-1", chamada.Corpo);
        Assert.Contains("redirect_uri=https%3A%2F%2Fcrm%2Fretorno", chamada.Corpo);
    }

    [Fact]
    public async Task TrocarCodigo_RecusadoPeloDiscord_LancaErroSemExporOSegredo()
    {
        var (api, _) = Montar(_ => Json(HttpStatusCode.BadRequest, """{"error":"invalid_grant"}"""));

        var erro = await Assert.ThrowsAsync<DiscordApiException>(() => api.TrocarCodigoAsync("x", "https://crm/retorno", CancellationToken.None));

        Assert.DoesNotContain("segredo-1", erro.Message);
    }

    [Fact]
    public async Task ObterUsuario_UsaOTokenDoUsuario_EPrefereONomeDeExibicao()
    {
        var (api, manipulador) = Montar(_ => Json(HttpStatusCode.OK, """{"id":"42","username":"ana_dev","global_name":"Ana Souza"}"""));

        var usuario = await api.ObterUsuarioAsync("token-do-usuario", CancellationToken.None);

        Assert.Equal(new DiscordUsuario("42", "Ana Souza"), usuario);
        Assert.Equal("Bearer token-do-usuario", Assert.Single(manipulador.Chamadas).Autorizacao);
    }

    [Fact]
    public async Task ObterUsuario_SemNomeDeExibicao_UsaOUsuario()
    {
        var (api, _) = Montar(_ => Json(HttpStatusCode.OK, """{"id":"42","username":"ana_dev","global_name":null}"""));

        Assert.Equal("ana_dev", (await api.ObterUsuarioAsync("t", CancellationToken.None)).Nome);
    }

    [Theory]
    [InlineData(HttpStatusCode.Created, true)]
    [InlineData(HttpStatusCode.NoContent, true)]
    [InlineData(HttpStatusCode.Forbidden, false)]
    public async Task AdicionarAoServidor_UsaOBot_ETrataOsCodigos(HttpStatusCode status, bool esperado)
    {
        var (api, manipulador) = Montar(_ => new HttpResponseMessage(status));

        var entrou = await api.AdicionarAoServidorAsync("42", "token-do-usuario", CancellationToken.None);

        Assert.Equal(esperado, entrou);
        var chamada = Assert.Single(manipulador.Chamadas);
        Assert.Equal(HttpMethod.Put, chamada.Metodo);
        Assert.Equal("/api/v10/guilds/servidor-9/members/42", chamada.Caminho);
        Assert.Equal("Bot token-do-bot", chamada.Autorizacao);
        Assert.Contains("token-do-usuario", chamada.Corpo);
    }

    [Fact]
    public async Task EnviarMensagemDireta_AbreAConversa_EEnviaOAvisoComoEmbed()
    {
        var (api, manipulador) = Montar(c => c.Caminho.EndsWith("/users/@me/channels")
            ? Json(HttpStatusCode.OK, """{"id":"canal-77"}""")
            : Json(HttpStatusCode.OK, """{"id":"msg-1"}"""));

        var resultado = await api.EnviarMensagemDiretaAsync("42", new DiscordAviso("Novo lead", "Maria · AGV", "https://crm/app/portal"), CancellationToken.None);

        Assert.Equal(ResultadoEnvioDiscord.Enviada, resultado);
        Assert.Equal(2, manipulador.Chamadas.Count);
        Assert.Contains("\"recipient_id\":\"42\"", manipulador.Chamadas[0].Corpo);
        Assert.Equal("/api/v10/channels/canal-77/messages", manipulador.Chamadas[1].Caminho);
        Assert.All(manipulador.Chamadas, c => Assert.Equal("Bot token-do-bot", c.Autorizacao));
        using var corpo = JsonDocument.Parse(manipulador.Chamadas[1].Corpo);
        var embed = corpo.RootElement.GetProperty("embeds")[0];
        Assert.Equal("Novo lead", embed.GetProperty("title").GetString());
        Assert.Equal("Maria · AGV", embed.GetProperty("description").GetString());
        Assert.Equal("https://crm/app/portal", embed.GetProperty("url").GetString());
    }

    [Fact]
    public async Task EnviarMensagemDireta_ComMensagensDiretasBloqueadas_DevolveDmFechada()
    {
        var (api, manipulador) = Montar(_ => Json(HttpStatusCode.Forbidden, """{"code":50007,"message":"Cannot send messages to this user"}"""));

        var resultado = await api.EnviarMensagemDiretaAsync("42", new DiscordAviso("t", "c", null), CancellationToken.None);

        Assert.Equal(ResultadoEnvioDiscord.DmFechada, resultado);
        Assert.Single(manipulador.Chamadas); // nem tenta enviar a mensagem
    }

    [Fact]
    public async Task EnviarMensagemDireta_ComErroDoDiscord_DevolveErro_SemLancar()
    {
        var (api, _) = Montar(_ => Json(HttpStatusCode.InternalServerError, "{}"));

        Assert.Equal(ResultadoEnvioDiscord.Erro, await api.EnviarMensagemDiretaAsync("42", new DiscordAviso("t", "c", null), CancellationToken.None));
    }

    [Fact]
    public async Task LimiteDeRequisicoes_EsperaOTempoPedido_ETentaDeNovo()
    {
        var tentativas = 0;
        var (api, manipulador) = Montar(_ => ++tentativas == 1
            ? Json(HttpStatusCode.TooManyRequests, """{"message":"You are being rate limited.","retry_after":0.1,"global":false}""")
            : Json(HttpStatusCode.OK, """{"id":"42","username":"ana"}"""));

        var usuario = await api.ObterUsuarioAsync("t", CancellationToken.None);

        Assert.Equal("42", usuario.Id);
        Assert.Equal(2, manipulador.Chamadas.Count);
    }

    [Fact]
    public async Task MensagemLonga_EhCortadaNoLimiteDoDiscord()
    {
        var (api, manipulador) = Montar(c => c.Caminho.EndsWith("/users/@me/channels") ? Json(HttpStatusCode.OK, """{"id":"c1"}""") : Json(HttpStatusCode.OK, "{}"));

        await api.EnviarMensagemDiretaAsync("42", new DiscordAviso(new string('T', 500), new string('C', 9000), null), CancellationToken.None);

        using var corpo = JsonDocument.Parse(manipulador.Chamadas[1].Corpo);
        var embed = corpo.RootElement.GetProperty("embeds")[0];
        Assert.Equal(256, embed.GetProperty("title").GetString()!.Length);
        Assert.Equal(4000, embed.GetProperty("description").GetString()!.Length);
    }
}
