using System.Web;
using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Data;
using CssVision.Web.Services.Discord;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Vínculo da conta do Discord com o usuário do CRM (OAuth2) e envio dos avisos como mensagem direta.</summary>
public class DiscordServiceTests
{
    private sealed class DiscordFalso : IDiscordApi
    {
        public string ContaId { get; set; } = "111";
        public string ContaNome { get; set; } = "Ana no Discord";
        public bool EntraNoServidor { get; set; } = true;
        public Exception? FalhaNaTroca { get; set; }
        public ResultadoEnvioDiscord Resultado { get; set; } = ResultadoEnvioDiscord.Enviada;
        public List<(string DiscordUserId, DiscordAviso Aviso)> Envios { get; } = [];
        public int AdicionadosAoServidor { get; private set; }

        public Task<string> TrocarCodigoAsync(string code, string redirectUri, CancellationToken ct) =>
            FalhaNaTroca is null ? Task.FromResult($"token-de-{code}") : throw FalhaNaTroca;

        public Task<DiscordUsuario> ObterUsuarioAsync(string accessToken, CancellationToken ct) => Task.FromResult(new DiscordUsuario(ContaId, ContaNome));

        public Task<bool> AdicionarAoServidorAsync(string discordUserId, string accessToken, CancellationToken ct)
        {
            AdicionadosAoServidor++;
            return Task.FromResult(EntraNoServidor);
        }

        public Task<ResultadoEnvioDiscord> EnviarMensagemDiretaAsync(string discordUserId, DiscordAviso aviso, CancellationToken ct)
        {
            Envios.Add((discordUserId, aviso));
            return Task.FromResult(Resultado);
        }
    }

    private static DiscordOptions Configurado() => new()
    {
        BotToken = "bot", ClientId = "app-1", ClientSecret = "segredo", GuildId = "servidor-1", UrlPublica = "https://crm.exemplo.com/",
    };

    private static (DiscordService Servico, DiscordFalso Api, ApplicationDbContext Db) Montar(TestDbContextFactory factory, DiscordOptions? opcoes = null)
    {
        var db = factory.CreateContext();
        var api = new DiscordFalso();
        var servico = new DiscordService(db, api, new MemoryCache(new MemoryCacheOptions()), Options.Create(opcoes ?? Configurado()), NullLogger<DiscordService>.Instance);
        return (servico, api, db);
    }

    private const string Retorno = "https://crm.exemplo.com/api/crm/discord/callback";

    private static string EstadoDaUrl(string url) => HttpUtility.ParseQueryString(new Uri(url).Query)["state"]!;

    [Fact]
    public async Task IniciarVinculo_MontaOEnderecoDeAutorizacaoDoDiscord()
    {
        using var factory = new TestDbContextFactory();
        var (servico, _, _) = Montar(factory);

        var url = await servico.IniciarVinculoAsync(Guid.NewGuid(), Retorno, CancellationToken.None);

        var consulta = HttpUtility.ParseQueryString(new Uri(url).Query);
        Assert.StartsWith("https://discord.com/oauth2/authorize", url);
        Assert.Equal("app-1", consulta["client_id"]);
        Assert.Equal("code", consulta["response_type"]);
        Assert.Equal(Retorno, consulta["redirect_uri"]);
        Assert.Equal("identify guilds.join", consulta["scope"]);
        Assert.False(string.IsNullOrWhiteSpace(consulta["state"]));
        Assert.DoesNotContain("segredo", url); // o segredo do OAuth2 nunca vai no endereço
    }

    [Fact]
    public async Task SemCredenciais_AIntegracaoFicaDesligada()
    {
        using var factory = new TestDbContextFactory();
        var (servico, _, _) = Montar(factory, new DiscordOptions());

        var erro = await Assert.ThrowsAsync<CrmBusinessException>(() => servico.IniciarVinculoAsync(Guid.NewGuid(), Retorno, CancellationToken.None));
        Assert.Equal("discord_nao_configurado", erro.Codigo);
        Assert.False((await servico.ObterStatusAsync(Guid.NewGuid(), CancellationToken.None)).Configurado);
        Assert.False(await servico.EnviarAvisoAsync(Guid.NewGuid(), "t", "c", null, CancellationToken.None));
    }

