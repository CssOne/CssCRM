using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Authorization;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Aviso aos gestores quando o limite diário/mensal trava a distribuição, e a decisão de continuar mesmo assim.</summary>
public class AlertaLimiteDistribuicaoTests
{
    private sealed class PushFalso : IPushService
    {
        public List<(Guid UsuarioId, PushMensagem Mensagem)> Enviados { get; } = [];
        public Task<string> ChavePublicaAsync(CancellationToken ct) => Task.FromResult("");
        public Task InscreverAsync(Guid usuarioId, PushInscricaoRequest request, CancellationToken ct) => Task.CompletedTask;
        public Task RemoverAsync(Guid usuarioId, string endpoint, CancellationToken ct) => Task.CompletedTask;
        public Task<int> EnviarAsync(Guid usuarioId, PushMensagem mensagem, CancellationToken ct)
        {
            Enviados.Add((usuarioId, mensagem));
            return Task.FromResult(1);
        }
    }

    private sealed class RelogioAjustavel(DateTimeOffset agora) : TimeProvider
    {
        public DateTimeOffset Agora { get; set; } = agora;
        public override DateTimeOffset GetUtcNow() => Agora;
    }

    private sealed record Ctx(ApplicationDbContext Db, Guid GestorId, Guid AdminId, Guid OutroId);

    private static CrmLead Trafego(string nome, Guid? responsavelId) => new()
    {
        NomeOuRazaoSocial = nome, TipoPessoa = TipoPessoa.Fisica, ResponsavelId = responsavelId,
        MetaLeadId = Guid.NewGuid().ToString(), CriadoManualmente = false,
    };

    /// <summary>Ana e Bruna com limite diário 1 e um lead hoje cada, mais um lead do tráfego sem responsável.</summary>
    private static async Task<(TestDbContextFactory Factory, Ctx Ctx)> CenarioAsync()
    {
        var factory = new TestDbContextFactory();
        var db = factory.CreateContext();
        var ana = await factory.CriarUsuarioAsync(db, "Ana Vendedora");
        var bruna = await factory.CriarUsuarioAsync(db, "Bruna Vendedora");
        var gestor = await factory.CriarUsuarioAsync(db, "Gestora");
        var admin = await factory.CriarUsuarioAsync(db, "Admin");
        var consultorInativoGestor = await factory.CriarUsuarioAsync(db, "Outro");
        await factory.AtribuirPapelAsync(db, ana, Roles.Comercial);
        await factory.AtribuirPapelAsync(db, bruna, Roles.Comercial);
        await factory.AtribuirPapelAsync(db, gestor, Roles.GestorComercial);
        await factory.AtribuirPapelAsync(db, admin, Roles.Admin);
        ana.LimiteDiarioLeads = 1;
        bruna.LimiteDiarioLeads = 1;
        db.CrmLeads.AddRange(Trafego("Da Ana", ana.Id), Trafego("Da Bruna", bruna.Id), Trafego("Parado", null));
        await db.SaveChangesAsync();
        return (factory, new Ctx(db, gestor.Id, admin.Id, consultorInativoGestor.Id));
    }

    [Fact]
    public async Task TodosNoLimite_ComLeadParado_AvisaGestoresUmaVezEReavisaDepoisDoIntervalo()
    {
        var (factory, c) = await CenarioAsync();
        using var _ = factory;
        await using var db = c.Db;
        var relogio = new RelogioAjustavel(DateTimeOffset.UtcNow);
        var distribuicao = new LeadAssignmentService(db);
        var push = new PushFalso();
        var alerta = new AlertaDistribuicaoService(db, distribuicao, push, NullLogger<AlertaDistribuicaoService>.Instance, relogio: relogio);

        var estado = await distribuicao.ObterEstadoDistribuicaoAsync(CancellationToken.None);
        Assert.True(estado.Bloqueada);
        Assert.Equal(1, estado.LeadsSemResponsavel);
        Assert.Equal(1, estado.LeadsBloqueadosPorLimite);
        Assert.Equal(2, estado.NoLimiteDiario);

        Assert.True(await alerta.VerificarAsync(CancellationToken.None));
        // Admin e gestor comercial recebem; os consultores não.
        Assert.Equal(new[] { c.GestorId, c.AdminId }.Order(), push.Enviados.Select(e => e.UsuarioId).Order());

        Assert.False(await alerta.VerificarAsync(CancellationToken.None)); // não repete na hora
        relogio.Agora = relogio.Agora.Add(AlertaDistribuicaoService.IntervaloReaviso).AddMinutes(1);
        Assert.True(await alerta.VerificarAsync(CancellationToken.None)); // continua parado: reavisa
    }

