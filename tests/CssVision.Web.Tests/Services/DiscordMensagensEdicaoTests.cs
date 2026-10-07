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

/// <summary>Menções "@pessoa", editar e apagar a própria mensagem.</summary>
public class DiscordMensagensEdicaoTests
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

        public string CanalGeral() => Db.CrmDiscordCanais.Single(x => x.Chave == "geral").DiscordCanalId;
    }

    private static async Task<Cenario> MontarAsync(TestDbContextFactory factory)
    {
        var db = factory.CreateContext();
        var mg132 = await factory.CriarRegionalAsync(db, "MG132");
        var servidor = new DiscordServidorFalso();
        await new DiscordGruposService(db, servidor, Options.Create(Configurado()), NullLogger<DiscordGruposService>.Instance).SincronizarAsync(CancellationToken.None);
        return new Cenario { Factory = factory, Db = db, Servidor = servidor, Mg132 = mg132 };
    }

    private static async Task<ApplicationUser> PessoaAsync(Cenario c, string nome, bool vinculada = true, bool noServidor = true)
    {
        var u = await c.Factory.CriarUsuarioAsync(c.Db, nome);
        u.RegionalId = c.Mg132.Id;
        await c.Db.SaveChangesAsync();
        await c.Factory.AtribuirPapelAsync(c.Db, u, Roles.Comercial);
        if (vinculada)
        {
            c.Db.CrmDiscordVinculos.Add(new CrmDiscordVinculo { UsuarioId = u.Id, DiscordUserId = $"d-{nome.ToLowerInvariant().Replace(' ', '-')}", DiscordNome = nome, NoServidor = noServidor, VinculadoEm = DateTimeOffset.UtcNow });
            await c.Db.SaveChangesAsync();
        }

        return u;
    }

    // ---------------- menções

    [Fact]
    public async Task Mencao_TrocaOArrobaNomePelaMencaoDoDiscord_EMarcaSoEssaPessoa()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var ana = await PessoaAsync(c, "Ana");
        var bia = await PessoaAsync(c, "Bia Lima");

        await c.Servico().EnviarAsync(ana.Id, "geral", "oi @Bia Lima, vê isso?", CancellationToken.None, [bia.Id]);

        var envio = Assert.Single(c.Servidor.Enviadas);
        Assert.Equal("oi <@d-bia-lima>, vê isso?", envio.Texto);
        Assert.Equal(["d-bia-lima"], Assert.Single(c.Servidor.MencoesEnviadas));
    }

    [Fact]
    public async Task Mencao_NomeMaiorTemPrioridade_AnaMariaNaoViraAnaMaisMaria()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var eu = await PessoaAsync(c, "Eu");
        var ana = await PessoaAsync(c, "Ana");
        var anaMaria = await PessoaAsync(c, "Ana Maria");

        await c.Servico().EnviarAsync(eu.Id, "geral", "@Ana Maria e @Ana", CancellationToken.None, [ana.Id, anaMaria.Id]);

        Assert.Equal("<@d-ana-maria> e <@d-ana>", Assert.Single(c.Servidor.Enviadas).Texto);
    }

    [Fact]
    public async Task Mencao_IgnoraQuemNaoVinculouOuNaoEstaNoServidor_EQuemNaoFoiMarcadoNoTexto()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var ana = await PessoaAsync(c, "Ana");
        var semDiscord = await PessoaAsync(c, "Sem", vinculada: false);
        var fora = await PessoaAsync(c, "Fora", noServidor: false);
        var naoCitada = await PessoaAsync(c, "Zé");

        await c.Servico().EnviarAsync(ana.Id, "geral", "@Sem e @Fora, oi", CancellationToken.None, [semDiscord.Id, fora.Id, naoCitada.Id]);

        Assert.Equal("@Sem e @Fora, oi", Assert.Single(c.Servidor.Enviadas).Texto); // sem troca: continua texto
        Assert.Empty(c.Servidor.MencoesEnviadas);                                    // e ninguém é marcado
    }

    [Fact]
    public async Task Mencao_SemMencoes_ComportaComoAntes()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var ana = await PessoaAsync(c, "Ana");

        await c.Servico().EnviarAsync(ana.Id, "geral", "oi @todos", CancellationToken.None);

        Assert.Equal("oi @todos", Assert.Single(c.Servidor.Enviadas).Texto);
        Assert.Empty(c.Servidor.MencoesEnviadas);
    }

    // ---------------- editar e apagar

    private static async Task<(Cenario C, ApplicationUser Ana, DiscordMensagem Minha)> ComMensagemAsync(TestDbContextFactory factory)
    {
        var c = await MontarAsync(factory);
        var ana = await PessoaAsync(c, "Ana");
        var enviada = await c.Servico().EnviarAsync(ana.Id, "geral", "texto original", CancellationToken.None);
        var minha = c.Servidor.Mensagens[c.CanalGeral()].Single(m => m.Id == enviada.Id);
        return (c, ana, minha);
    }

    [Fact]
    public async Task Editar_TrocaOTextoDaPropriaMensagem()
    {
        using var factory = new TestDbContextFactory();
        var (c, ana, minha) = await ComMensagemAsync(factory);
        var servico = c.Servico();

        await servico.EditarMensagemAsync(ana.Id, "geral", minha.Id, "  texto corrigido  ", CancellationToken.None);

        Assert.Equal("texto corrigido", Assert.Single(c.Servidor.Edicoes).Texto);
        var lida = await servico.ListarMensagensAsync(ana.Id, "geral", null, CancellationToken.None);
        var m = Assert.Single(lida.Mensagens);
        Assert.Equal("texto corrigido", m.Conteudo);
        Assert.True(m.Editada);
    }

    [Fact]
    public async Task Apagar_RemoveAPropriaMensagem()
    {
        using var factory = new TestDbContextFactory();
        var (c, ana, minha) = await ComMensagemAsync(factory);
        var servico = c.Servico();

        await servico.ApagarMensagemAsync(ana.Id, "geral", minha.Id, CancellationToken.None);

        Assert.Single(c.Servidor.Apagadas);
        Assert.Empty((await servico.ListarMensagensAsync(ana.Id, "geral", null, CancellationToken.None)).Mensagens);
    }

    [Fact]
    public async Task EditarEApagar_RecusamMensagemDeOutraPessoa_ENaoMexemNoDiscord()
    {
        using var factory = new TestDbContextFactory();
        var (c, _, minha) = await ComMensagemAsync(factory);
        var bia = await PessoaAsync(c, "Bia");
        var servico = c.Servico();

        await Assert.ThrowsAsync<CrmForbiddenException>(() => servico.EditarMensagemAsync(bia.Id, "geral", minha.Id, "sequestrada", CancellationToken.None));
        await Assert.ThrowsAsync<CrmForbiddenException>(() => servico.ApagarMensagemAsync(bia.Id, "geral", minha.Id, CancellationToken.None));
        Assert.Empty(c.Servidor.Edicoes);
        Assert.Empty(c.Servidor.Apagadas);
    }

    [Fact]
    public async Task EditarEApagar_RecusamMensagemEscritaDiretoNoDiscord()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var ana = await PessoaAsync(c, "Ana");
        // Mensagem com o mesmo nome da Ana, mas escrita no Discord (não publicada pelo webhook do CRM).
        c.Servidor.Mensagens[c.CanalGeral()] = [new DiscordMensagem("900", "Ana", null, "escrita no discord", DateTimeOffset.UtcNow, [], DoCrm: false)];

        await Assert.ThrowsAsync<CrmForbiddenException>(() => c.Servico().EditarMensagemAsync(ana.Id, "geral", "900", "x", CancellationToken.None));
        await Assert.ThrowsAsync<CrmForbiddenException>(() => c.Servico().ApagarMensagemAsync(ana.Id, "geral", "900", CancellationToken.None));
    }

    [Fact]
    public async Task EditarEApagar_RecusamGrupoDeOutraPessoaEIdsEstranhos()
    {
        using var factory = new TestDbContextFactory();
        var (c, ana, minha) = await ComMensagemAsync(factory);
        var servico = c.Servico();

        await Assert.ThrowsAsync<CrmForbiddenException>(() => servico.EditarMensagemAsync(ana.Id, "gestao", minha.Id, "x", CancellationToken.None));
        foreach (var id in new[] { "", "abc", "123/../x", "1" + new string('0', 30) })
        {
            var ex = await Assert.ThrowsAsync<CrmBusinessException>(() => servico.ApagarMensagemAsync(ana.Id, "geral", id, CancellationToken.None));
            Assert.Equal("mensagem_invalida", ex.Codigo);
        }
    }

    [Fact]
    public async Task Editar_RecusaTextoVazioOuLongo()
    {
        using var factory = new TestDbContextFactory();
        var (c, ana, minha) = await ComMensagemAsync(factory);
        var servico = c.Servico();

        var vazio = await Assert.ThrowsAsync<CrmBusinessException>(() => servico.EditarMensagemAsync(ana.Id, "geral", minha.Id, "  ", CancellationToken.None));
        var longo = await Assert.ThrowsAsync<CrmBusinessException>(() => servico.EditarMensagemAsync(ana.Id, "geral", minha.Id, new string('x', DiscordChatService.LimiteDoTexto + 1), CancellationToken.None));

        Assert.Equal("mensagem_vazia", vazio.Codigo);
        Assert.Equal("mensagem_longa", longo.Codigo);
        Assert.Empty(c.Servidor.Edicoes);
    }

    [Fact]
    public async Task ConversaDireta_PermiteEditarEApagarNaThread()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var ana = await PessoaAsync(c, "Ana");
        var bia = await PessoaAsync(c, "Bia");
        var servico = c.Servico();
        var conversa = await servico.IniciarConversaAsync(ana.Id, bia.Id, CancellationToken.None);
        var enviada = await servico.EnviarAsync(ana.Id, conversa.Chave, "oi", CancellationToken.None);

        await servico.EditarMensagemAsync(ana.Id, conversa.Chave, enviada.Id, "oi, tudo bem?", CancellationToken.None);
        await Assert.ThrowsAsync<CrmForbiddenException>(() => servico.ApagarMensagemAsync(bia.Id, conversa.Chave, enviada.Id, CancellationToken.None));
        await servico.ApagarMensagemAsync(ana.Id, conversa.Chave, enviada.Id, CancellationToken.None);

        Assert.Equal(c.Servidor.Threads.Keys.Single(), Assert.Single(c.Servidor.Edicoes).Onde);
        Assert.Single(c.Servidor.Apagadas);
    }

    // ---------------- camada HTTP

    private sealed class ManipuladorFalso(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<(HttpMethod Metodo, string Caminho, string? Autorizacao, string Corpo)> Chamadas { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var corpo = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Chamadas.Add((request.Method, request.RequestUri!.PathAndQuery, request.Headers.Authorization?.ToString(), corpo));
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

    private static HttpResponseMessage Webhooks(HttpRequestMessage r) =>
        r.Method == HttpMethod.Get ? Json(HttpStatusCode.OK, "[]") : Json(HttpStatusCode.OK, """{"id":"wz","token":"tkz"}""");

    [Fact]
    public async Task Http_Mencao_PermiteSoAsPessoasMarcadas()
    {
        var canal = $"canal-{Guid.NewGuid():N}";
        var (api, manipulador) = MontarApi(r => r.RequestUri!.AbsolutePath.EndsWith("/webhooks") ? Webhooks(r)
            : Json(HttpStatusCode.OK, """{"id":"m1","content":"oi","timestamp":"2026-10-07T18:00:00+00:00","author":{"id":"wz","username":"Ana"}}"""));

        await api.EnviarMensagemAsync(canal, "Ana", null, "oi <@111>", CancellationToken.None, mencionar: ["111", "111", "222"]);

        var envio = manipulador.Chamadas.Single(c => c.Caminho.StartsWith("/api/v10/webhooks/wz/tkz"));
        Assert.Contains("\"parse\":[]", envio.Corpo);               // nunca @everyone/@here/cargos
        Assert.Contains("\"users\":[\"111\",\"222\"]", envio.Corpo); // só quem foi escolhido (sem repetir)
    }

    [Fact]
    public async Task Http_Editar_UsaPatchNoWebhook_ComThreadIdQuandoHouver()
    {
        var canal = $"canal-{Guid.NewGuid():N}";
        var (api, manipulador) = MontarApi(r => r.RequestUri!.AbsolutePath.EndsWith("/webhooks") ? Webhooks(r) : Json(HttpStatusCode.OK, "{}"));

        await api.EditarMensagemAsync(canal, "thread-5", "777", "novo texto", CancellationToken.None);

        var chamada = manipulador.Chamadas.Single(c => c.Metodo == HttpMethod.Patch);
        Assert.Equal("/api/v10/webhooks/wz/tkz/messages/777?thread_id=thread-5", chamada.Caminho);
        Assert.Contains("\"content\":\"novo texto\"", chamada.Corpo);
        Assert.Null(chamada.Autorizacao); // o webhook se autentica pelo token na URL
    }

    [Fact]
    public async Task Http_Apagar_UsaDeleteNoWebhook()
    {
        var canal = $"canal-{Guid.NewGuid():N}";
        var (api, manipulador) = MontarApi(r => r.RequestUri!.AbsolutePath.EndsWith("/webhooks") ? Webhooks(r) : new HttpResponseMessage(HttpStatusCode.NoContent));

        await api.ApagarMensagemAsync(canal, null, "778", CancellationToken.None);

        var chamada = manipulador.Chamadas.Single(c => c.Metodo == HttpMethod.Delete);
        Assert.Equal("/api/v10/webhooks/wz/tkz/messages/778", chamada.Caminho);
    }

    [Fact]
    public async Task Http_ReconheceMensagemDoCrmPeloDonoDoWebhook_MesmoSemOWebhookEmMemoria()
    {
        var (api, _) = MontarApi(_ => Json(HttpStatusCode.OK, """
            [
              {"id":"2","content":"minha","timestamp":"2026-10-07T18:00:02+00:00","webhook_id":"qualquer-w","application_id":"app-1","edited_timestamp":"2026-10-07T18:05:00+00:00","author":{"id":"qualquer-w","username":"Ana"}},
              {"id":"1","content":"de outro webhook","timestamp":"2026-10-07T18:00:01+00:00","webhook_id":"outro-w","application_id":"outro-app","author":{"id":"outro-w","username":"Github"}}
            ]
            """));

        var mensagens = await api.ListarMensagensAsync($"canal-{Guid.NewGuid():N}", 50, null, CancellationToken.None);

        Assert.False(mensagens[0].DoCrm); // "Github": webhook de outra aplicação
        Assert.False(mensagens[0].Editada);
        Assert.True(mensagens[1].DoCrm);  // do webhook do CRM (aplicação app-1), mesmo sem estar em memória
        Assert.True(mensagens[1].Editada);
    }
}
