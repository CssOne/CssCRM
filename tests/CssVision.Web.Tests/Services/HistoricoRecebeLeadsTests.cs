using CssVision.Web.Authorization;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Quem tirou (ou devolveu) cada consultor da distribuição de leads fica registrado e aparece no histórico da Gestão comercial.</summary>
public class HistoricoRecebeLeadsTests
{
    private static async Task<(CssVision.Web.Data.ApplicationDbContext Db, ManagementService Gestao, Guid GestorId, Guid ConsultorId)> PrepararAsync(TestDbContextFactory factory)
    {
        var db = factory.CreateContext();
        var gestor = await factory.CriarUsuarioAsync(db, "Gestora Maria");
        var consultor = await factory.CriarUsuarioAsync(db, "Consultor João");
        await factory.AtribuirPapelAsync(db, consultor, Roles.Comercial);
        var usuario = TestDbContextFactory.MockCurrentUser(gestor.Id, visaoTotal: true, podeGerir: true).Object;
        var gestao = new ManagementService(db, usuario, new EquipeComercialService(db, usuario), TestDbContextFactory.CreateUserManager(db), audit: new CrmAuditLogSink(db, usuario));
        return (db, gestao, gestor.Id, consultor.Id);
    }

    [Fact]
    public async Task DesativarEReativar_FicaNoHistorico_ComQuemFez()
    {
        using var factory = new TestDbContextFactory();
        var (_, gestao, _, consultorId) = await PrepararAsync(factory);

        await gestao.AtualizarRecebeLeadsAsync(consultorId, new AtualizarRecebeLeadsRequest(false), CancellationToken.None);
        await gestao.AtualizarRecebeLeadsAsync(consultorId, new AtualizarRecebeLeadsRequest(true), CancellationToken.None);

        var historico = await gestao.ObterHistoricoRecebeLeadsAsync(CancellationToken.None);

        Assert.Equal(2, historico.Count);
        Assert.Equal(["ativou", "desativou"], historico.Select(h => h.Evento).ToArray()); // mais recente primeiro
        Assert.All(historico, h =>
        {
            Assert.Equal("Consultor João", h.ConsultorNome);
            Assert.Equal("Gestora Maria", h.AlteradoPorNome);
        });
    }

    [Fact]
    public async Task SemMudancaNoValor_NaoRegistraNada()
    {
        using var factory = new TestDbContextFactory();
        var (_, gestao, _, consultorId) = await PrepararAsync(factory);

        // O consultor já recebe leads (padrão): "ligar" de novo não muda nada.
        await gestao.AtualizarRecebeLeadsAsync(consultorId, new AtualizarRecebeLeadsRequest(true), CancellationToken.None);

        Assert.Empty(await gestao.ObterHistoricoRecebeLeadsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task HistoricoTrazTambemAContaInativadaQueOSistemaJaAuditava()
    {
        using var factory = new TestDbContextFactory();
        var (db, gestao, gestorId, consultorId) = await PrepararAsync(factory);
        // Registro anterior a esta mudança: o sistema já auditava inativar/reativar a conta do usuário.
        db.CrmAuditLogs.AddRange(
            new CrmAuditLog { UsuarioId = gestorId, Acao = "UsuarioInativado", EntidadeTipo = "ApplicationUser", EntidadeId = consultorId, OcorridoEm = DateTimeOffset.UtcNow.AddDays(-10) },
            new CrmAuditLog { UsuarioId = gestorId, Acao = "UsuarioAtivado", EntidadeTipo = "ApplicationUser", EntidadeId = consultorId, OcorridoEm = DateTimeOffset.UtcNow.AddDays(-5) },
            // Outra ação da auditoria do mesmo usuário não entra.
            new CrmAuditLog { UsuarioId = gestorId, Acao = "SenhaRedefinida", EntidadeTipo = "ApplicationUser", EntidadeId = consultorId, OcorridoEm = DateTimeOffset.UtcNow.AddDays(-1) });
        await db.SaveChangesAsync();

        var historico = await gestao.ObterHistoricoRecebeLeadsAsync(CancellationToken.None);

        Assert.Equal(["conta_reativada", "conta_inativada"], historico.Select(h => h.Evento).ToArray());
        Assert.All(historico, h => Assert.Equal("Gestora Maria", h.AlteradoPorNome));
    }

    [Fact]
    public async Task RegistroSemOsDetalhesEsperados_EIgnorado()
    {
        using var factory = new TestDbContextFactory();
        var (db, gestao, gestorId, consultorId) = await PrepararAsync(factory);
        db.CrmAuditLogs.Add(new CrmAuditLog { UsuarioId = gestorId, Acao = "RecebeLeadsAlterado", EntidadeTipo = "ApplicationUser", EntidadeId = consultorId, DetalhesJson = "não é json", OcorridoEm = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        Assert.Empty(await gestao.ObterHistoricoRecebeLeadsAsync(CancellationToken.None));
    }
}
