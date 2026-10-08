using CssVision.Web.Authorization;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Domain.Identity;
using CssVision.Web.Services.Crm;
using CssVision.Web.Services.Discord;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Personalização do servidor: cores e agrupamento dos cargos, apelidos, boas-vindas fixada e avisos em cartão.</summary>
public class DiscordPersonalizacaoTests
{
    private static DiscordOptions Configurado() => new()
    {
        BotToken = "bot", ClientId = "app-1", ClientSecret = "segredo", GuildId = "servidor-1", UrlPublica = "https://crm.exemplo.com/",
    };

    private static DiscordGruposService Grupos(ApplicationDbContext db, DiscordServidorFalso servidor) =>
        new(db, servidor, Options.Create(Configurado()), NullLogger<DiscordGruposService>.Instance);

    // ---------------- cores e agrupamento dos cargos

    [Fact]
    public async Task Cargos_CadaRegionalTemSuaCor_ApareceSeparadaNaLista_EOsGruposUsamACorDaRegional()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var mg132 = await factory.CriarRegionalAsync(db, "MG132");
        var mg134 = await factory.CriarRegionalAsync(db, "MG134");
        await factory.CriarGrupoAsync(db, mg132.Id, "Growth Sales");
        var servidor = new DiscordServidorFalso();

        await Grupos(db, servidor).SincronizarAsync(CancellationToken.None);

        DiscordAparenciaCargo? De(string nomeDoCargo) => servidor.AparenciasDeCargo[servidor.NomesDeCargo.Single(c => c.Value == nomeDoCargo).Key];

        var geral = De("CRM · Todos")!;
        var gestao = De("CRM · Gestão")!;
        var r132 = De("CRM · MG132")!;
        var r134 = De("CRM · MG134")!;
        var grupo = De("CRM · MG132 / Growth Sales")!;

