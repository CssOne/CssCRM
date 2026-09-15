using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Authorization;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Xunit;

namespace CssVision.Web.Tests.Services;

public class UserManagementServiceTests
{
    private static UserCreateRequest NovoUsuario(string papel, Guid? regionalId, string email = "novo@teste.com", Guid? grupoId = null) =>
        new("Novo Usuário", email, "Senha@123", "31999990000", papel, regionalId, null, grupoId, null);

    [Fact]
    public async Task CriarAsync_Admin_DeveCriarGestorComercial_ComRegional()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        await factory.SeedRolesAsync(db);
        using var userManager = TestDbContextFactory.CreateUserManager(db);

        var regional = await factory.CriarRegionalAsync(db, "Grande BH");
        var admin = await factory.CriarUsuarioAsync(db, "Admin");

        var currentUser = TestDbContextFactory.MockCurrentUser(admin.Id, visaoTotal: true);
        var service = new UserManagementService(db, userManager, currentUser.Object, new NoOpAuditSink());

        var resultado = await service.CriarAsync(NovoUsuario(Roles.GestorComercial, regional.Id), CancellationToken.None);

        Assert.Contains(Roles.GestorComercial, resultado.Papeis);
        Assert.Equal(regional.Id, resultado.RegionalId);
    }

    [Fact]
    public async Task CriarAsync_GestorComercial_DeveForcarPropriaRegional_MesmoSeOutraForEnviada()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        await factory.SeedRolesAsync(db);
        using var userManager = TestDbContextFactory.CreateUserManager(db);

        var regionalDoGestor = await factory.CriarRegionalAsync(db, "Grande BH");
        var outraRegional = await factory.CriarRegionalAsync(db, "Interior");

        var gestor = await factory.CriarUsuarioAsync(db, "Gestora");
        gestor.RegionalId = regionalDoGestor.Id;
        await db.SaveChangesAsync();
        await userManager.AddToRoleAsync(gestor, Roles.GestorComercial);

        var currentUser = TestDbContextFactory.MockCurrentUser(gestor.Id, gestorComercial: true);
        var service = new UserManagementService(db, userManager, currentUser.Object, new NoOpAuditSink());

        // Tenta cadastrar um consultor informando a regional errada (do gestor não deveria valer).
        var resultado = await service.CriarAsync(NovoUsuario(Roles.Comercial, outraRegional.Id), CancellationToken.None);

        Assert.Equal(regionalDoGestor.Id, resultado.RegionalId);
        Assert.Equal(gestor.Id, resultado.GestorComercialId);
    }

    [Fact]
    public async Task CriarAsync_GestorComercial_NaoPodeCriarAdmin()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        await factory.SeedRolesAsync(db);
        using var userManager = TestDbContextFactory.CreateUserManager(db);

        var regional = await factory.CriarRegionalAsync(db, "Grande BH");
        var gestor = await factory.CriarUsuarioAsync(db, "Gestora");
        gestor.RegionalId = regional.Id;
        await db.SaveChangesAsync();

        var currentUser = TestDbContextFactory.MockCurrentUser(gestor.Id, gestorComercial: true);
        var service = new UserManagementService(db, userManager, currentUser.Object, new NoOpAuditSink());

        await Assert.ThrowsAsync<CrmForbiddenException>(() => service.CriarAsync(NovoUsuario(Roles.Admin, null), CancellationToken.None));
    }

    [Fact]
    public async Task CriarAsync_DeveRejeitarEmailDuplicado()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        await factory.SeedRolesAsync(db);
        using var userManager = TestDbContextFactory.CreateUserManager(db);

        var admin = await factory.CriarUsuarioAsync(db, "Admin");
        var currentUser = TestDbContextFactory.MockCurrentUser(admin.Id, visaoTotal: true);
        var service = new UserManagementService(db, userManager, currentUser.Object, new NoOpAuditSink());

        await service.CriarAsync(NovoUsuario(Roles.Admin, null, "duplicado@teste.com"), CancellationToken.None);

        await Assert.ThrowsAsync<CrmBusinessException>(() =>
            service.CriarAsync(NovoUsuario(Roles.Admin, null, "duplicado@teste.com"), CancellationToken.None));
    }

    [Fact]
    public async Task ObterPorIdAsync_GestorComercial_NaoPodeAcessarUsuarioDeOutraRegional()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        await factory.SeedRolesAsync(db);
        using var userManager = TestDbContextFactory.CreateUserManager(db);

        var regionalDoGestor = await factory.CriarRegionalAsync(db, "Grande BH");
        var outraRegional = await factory.CriarRegionalAsync(db, "Interior");

        var gestor = await factory.CriarUsuarioAsync(db, "Gestora");
        gestor.RegionalId = regionalDoGestor.Id;

        var consultorForaDaRegional = await factory.CriarUsuarioAsync(db, "ConsultorFora");
        consultorForaDaRegional.RegionalId = outraRegional.Id;
        await db.SaveChangesAsync();

        var currentUser = TestDbContextFactory.MockCurrentUser(gestor.Id, gestorComercial: true);
        var service = new UserManagementService(db, userManager, currentUser.Object, new NoOpAuditSink());

        await Assert.ThrowsAsync<CrmForbiddenException>(() => service.ObterPorIdAsync(consultorForaDaRegional.Id, CancellationToken.None));
    }

    [Fact]
    public async Task AtualizarAsync_Admin_NaoPodeDesativarAPropriaConta()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        await factory.SeedRolesAsync(db);
        using var userManager = TestDbContextFactory.CreateUserManager(db);

        var admin = await factory.CriarUsuarioAsync(db, "Admin");
        await userManager.AddToRoleAsync(admin, Roles.Admin);

        var currentUser = TestDbContextFactory.MockCurrentUser(admin.Id, visaoTotal: true);
        var service = new UserManagementService(db, userManager, currentUser.Object, new NoOpAuditSink());

        var request = new UserUpdateRequest(admin.NomeCompleto, null, Roles.Admin, null, null, null, null, Ativo: false);

        await Assert.ThrowsAsync<CrmBusinessException>(() => service.AtualizarAsync(admin.Id, request, CancellationToken.None));
    }

    [Fact]
    public async Task CriarAsync_DeveAceitarGrupoDaMesmaRegional()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        await factory.SeedRolesAsync(db);
        using var userManager = TestDbContextFactory.CreateUserManager(db);

        var regional = await factory.CriarRegionalAsync(db, "Grande BH");
        var grupo = await factory.CriarGrupoAsync(db, regional.Id, "Externos");
        var admin = await factory.CriarUsuarioAsync(db, "Admin");

        var currentUser = TestDbContextFactory.MockCurrentUser(admin.Id, visaoTotal: true);
        var service = new UserManagementService(db, userManager, currentUser.Object, new NoOpAuditSink());

        var resultado = await service.CriarAsync(NovoUsuario(Roles.Comercial, regional.Id, grupoId: grupo.Id), CancellationToken.None);

        Assert.Equal(grupo.Id, resultado.GrupoId);
        Assert.Equal("Externos", resultado.GrupoNome);
    }

    [Fact]
    public async Task ExcluirAsync_Admin_DeveExcluirUsuarioSemVinculos()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        await factory.SeedRolesAsync(db);
        using var userManager = TestDbContextFactory.CreateUserManager(db);

        var admin = await factory.CriarUsuarioAsync(db, "Admin");
        var consultor = await factory.CriarUsuarioAsync(db, "Consultor");

        var currentUser = TestDbContextFactory.MockCurrentUser(admin.Id, visaoTotal: true);
        var service = new UserManagementService(db, userManager, currentUser.Object, new NoOpAuditSink());

        await service.ExcluirAsync(consultor.Id, CancellationToken.None);

        Assert.Null(await userManager.FindByIdAsync(consultor.Id.ToString()));
    }

    [Fact]
    public async Task ExcluirAsync_Admin_NaoPodeExcluirAPropriaConta()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        await factory.SeedRolesAsync(db);
        using var userManager = TestDbContextFactory.CreateUserManager(db);

        var admin = await factory.CriarUsuarioAsync(db, "Admin");

        var currentUser = TestDbContextFactory.MockCurrentUser(admin.Id, visaoTotal: true);
        var service = new UserManagementService(db, userManager, currentUser.Object, new NoOpAuditSink());

        await Assert.ThrowsAsync<CrmBusinessException>(() => service.ExcluirAsync(admin.Id, CancellationToken.None));
    }

    [Fact]
    public async Task ExcluirAsync_GestorComercial_NaoPodeExcluirUsuario()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        await factory.SeedRolesAsync(db);
        using var userManager = TestDbContextFactory.CreateUserManager(db);

        var regional = await factory.CriarRegionalAsync(db, "Grande BH");
        var gestor = await factory.CriarUsuarioAsync(db, "Gestora");
        gestor.RegionalId = regional.Id;
        var consultor = await factory.CriarUsuarioAsync(db, "Consultor", gestorId: gestor.Id);
        consultor.RegionalId = regional.Id;
        await db.SaveChangesAsync();

        var currentUser = TestDbContextFactory.MockCurrentUser(gestor.Id, gestorComercial: true);
        var service = new UserManagementService(db, userManager, currentUser.Object, new NoOpAuditSink());

        await Assert.ThrowsAsync<CrmForbiddenException>(() => service.ExcluirAsync(consultor.Id, CancellationToken.None));
    }

    [Fact]
    public async Task ExcluirAsync_Admin_NaoPodeExcluirUsuarioComLeadsVinculados()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        await factory.SeedRolesAsync(db);
        using var userManager = TestDbContextFactory.CreateUserManager(db);

        var admin = await factory.CriarUsuarioAsync(db, "Admin");
        var consultor = await factory.CriarUsuarioAsync(db, "Consultor");

        db.CrmLeads.Add(new CssVision.Web.Domain.Crm.CrmLead
        {
            NomeOuRazaoSocial = "Cliente Teste",
            ResponsavelId = consultor.Id,
        });
        await db.SaveChangesAsync();

        var currentUser = TestDbContextFactory.MockCurrentUser(admin.Id, visaoTotal: true);
        var service = new UserManagementService(db, userManager, currentUser.Object, new NoOpAuditSink());

        await Assert.ThrowsAsync<CrmBusinessException>(() => service.ExcluirAsync(consultor.Id, CancellationToken.None));
        Assert.NotNull(await userManager.FindByIdAsync(consultor.Id.ToString()));
    }

    [Fact]
    public async Task CriarAsync_DeveRejeitarGrupoDeOutraRegional()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        await factory.SeedRolesAsync(db);
        using var userManager = TestDbContextFactory.CreateUserManager(db);

        var regional = await factory.CriarRegionalAsync(db, "Grande BH");
        var outraRegional = await factory.CriarRegionalAsync(db, "Interior");
        var grupoDeOutraRegional = await factory.CriarGrupoAsync(db, outraRegional.Id, "Externos");
        var admin = await factory.CriarUsuarioAsync(db, "Admin");

        var currentUser = TestDbContextFactory.MockCurrentUser(admin.Id, visaoTotal: true);
        var service = new UserManagementService(db, userManager, currentUser.Object, new NoOpAuditSink());

        await Assert.ThrowsAsync<CrmBusinessException>(() =>
            service.CriarAsync(NovoUsuario(Roles.Comercial, regional.Id, grupoId: grupoDeOutraRegional.Id), CancellationToken.None));
    }
}
