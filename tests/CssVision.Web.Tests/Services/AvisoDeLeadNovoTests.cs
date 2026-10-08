using CssVision.Web.Authorization;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Aviso de lead novo: chega a quem tem push E a quem vinculou o Discord (mesmo sem nunca ter ativado o push).</summary>
public class AvisoDeLeadNovoTests
{
    private sealed class PushGravador : IPushService
    {
        public List<(Guid UsuarioId, PushMensagem Mensagem)> Enviados { get; } = [];

        public Task<string> ChavePublicaAsync(CancellationToken ct) => Task.FromResult("chave");

        public Task InscreverAsync(Guid usuarioId, PushInscricaoRequest request, CancellationToken ct) => Task.CompletedTask;

        public Task RemoverAsync(Guid usuarioId, string endpoint, CancellationToken ct) => Task.CompletedTask;

        public Task<int> EnviarAsync(Guid usuarioId, PushMensagem mensagem, CancellationToken ct)
        {
            Enviados.Add((usuarioId, mensagem));
            return Task.FromResult(1);
        }
    }

    private sealed class Cenario(TestDbContextFactory factory) : IDisposable
    {
        public TestDbContextFactory Factory { get; } = factory;
        public PushGravador Push { get; } = new();
        public ApplicationDbContext Db { get; } = factory.CreateContext();
        private readonly ServiceProvider _provedor = new ServiceCollection()
            .AddScoped(_ => factory.CreateContext())
            .BuildServiceProvider();

        public PushNovosLeadsBackgroundService Servico()
        {
            var servicos = new ServiceCollection().AddScoped(_ => Factory.CreateContext()).AddSingleton<IPushService>(Push).BuildServiceProvider();
            return new PushNovosLeadsBackgroundService(servicos.GetRequiredService<IServiceScopeFactory>(), NullLogger<PushNovosLeadsBackgroundService>.Instance);
        }

        public void Dispose() => _provedor.Dispose();
    }

    private static async Task<Guid> ConsultoraAsync(Cenario c, string nome, bool comPush = false, bool? discord = null)
    {
        var u = await c.Factory.CriarUsuarioAsync(c.Db, nome);
        await c.Factory.AtribuirPapelAsync(c.Db, u, Roles.Comercial);
        if (comPush) c.Db.CrmPushInscricoes.Add(new CrmPushInscricao { UsuarioId = u.Id, Endpoint = $"https://push/{u.Id}", P256dh = "p", Auth = "a" });
        if (discord is { } avisosAtivos) c.Db.CrmDiscordVinculos.Add(new CrmDiscordVinculo { UsuarioId = u.Id, DiscordUserId = $"d-{nome}", DiscordNome = nome, AvisosAtivos = avisosAtivos, VinculadoEm = DateTimeOffset.UtcNow });
        await c.Db.SaveChangesAsync();
        return u.Id;
    }

    private static async Task NovoLeadAsync(Cenario c, Guid responsavelId, string nome, string? oQue = "AGV", string? origem = "Meta ads")
    {
        c.Db.CrmLeads.Add(new CrmLead { NomeOuRazaoSocial = nome, TipoPessoa = TipoPessoa.Fisica, ResponsavelId = responsavelId, ProdutoInteresse = oQue, Origem = origem });
        await c.Db.SaveChangesAsync();
    }

    private static DateTimeOffset UmMinutoAtras => DateTimeOffset.UtcNow.AddMinutes(-1);

    [Fact]
    public async Task QuemVinculouODiscord_RecebeOAvisoMesmoSemPush()
    {
        using var factory = new TestDbContextFactory();
        using var c = new Cenario(factory);
        var ana = await ConsultoraAsync(c, "Ana", comPush: false, discord: true);
        await NovoLeadAsync(c, ana, "Maria Souza");

        await c.Servico().VerificarAsync(UmMinutoAtras, CancellationToken.None);

        var envio = Assert.Single(c.Push.Enviados);
        Assert.Equal(ana, envio.UsuarioId);
        Assert.Equal("Novo lead para você", envio.Mensagem.Titulo);
        Assert.Equal("Maria Souza · AGV · Meta ads", envio.Mensagem.Corpo);
    }

    [Fact]
    public async Task QuemDesligouOsAvisosDoDiscord_ESemPush_NaoRecebe()
    {
        using var factory = new TestDbContextFactory();
        using var c = new Cenario(factory);
        var ana = await ConsultoraAsync(c, "Ana", comPush: false, discord: false);
        await NovoLeadAsync(c, ana, "Maria Souza");

        await c.Servico().VerificarAsync(UmMinutoAtras, CancellationToken.None);

        Assert.Empty(c.Push.Enviados);
    }

    [Fact]
    public async Task QuemTemSoPush_ContinuaRecebendo()
    {
        using var factory = new TestDbContextFactory();
        using var c = new Cenario(factory);
        var bia = await ConsultoraAsync(c, "Bia", comPush: true);
        await NovoLeadAsync(c, bia, "João Lima", oQue: null, origem: null);

        await c.Servico().VerificarAsync(UmMinutoAtras, CancellationToken.None);

        var envio = Assert.Single(c.Push.Enviados);
        Assert.Equal("João Lima", envio.Mensagem.Corpo); // sem "O que?" nem origem: só o nome, sem separadores sobrando
    }

    [Fact]
    public async Task QuemTemPushEDiscord_RecebeUmaVezSo_QuemNaoTemNenhumNaoRecebe()
    {
        using var factory = new TestDbContextFactory();
        using var c = new Cenario(factory);
        var ana = await ConsultoraAsync(c, "Ana", comPush: true, discord: true);
        var sem = await ConsultoraAsync(c, "Sem");
        await NovoLeadAsync(c, ana, "Maria Souza");
        await NovoLeadAsync(c, sem, "Pedro Alves");

        await c.Servico().VerificarAsync(UmMinutoAtras, CancellationToken.None);

        Assert.Equal(ana, Assert.Single(c.Push.Enviados).UsuarioId);
    }

    [Fact]
    public async Task VariosLeadsDeUmaVez_ViramUmAvisoSo()
    {
        using var factory = new TestDbContextFactory();
        using var c = new Cenario(factory);
        var ana = await ConsultoraAsync(c, "Ana", discord: true);
        await NovoLeadAsync(c, ana, "Maria Souza");
        await NovoLeadAsync(c, ana, "João Lima");

        await c.Servico().VerificarAsync(UmMinutoAtras, CancellationToken.None);

        var envio = Assert.Single(c.Push.Enviados);
        Assert.Equal("2 novos leads para você", envio.Mensagem.Titulo);
        Assert.Contains("Maria Souza", envio.Mensagem.Corpo);
        Assert.Contains("João Lima", envio.Mensagem.Corpo);
    }

    [Fact]
    public async Task LeadAntigoOuQueAPropriaPessoaCadastrou_NaoAvisa()
    {
        using var factory = new TestDbContextFactory();
        using var c = new Cenario(factory);
        var ana = await ConsultoraAsync(c, "Ana", discord: true);
        await NovoLeadAsync(c, ana, "Maria Souza");

        // O cursor está no futuro: o lead já era "velho" para esta verificação.
        await c.Servico().VerificarAsync(DateTimeOffset.UtcNow.AddMinutes(5), CancellationToken.None);

        Assert.Empty(c.Push.Enviados);
    }
}