        Assert.Equal(DiscordGruposService.CorGeral, geral.Cor);
        Assert.False(geral.Destacar);
        Assert.Equal(DiscordGruposService.CorGestao, gestao.Cor);
        Assert.True(gestao.Destacar);
        Assert.True(r132.Destacar);
        Assert.True(r134.Destacar);
        Assert.NotEqual(r132.Cor, r134.Cor);               // duas regionais, duas cores
        Assert.Equal(r132.Cor, grupo.Cor);                 // o grupo herda a cor da regional dele
        Assert.False(grupo.Destacar);                      // só a regional aparece separada
        Assert.DoesNotContain(DiscordGruposService.CorGestao, DiscordGruposService.CoresDasRegionais); // o roxo é só da gestão
    }

    // ---------------- apelidos

    [Theory]
    [InlineData("Ana Melo", "Ana Melo")]
    [InlineData("  Ana    Melo  ", "Ana Melo")]
    [InlineData("Maria Aparecida dos Santos Figueiredo Lima", "Maria Lima")]
    [InlineData("Anastácio de Nascimento Albuquerque Montenegro Vasconcelos", "Anastácio Vasconcelos")]
    public void ApelidoNoDiscord_UsaONomeDoCrm_EEncurtaQuandoPassaDe32(string nome, string esperado) =>
        Assert.Equal(esperado, DiscordGruposService.ApelidoNoDiscord(nome));

    [Fact]
    public void ApelidoNoDiscord_NuncaPassaDe32Caracteres()
    {
        var apelido = DiscordGruposService.ApelidoNoDiscord("Pneumoultramicroscopicossilicovulcanoconiótico Hipopotomonstrosesquipedaliofobia");

        Assert.True(apelido.Length <= 32);
        Assert.NotEmpty(apelido);
    }

    private static async Task<(ApplicationUser Usuario, string DiscordId)> MembroAsync(TestDbContextFactory factory, ApplicationDbContext db, DiscordServidorFalso servidor, string nome, string? apelidoAtual = null, bool ativo = true)
    {
        var u = await factory.CriarUsuarioAsync(db, nome);
        u.Ativo = ativo;
        await db.SaveChangesAsync();
        var discordId = $"d-{nome.ToLowerInvariant().Replace(' ', '-')}";
        db.CrmDiscordVinculos.Add(new CrmDiscordVinculo { UsuarioId = u.Id, DiscordUserId = discordId, DiscordNome = "usuario123", NoServidor = true, VinculadoEm = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        servidor.Membros[discordId] = [];
        if (apelidoAtual is not null) servidor.Apelidos[discordId] = apelidoAtual;
        return (u, discordId);
    }

    [Fact]
    public async Task Apelido_VirouONomeDoCrm_SoParaQuemAindaNaoTem_ENaoSobrescreveOEscolhido()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var servidor = new DiscordServidorFalso();
        var (_, ana) = await MembroAsync(factory, db, servidor, "Ana Melo");
        var (_, bia) = await MembroAsync(factory, db, servidor, "Bia Lima", apelidoAtual: "Bia do Comercial");

        var r = await Grupos(db, servidor).SincronizarAsync(CancellationToken.None);

        Assert.Equal(1, r.ApelidosDefinidos);
        Assert.Equal("Ana Melo", servidor.Apelidos[ana]);
        Assert.Equal("Bia do Comercial", servidor.Apelidos[bia]); // o que a pessoa escolheu continua
    }

    [Fact]
    public async Task Apelido_NaoSeRepete_NasSincronizacoesSeguintes()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var servidor = new DiscordServidorFalso();
        await MembroAsync(factory, db, servidor, "Ana Melo");
        var servico = Grupos(db, servidor);

        await servico.SincronizarAsync(CancellationToken.None);
        var segunda = await servico.SincronizarAsync(CancellationToken.None);

        Assert.Equal(0, segunda.ApelidosDefinidos);
        Assert.Single(servidor.ApelidosDefinidos);
    }

    [Fact]
    public async Task Apelido_QuemFoiInativadoNoCrmNaoGanhaApelido()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var servidor = new DiscordServidorFalso();
        await MembroAsync(factory, db, servidor, "Ana Melo", ativo: false);

        var r = await Grupos(db, servidor).SincronizarAsync(CancellationToken.None);

        Assert.Equal(0, r.ApelidosDefinidos);
        Assert.Empty(servidor.ApelidosDefinidos);
    }

    [Fact]
    public async Task Apelido_SemPermissao_NaoViraFalha_ENaoTravaOsCargos()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var servidor = new DiscordServidorFalso { SemPermissaoParaApelidos = true };
        await MembroAsync(factory, db, servidor, "Ana Melo");

        var r = await Grupos(db, servidor).SincronizarAsync(CancellationToken.None);

        Assert.Empty(r.Falhas);            // o dono do servidor ou falta de "Gerenciar apelidos": não é erro da sincronização
        Assert.Equal(0, r.ApelidosDefinidos);
        Assert.Equal(1, r.MembrosAtualizados); // e os cargos foram dados normalmente
    }

    // ---------------- boas-vindas fixada

    [Fact]
    public async Task BoasVindas_PublicadaEFixadaNoGeral_UmaSoVez()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        await factory.CriarRegionalAsync(db, "MG132");
        var servidor = new DiscordServidorFalso();
        var servico = Grupos(db, servidor);

        var primeira = await servico.SincronizarAsync(CancellationToken.None);
        var segunda = await servico.SincronizarAsync(CancellationToken.None);

        Assert.True(primeira.BoasVindasPublicadas);
        Assert.False(segunda.BoasVindasPublicadas);
        var geral = (await db.CrmDiscordCanais.AsNoTracking().SingleAsync(c => c.Chave == "geral")).DiscordCanalId;
        var (canal, cartao) = Assert.Single(servidor.Cartoes);
        Assert.Equal(geral, canal);
        Assert.Contains("Bem-vindo", cartao.Titulo);
        Assert.Contains(cartao.Campos!, c => c.Valor.Contains("https://crm.exemplo.com/app/chat"));
        Assert.Equal(geral, Assert.Single(servidor.Fixadas).CanalId);
    }

    [Fact]
    public async Task BoasVindas_SeNaoDerParaFixar_PublicaMesmoAssim_ENaoTentaDeNovo()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var servidor = new DiscordServidorFalso { SemPermissaoParaFixar = true };
        var servico = Grupos(db, servidor);

        var primeira = await servico.SincronizarAsync(CancellationToken.None);
        await servico.SincronizarAsync(CancellationToken.None);

        Assert.True(primeira.BoasVindasPublicadas);
        Assert.Empty(primeira.Falhas);
        Assert.Single(servidor.Cartoes); // não encheu o canal de mensagens repetidas
        Assert.Empty(servidor.Fixadas);
    }

    [Fact]
    public async Task BoasVindas_SeAPublicacaoFalhar_ApareceNasFalhas_ETentaNaProximaVez()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var servidor = new DiscordServidorFalso();
        var servico = Grupos(db, servidor);
        await servico.SincronizarAsync(CancellationToken.None);
        db.CrmParametros.RemoveRange(await db.CrmParametros.Where(p => p.Chave == "discord:boas-vindas").ToListAsync());
        await db.SaveChangesAsync();
        servidor.Cartoes.Clear();

        servidor.SemPermissao = true;
        var falhou = await servico.SincronizarAsync(CancellationToken.None);
        servidor.SemPermissao = false;
        var depois = await servico.SincronizarAsync(CancellationToken.None);

        Assert.False(falhou.BoasVindasPublicadas);
        Assert.NotEmpty(falhou.Falhas);
        Assert.True(depois.BoasVindasPublicadas);
    }

    // ---------------- cartões dos avisos nos canais

    private sealed class RelogioFalso(DateTimeOffset inicio) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => inicio;
    }

    [Fact]
    public async Task CartaoDeVenda_TemTituloCorECampos()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var mg132 = await factory.CriarRegionalAsync(db, "MG132");
        var servidor = new DiscordServidorFalso();
        await Grupos(db, servidor).SincronizarAsync(CancellationToken.None);
        servidor.Cartoes.Clear();
        var ganho = await factory.CriarEtapaAsync(db, "Ganho", 9, TipoEtapaPipeline.Ganho);
        var ana = await factory.CriarUsuarioAsync(db, "Ana");
        ana.RegionalId = mg132.Id;
        await db.SaveChangesAsync();
        var lead = new CrmLead { NomeOuRazaoSocial = "Cliente", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = ana.Id };
        db.CrmLeads.Add(lead);
        await db.SaveChangesAsync();
        var agora = new DateTimeOffset(2026, 10, 7, 16, 0, 0, TimeSpan.Zero);
        var venda = new CrmOpportunity { LeadId = lead.Id, Titulo = "V", ResponsavelId = ana.Id, EtapaId = ganho.Id, PagamentoAdesao = 300m, DataEfetivaFechamento = agora };
        db.CrmOpportunities.Add(venda);
        await db.SaveChangesAsync();
        var avisos = new DiscordAvisosNosCanaisService(db, servidor, Options.Create(Configurado()), NullLogger<DiscordAvisosNosCanaisService>.Instance, new RelogioFalso(agora));
        await avisos.DefinirConfiguracaoAsync(new CssVision.Web.Api.Contracts.Crm.DiscordAvisosCanaisDto(true, false, false), CancellationToken.None);

        await avisos.PublicarVendaAsync(venda.Id, CancellationToken.None);

        var (_, cartao) = Assert.Single(servidor.Cartoes);
        Assert.Contains("Venda fechada", cartao.Titulo);
        Assert.Equal("**Ana** fechou uma venda.", cartao.Descricao);
        Assert.Equal(0x2ECC71, cartao.Cor);
        Assert.Equal(["Regional", "Adesão"], cartao.Campos!.Select(c => c.Nome).ToList());
        Assert.Equal("MG132", cartao.Campos![0].Valor);
        Assert.Contains("300,00", cartao.Campos![1].Valor);
        Assert.DoesNotContain("Cliente", cartao.Descricao + string.Concat(cartao.Campos.Select(c => c.Valor))); // nada do cliente
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

    private static HttpResponseMessage Json(System.Net.HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

    private static (DiscordGuildApi Api, ManipuladorFalso Manipulador) MontarApi(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var manipulador = new ManipuladorFalso(responder);
        var http = new HttpClient(manipulador) { BaseAddress = new Uri(DiscordApi.UrlBase) };
        var opcoes = Options.Create(new DiscordOptions { BotToken = "token-do-bot", ClientId = "app-1", ClientSecret = "s", GuildId = "servidor-9" });
        return (new DiscordGuildApi(http, opcoes, NullLogger<DiscordGuildApi>.Instance), manipulador);
    }

    [Fact]
    public async Task Http_Cartao_VaiComoEmbedComCorCamposRodape_ESemMarcarNinguem()
    {
        var (api, manipulador) = MontarApi(_ => Json(System.Net.HttpStatusCode.OK, """{"id":"m1","content":"","timestamp":"2026-10-07T18:00:00+00:00","author":{"id":"bot","username":"CRM"}}"""));

        await api.PublicarCartaoAsync("canal-1", new DiscordCartao("🎉 Venda fechada!", "**Ana** fechou uma venda.", 0x2ECC71, [new DiscordCampo("Regional", "MG132"), new DiscordCampo("Adesão", "R$ 300,00", Lado: false), new DiscordCampo("Vazio", "")], "CRM CSS Brasil"), CancellationToken.None);

        var chamada = Assert.Single(manipulador.Chamadas);
        Assert.Equal("/api/v10/channels/canal-1/messages", chamada.Caminho);
        Assert.Equal("Bot token-do-bot", chamada.Autorizacao);
        using var json = System.Text.Json.JsonDocument.Parse(chamada.Corpo); // o serializador escreve acentos como \uXXXX: confere lendo o JSON
        var embed = json.RootElement.GetProperty("embeds")[0];
        Assert.Equal("🎉 Venda fechada!", embed.GetProperty("title").GetString());
        Assert.Equal(0x2ECC71, embed.GetProperty("color").GetInt32());
        Assert.Equal("CRM CSS Brasil", embed.GetProperty("footer").GetProperty("text").GetString());
        var campos = embed.GetProperty("fields").EnumerateArray().ToList();
        Assert.Equal(["Regional", "Adesão", "Vazio"], campos.Select(c => c.GetProperty("name").GetString()).ToList());
        Assert.True(campos[0].GetProperty("inline").GetBoolean());
        Assert.False(campos[1].GetProperty("inline").GetBoolean());
        Assert.Equal("R$ 300,00", campos[1].GetProperty("value").GetString());
        Assert.Equal("-", campos[2].GetProperty("value").GetString()); // campo vazio o Discord recusa: vai um traço
        Assert.Contains("\"parse\":[]", chamada.Corpo);
    }

    [Fact]
    public async Task Http_FixarMensagem_UsaOEndpointDePins()
    {
        var (api, manipulador) = MontarApi(_ => new HttpResponseMessage(System.Net.HttpStatusCode.NoContent));

        await api.FixarMensagemAsync("canal-1", "555", CancellationToken.None);

        var chamada = Assert.Single(manipulador.Chamadas);
        Assert.Equal(HttpMethod.Put, chamada.Metodo);
        Assert.Equal("/api/v10/channels/canal-1/pins/555", chamada.Caminho);
    }

    [Fact]
    public async Task Http_Apelido_PatchNoMembro_CortadoEm32_ELeituraDoMembroTrazOApelido()
    {
        var (api, manipulador) = MontarApi(r => r.Method == HttpMethod.Get
            ? Json(System.Net.HttpStatusCode.OK, """{"roles":["r1","r2"],"nick":"Ana do Comercial"}""")
            : new HttpResponseMessage(System.Net.HttpStatusCode.NoContent));

        var membro = await api.ObterMembroAsync("u1", CancellationToken.None);
        await api.DefinirApelidoAsync("u1", new string('A', 50), CancellationToken.None);

        Assert.Equal(["r1", "r2"], membro!.Cargos.ToList());
        Assert.Equal("Ana do Comercial", membro.Apelido);
        var patch = manipulador.Chamadas.Single(c => c.Metodo == HttpMethod.Patch);
        Assert.Equal("/api/v10/guilds/servidor-9/members/u1", patch.Caminho);
        Assert.Contains($"\"nick\":\"{new string('A', 32)}\"", patch.Corpo);
    }

    [Fact]
    public async Task Http_Membro_SemApelido_ApelidoNulo_ESemMembro_Nulo()
    {
        var (api, _) = MontarApi(r => r.RequestUri!.AbsolutePath.EndsWith("/u-fora")
            ? Json(System.Net.HttpStatusCode.NotFound, """{"code":10007}""")
            : Json(System.Net.HttpStatusCode.OK, """{"roles":[],"nick":null}"""));

        Assert.Null((await api.ObterMembroAsync("u-sem-apelido", CancellationToken.None))!.Apelido);
        Assert.Null(await api.ObterMembroAsync("u-fora", CancellationToken.None));
    }

    [Fact]
    public async Task Http_CriarCargo_ComCorEDestaque()
    {
        var (api, manipulador) = MontarApi(_ => Json(System.Net.HttpStatusCode.OK, """{"id":"r9"}"""));

        await api.CriarCargoAsync("CRM · MG132", CancellationToken.None, new DiscordAparenciaCargo(0x3498DB, Destacar: true));

        var corpo = Assert.Single(manipulador.Chamadas).Corpo;
        Assert.Contains("\"color\":3447003", corpo); // 0x3498DB
        Assert.Contains("\"hoist\":true", corpo);
    }
}