    [Fact]
    public async Task ConcluirVinculo_GravaAContaEColocaNoServidor()
    {
        using var factory = new TestDbContextFactory();
        var (servico, api, db) = Montar(factory);
        var usuario = Guid.NewGuid();
        var state = EstadoDaUrl(await servico.IniciarVinculoAsync(usuario, Retorno, CancellationToken.None));

        var status = await servico.ConcluirVinculoAsync("codigo", state, Retorno, CancellationToken.None);

        Assert.True(status.Vinculado);
        Assert.Equal("Ana no Discord", status.DiscordNome);
        Assert.True(status.AvisosAtivos);
        Assert.True(status.NoServidor);
        Assert.Equal(1, api.AdicionadosAoServidor);
        var salvo = await db.CrmDiscordVinculos.AsNoTracking().SingleAsync();
        Assert.Equal(usuario, salvo.UsuarioId);
        Assert.Equal("111", salvo.DiscordUserId);
    }

    [Fact]
    public async Task EstadoInvalido_OuUsadoDuasVezes_NaoVincula()
    {
        using var factory = new TestDbContextFactory();
        var (servico, _, db) = Montar(factory);

        var invalido = await Assert.ThrowsAsync<CrmBusinessException>(() => servico.ConcluirVinculoAsync("codigo", "inventado", Retorno, CancellationToken.None));
        Assert.Equal("discord_state_invalido", invalido.Codigo);

        var state = EstadoDaUrl(await servico.IniciarVinculoAsync(Guid.NewGuid(), Retorno, CancellationToken.None));
        await servico.ConcluirVinculoAsync("codigo", state, Retorno, CancellationToken.None);
        var repetido = await Assert.ThrowsAsync<CrmBusinessException>(() => servico.ConcluirVinculoAsync("codigo", state, Retorno, CancellationToken.None));
        Assert.Equal("discord_state_invalido", repetido.Codigo); // uso único
        Assert.Equal(1, await db.CrmDiscordVinculos.CountAsync());
    }

    [Fact]
    public async Task ContaDoDiscordJaVinculadaAOutroUsuario_EhRecusada()
    {
        using var factory = new TestDbContextFactory();
        var (servico, _, db) = Montar(factory);
        var primeiro = Guid.NewGuid();
        var segundo = Guid.NewGuid();
        await servico.ConcluirVinculoAsync("c1", EstadoDaUrl(await servico.IniciarVinculoAsync(primeiro, Retorno, CancellationToken.None)), Retorno, CancellationToken.None);

        var erro = await Assert.ThrowsAsync<CrmBusinessException>(async () =>
            await servico.ConcluirVinculoAsync("c2", EstadoDaUrl(await servico.IniciarVinculoAsync(segundo, Retorno, CancellationToken.None)), Retorno, CancellationToken.None));

        Assert.Equal("discord_conta_em_uso", erro.Codigo);
        Assert.Equal(primeiro, (await db.CrmDiscordVinculos.AsNoTracking().SingleAsync()).UsuarioId);
    }

    [Fact]
    public async Task VincularDeNovo_TrocaAContaDoMesmoUsuario_SemDuplicar()
    {
        using var factory = new TestDbContextFactory();
        var (servico, api, db) = Montar(factory);
        var usuario = Guid.NewGuid();
        await servico.ConcluirVinculoAsync("c1", EstadoDaUrl(await servico.IniciarVinculoAsync(usuario, Retorno, CancellationToken.None)), Retorno, CancellationToken.None);

        api.ContaId = "222";
        api.ContaNome = "Outra conta";
        await servico.ConcluirVinculoAsync("c2", EstadoDaUrl(await servico.IniciarVinculoAsync(usuario, Retorno, CancellationToken.None)), Retorno, CancellationToken.None);

        var salvo = await db.CrmDiscordVinculos.AsNoTracking().SingleAsync();
        Assert.Equal("222", salvo.DiscordUserId);
        Assert.Equal("Outra conta", salvo.DiscordNome);
    }

