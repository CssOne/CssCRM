using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Authorization;
using CssVision.Web.Data;
using CssVision.Web.Domain.Identity;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Ativar/inativar conta pela tela de Usuários: cada perfil só mexe no que também pode editar.</summary>
public class UsuarioAtivoTests
{
    private sealed record Cenario(
        ApplicationDbContext Db, ApplicationUser Admin, ApplicationUser Supervisor, ApplicationUser Gestor, ApplicationUser Consultor, ApplicationUser OutroGestor,
        ApplicationUser ConsultorDeOutraRegional, UserManagementService ComoAdmin, UserManagementService ComoSupervisor, UserManagementService ComoGestor);

    private static async Task<Cenario> MontarAsync(TestDbContextFactory factory)
    {
        var db = factory.CreateContext();
        await factory.SeedRolesAsync(db);
        var mg132 = await factory.CriarRegionalAsync(db, "MG132");
        var mg134 = await factory.CriarRegionalAsync(db, "MG134");
        async Task<ApplicationUser> Novo(string nome, string papel, Guid? regional)
        {
            var u = await factory.CriarUsuarioAsync(db, nome);
            await factory.AtribuirPapelAsync(db, u, papel);
            u.RegionalId = regional;
            return u;
        }
        var admin = await Novo("Admin", Roles.Admin, null);
        var supervisor = await Novo("Supervisor", Roles.SupervisorComercial, mg132.Id);
        var gestor = await Novo("Gestor", Roles.GestorComercial, mg132.Id);
        var consultor = await Novo("Consultor", Roles.Comercial, mg132.Id);
        var outroGestor = await Novo("OutroGestor", Roles.GestorComercial, mg132.Id);
        var consultorFora = await Novo("ConsultorFora", Roles.Comercial, mg134.Id);
        await db.SaveChangesAsync();

        UserManagementService Servico(Mock<ICurrentUserService> quem) => new(db, TestDbContextFactory.CreateUserManager(db), quem.Object, new NoOpAuditSink());

        var comoAdmin = TestDbContextFactory.MockCurrentUser(admin.Id, visaoTotal: true);
        comoAdmin.Setup(m => m.IsInRole(Roles.Admin)).Returns(true);
        var comoSupervisor = TestDbContextFactory.MockCurrentUser(supervisor.Id, visaoTotal: true);
        comoSupervisor.Setup(m => m.IsInRole(Roles.SupervisorComercial)).Returns(true);
        var comoGestor = TestDbContextFactory.MockCurrentUser(gestor.Id, gestorComercial: true, podeGerir: true);
        comoGestor.Setup(m => m.IsInRole(Roles.GestorComercial)).Returns(true);

        return new Cenario(db, admin, supervisor, gestor, consultor, outroGestor, consultorFora, Servico(comoAdmin), Servico(comoSupervisor), Servico(comoGestor));
    }

    private static async Task<bool> AtivoAsync(Cenario c, Guid id)
    {
        c.Db.ChangeTracker.Clear();
        return (await c.Db.Users.AsNoTracking().SingleAsync(u => u.Id == id)).Ativo;
    }

    [Fact]
    public async Task Admin_InativaEAtivaQualquerConta_MenosAPropria()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);

        foreach (var alvo in new[] { c.Gestor.Id, c.Supervisor.Id, c.Consultor.Id, c.ConsultorDeOutraRegional.Id })
        {
            Assert.False((await c.ComoAdmin.AlterarAtivoAsync(alvo, false, CancellationToken.None)).Ativo);
            Assert.False(await AtivoAsync(c, alvo));
            c.Db.ChangeTracker.Clear();
            Assert.True((await c.ComoAdmin.AlterarAtivoAsync(alvo, true, CancellationToken.None)).Ativo);
        }

        await Assert.ThrowsAsync<CrmForbiddenException>(() => c.ComoAdmin.AlterarAtivoAsync(c.Admin.Id, false, CancellationToken.None));
    }

    [Fact]
    public async Task GestorRegional_SoInativaConsultoresDaPropriaRegional()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);

        Assert.False((await c.ComoGestor.AlterarAtivoAsync(c.Consultor.Id, false, CancellationToken.None)).Ativo);

        c.Db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<CrmForbiddenException>(() => c.ComoGestor.AlterarAtivoAsync(c.OutroGestor.Id, false, CancellationToken.None));       // outro gestor
        await Assert.ThrowsAsync<CrmForbiddenException>(() => c.ComoGestor.AlterarAtivoAsync(c.Admin.Id, false, CancellationToken.None));            // administrador
        await Assert.ThrowsAsync<CrmForbiddenException>(() => c.ComoGestor.AlterarAtivoAsync(c.ConsultorDeOutraRegional.Id, false, CancellationToken.None)); // outra regional
        await Assert.ThrowsAsync<CrmForbiddenException>(() => c.ComoGestor.AlterarAtivoAsync(c.Gestor.Id, false, CancellationToken.None));           // a própria conta
        Assert.True(await AtivoAsync(c, c.Admin.Id));
    }

    [Fact]
    public async Task Supervisor_InativaConsultoresEGestoresDaPropriaRegional_NuncaAdministradorNemOutraRegional()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);

        Assert.False((await c.ComoSupervisor.AlterarAtivoAsync(c.Consultor.Id, false, CancellationToken.None)).Ativo);
        c.Db.ChangeTracker.Clear();
        Assert.False((await c.ComoSupervisor.AlterarAtivoAsync(c.Gestor.Id, false, CancellationToken.None)).Ativo);

        c.Db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<CrmForbiddenException>(() => c.ComoSupervisor.AlterarAtivoAsync(c.Admin.Id, false, CancellationToken.None));
        await Assert.ThrowsAsync<CrmForbiddenException>(() => c.ComoSupervisor.AlterarAtivoAsync(c.ConsultorDeOutraRegional.Id, false, CancellationToken.None));
        await Assert.ThrowsAsync<CrmForbiddenException>(() => c.ComoSupervisor.AlterarAtivoAsync(c.Supervisor.Id, false, CancellationToken.None));
        Assert.True(await AtivoAsync(c, c.Admin.Id));
    }
}