    [Fact]
    public async Task ContinuarMesmoComLimite_DistribuiOsParadosENaoAvisa()
    {
        var (factory, c) = await CenarioAsync();
        using var _ = factory;
        await using var db = c.Db;
        var distribuicao = new LeadAssignmentService(db);
        var push = new PushFalso();
        var alerta = new AlertaDistribuicaoService(db, distribuicao, push, NullLogger<AlertaDistribuicaoService>.Instance);

        await distribuicao.DefinirContinuarAposLimiteAsync(true, CancellationToken.None);

        var estado = await distribuicao.ObterEstadoDistribuicaoAsync(CancellationToken.None);
        Assert.False(estado.Bloqueada);
        Assert.NotNull(estado.ContinuarAteEm);
        Assert.Equal(0, estado.LeadsSemResponsavel); // o parado foi entregue na hora
        Assert.False(await alerta.VerificarAsync(CancellationToken.None));
        Assert.Empty(push.Enviados);

        await distribuicao.DefinirContinuarAposLimiteAsync(false, CancellationToken.None);
        Assert.Null((await distribuicao.ObterEstadoDistribuicaoAsync(CancellationToken.None)).ContinuarAteEm);
    }

    [Fact]
    public async Task SemLeadParado_NaoAvisa_EZeraOControleDeReaviso()
    {
        var (factory, c) = await CenarioAsync();
        using var _ = factory;
        await using var db = c.Db;
        var distribuicao = new LeadAssignmentService(db);
        var push = new PushFalso();
        var alerta = new AlertaDistribuicaoService(db, distribuicao, push, NullLogger<AlertaDistribuicaoService>.Instance);

        Assert.True(await alerta.VerificarAsync(CancellationToken.None));
        // O gestor resolve (o lead parado sai da fila): sem pendência, o próximo problema avisa de novo.
        foreach (var lead in db.CrmLeads.Where(l => l.ResponsavelId == null)) lead.Arquivado = true;
        await db.SaveChangesAsync();
        Assert.False(await alerta.VerificarAsync(CancellationToken.None));

        db.CrmLeads.Add(Trafego("Novo parado", null));
        await db.SaveChangesAsync();
        Assert.True(await alerta.VerificarAsync(CancellationToken.None));
        Assert.Equal(4, push.Enviados.Count);
    }

    [Fact]
    public async Task Gestao_EditaOsTiposDeLeadDoConsultor()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var admin = await factory.CriarUsuarioAsync(db, "Admin");
        var ana = await factory.CriarUsuarioAsync(db, "Ana");
        await factory.AtribuirPapelAsync(db, ana, Roles.Comercial);
        var usuario = TestDbContextFactory.MockCurrentUser(admin.Id, visaoTotal: true, podeGerir: true).Object;
        var gestao = new ManagementService(db, usuario, new EquipeComercialService(db, usuario), TestDbContextFactory.CreateUserManager(db));

        await gestao.AtualizarTiposLeadAsync(ana.Id, new AtualizarTiposLeadRequest(["AGV", "AGV TRUCK"]), CancellationToken.None);
        var vendedor = Assert.Single(await gestao.ObterVendedoresAsync(CancellationToken.None), v => v.Id == ana.Id);
        Assert.Equal(["AGV", "AGV TRUCK"], vendedor.RecebeSomenteOQue!);

        // Só AGV TRUCK: um lead AGV não vai para ela.
        Assert.True(FiltroOQue.Aceita(ana.RecebeSomenteOQue, "agv truck"));
        Assert.False(FiltroOQue.Aceita(ana.RecebeSomenteOQue, "AGV ELÉTRICO"));

        await gestao.AtualizarTiposLeadAsync(ana.Id, new AtualizarTiposLeadRequest([]), CancellationToken.None);
        Assert.Null(Assert.Single(await gestao.ObterVendedoresAsync(CancellationToken.None), v => v.Id == ana.Id).RecebeSomenteOQue);
    }
}