    [Fact]
    public async Task NaoConseguirColocarNoServidor_NaoDerrubaOVinculo()
    {
        using var factory = new TestDbContextFactory();
        var (servico, api, _) = Montar(factory);
        api.EntraNoServidor = false;

        var status = await servico.ConcluirVinculoAsync("c", EstadoDaUrl(await servico.IniciarVinculoAsync(Guid.NewGuid(), Retorno, CancellationToken.None)), Retorno, CancellationToken.None);

        Assert.True(status.Vinculado);
        Assert.False(status.NoServidor);
    }

    [Fact]
    public async Task DiscordRecusandoOCodigo_DaErroClaro_ENaoVincula()
    {
        using var factory = new TestDbContextFactory();
        var (servico, api, db) = Montar(factory);
        api.FalhaNaTroca = new DiscordApiException("O Discord recusou a autorização.");

        var erro = await Assert.ThrowsAsync<CrmBusinessException>(async () =>
            await servico.ConcluirVinculoAsync("c", EstadoDaUrl(await servico.IniciarVinculoAsync(Guid.NewGuid(), Retorno, CancellationToken.None)), Retorno, CancellationToken.None));

        Assert.Equal("discord_indisponivel", erro.Codigo);
        Assert.Empty(await db.CrmDiscordVinculos.ToListAsync());
    }

    [Fact]
    public async Task Desvincular_RemoveOVinculo_ENaoEnviaMais()
    {
        using var factory = new TestDbContextFactory();
        var (servico, api, db) = Montar(factory);
        var usuario = Guid.NewGuid();
        await servico.ConcluirVinculoAsync("c", EstadoDaUrl(await servico.IniciarVinculoAsync(usuario, Retorno, CancellationToken.None)), Retorno, CancellationToken.None);

        api.Envios.Clear(); // a boas-vindas do vínculo não é o que este teste confere

        await servico.DesvincularAsync(usuario, CancellationToken.None);

        Assert.Empty(await db.CrmDiscordVinculos.ToListAsync());
        Assert.False(await servico.EnviarAvisoAsync(usuario, "t", "c", null, CancellationToken.None));
        Assert.Empty(api.Envios);
    }

    [Fact]
    public async Task EnviarAviso_VaiParaAContaVinculada_ComLinkAbsoluto()
    {
        using var factory = new TestDbContextFactory();
        var (servico, api, _) = Montar(factory);
        var usuario = Guid.NewGuid();
        await servico.ConcluirVinculoAsync("c", EstadoDaUrl(await servico.IniciarVinculoAsync(usuario, Retorno, CancellationToken.None)), Retorno, CancellationToken.None);

        api.Envios.Clear(); // a boas-vindas do vínculo não é o que este teste confere
        var enviado = await servico.EnviarAvisoAsync(usuario, "Novo lead para você", "Maria · AGV", "/app/crm/leads/kanban?lead=1", CancellationToken.None);

        Assert.True(enviado);
        var (conta, aviso) = Assert.Single(api.Envios);
        Assert.Equal("111", conta);
        Assert.Equal("Novo lead para você", aviso.Titulo);
        Assert.Equal("https://crm.exemplo.com/app/crm/leads/kanban?lead=1", aviso.Url);
    }

    [Fact]
    public async Task EnviarAviso_RespeitaQuemDesligouOsAvisos_ESemVinculo()
    {
        using var factory = new TestDbContextFactory();
        var (servico, api, _) = Montar(factory);
        var usuario = Guid.NewGuid();

        Assert.False(await servico.EnviarAvisoAsync(usuario, "t", "c", null, CancellationToken.None)); // sem vínculo

        await servico.ConcluirVinculoAsync("c", EstadoDaUrl(await servico.IniciarVinculoAsync(usuario, Retorno, CancellationToken.None)), Retorno, CancellationToken.None);
        api.Envios.Clear(); // a boas-vindas do vínculo não é o que este teste confere
        await servico.DefinirAvisosAsync(usuario, false, CancellationToken.None);

        Assert.False(await servico.EnviarAvisoAsync(usuario, "t", "c", null, CancellationToken.None)); // avisos desligados
        Assert.Empty(api.Envios);

        await servico.DefinirAvisosAsync(usuario, true, CancellationToken.None);
        Assert.True(await servico.EnviarAvisoAsync(usuario, "t", "c", null, CancellationToken.None));
    }

    [Fact]
    public async Task Vincular_NaPrimeiraVez_EnviaBoasVindasPorMensagemDireta_ComLinkParaOChat()
    {
        using var factory = new TestDbContextFactory();
        var (servico, api, _) = Montar(factory);
        var usuario = Guid.NewGuid();

        await servico.ConcluirVinculoAsync("c", EstadoDaUrl(await servico.IniciarVinculoAsync(usuario, Retorno, CancellationToken.None)), Retorno, CancellationToken.None);

        var (conta, aviso) = Assert.Single(api.Envios);
        Assert.Equal("111", conta);
        Assert.Contains("vinculada", aviso.Titulo);
        Assert.Equal("https://crm.exemplo.com/app/chat", aviso.Url);
    }

    [Fact]
    public async Task Vincular_DeNovo_NaoRepeteABoasVindas()
    {
        using var factory = new TestDbContextFactory();
        var (servico, api, _) = Montar(factory);
        var usuario = Guid.NewGuid();
        await servico.ConcluirVinculoAsync("c", EstadoDaUrl(await servico.IniciarVinculoAsync(usuario, Retorno, CancellationToken.None)), Retorno, CancellationToken.None);

        await servico.ConcluirVinculoAsync("c", EstadoDaUrl(await servico.IniciarVinculoAsync(usuario, Retorno, CancellationToken.None)), Retorno, CancellationToken.None);

        Assert.Single(api.Envios);
    }

    [Fact]
    public async Task Vincular_ComMensagemDiretaFechada_ContinuaVinculadoSemErro()
    {
        using var factory = new TestDbContextFactory();
        var (servico, api, db) = Montar(factory);
        api.Resultado = ResultadoEnvioDiscord.DmFechada;
        var usuario = Guid.NewGuid();

        var status = await servico.ConcluirVinculoAsync("c", EstadoDaUrl(await servico.IniciarVinculoAsync(usuario, Retorno, CancellationToken.None)), Retorno, CancellationToken.None);

        Assert.True(status.Vinculado);
        Assert.Single(await db.CrmDiscordVinculos.ToListAsync());
    }

    [Fact]
    public async Task EnviarAviso_ComMensagemDiretaFechada_NaoContaComoEntregue()
    {
        using var factory = new TestDbContextFactory();
        var (servico, api, _) = Montar(factory);
        var usuario = Guid.NewGuid();
        await servico.ConcluirVinculoAsync("c", EstadoDaUrl(await servico.IniciarVinculoAsync(usuario, Retorno, CancellationToken.None)), Retorno, CancellationToken.None);
        api.Resultado = ResultadoEnvioDiscord.DmFechada;

        Assert.False(await servico.EnviarAvisoAsync(usuario, "t", "c", null, CancellationToken.None));
    }

    [Fact]
    public async Task DefinirAvisos_SemVinculo_DaErro()
    {
        using var factory = new TestDbContextFactory();
        var (servico, _, _) = Montar(factory);

        var erro = await Assert.ThrowsAsync<CrmBusinessException>(() => servico.DefinirAvisosAsync(Guid.NewGuid(), true, CancellationToken.None));
        Assert.Equal("discord_sem_vinculo", erro.Codigo);
    }
}
