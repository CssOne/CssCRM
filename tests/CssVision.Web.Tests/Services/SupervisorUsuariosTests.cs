using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Authorization;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Moq;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Supervisor comercial: funções de administrador, mas cadastra e edita usuários só da própria regional.</summary>
public class SupervisorUsuariosTests
{
    private static UserCreateRequest Novo(string papel, Guid? regionalId, string email) =>
        new("Novo Usuário", email, "Senha@123", "31999990000", papel, regionalId, null, null, null);

    private static Mock<ICurrentUserService> Supervisor(Guid id)
    {
        var mock = TestDbContextFactory.MockCurrentUser(id, visaoTotal: true);
        mock.Setup(m => m.IsInRole(Roles.SupervisorComercial)).Returns(true);
        return mock;
    }

    [Fact]
    public async Task Supervisor_CriaConsultorEGestorRegionalSoNaPropriaRegional()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        await factory.SeedRolesAsync(db);
        using var userManager = TestDbContextFactory.CreateUserManager(db);
        var mg134 = await factory.CriarRegionalAsync(db, "MG134");
        var outra = await factory.CriarRegionalAsync(db, "MG132");
        var supervisor = await factory.CriarUsuarioAsync(db, "Supervisor");
        supervisor.RegionalId = mg134.Id;
        await db.SaveChangesAsync();
        var service = new UserManagementService(db, userManager, Supervisor(supervisor.Id).Object, new NoOpAuditSink());

        // Mesmo mandando outra regional, o usuário entra na do supervisor.
        var consultor = await service.CriarAsync(Novo(Roles.Comercial, outra.Id, "consultor@teste.com"), CancellationToken.None);
        Assert.Equal(mg134.Id, consultor.RegionalId);
        Assert.Null(consultor.GestorComercialId); // o supervisor não é gestor de equipe

        var gestor = await service.CriarAsync(Novo(Roles.GestorComercial, outra.Id, "gestor@teste.com"), CancellationToken.None);
        Assert.Equal(mg134.Id, gestor.RegionalId);
        Assert.Contains(Roles.GestorComercial, gestor.Papeis);

        // Admin, supervisor e outros papéis ficam com o administrador.
        await Assert.ThrowsAsync<CrmForbiddenException>(() => service.CriarAsync(Novo(Roles.Admin, mg134.Id, "admin@teste.com"), CancellationToken.None));
        await Assert.ThrowsAsync<CrmForbiddenException>(() => service.CriarAsync(Novo(Roles.SupervisorComercial, mg134.Id, "sup@teste.com"), CancellationToken.None));
    }

    [Fact]
    public async Task Supervisor_SoListaEEditaUsuariosDaPropriaRegional_SemRegionalNaoCadastra()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        await factory.SeedRolesAsync(db);
        using var userManager = TestDbContextFactory.CreateUserManager(db);
        var mg134 = await factory.CriarRegionalAsync(db, "MG134");
        var outra = await factory.CriarRegionalAsync(db, "MG132");
        var supervisor = await factory.CriarUsuarioAsync(db, "Supervisor");
        supervisor.RegionalId = mg134.Id;
        var daRegional = await factory.CriarUsuarioAsync(db, "Da Regional");
        daRegional.RegionalId = mg134.Id;
        var deFora = await factory.CriarUsuarioAsync(db, "De Fora");
        deFora.RegionalId = outra.Id;
        await db.SaveChangesAsync();
        await userManager.AddToRoleAsync(daRegional, Roles.Comercial);
        await userManager.AddToRoleAsync(deFora, Roles.Comercial);
        var service = new UserManagementService(db, userManager, Supervisor(supervisor.Id).Object, new NoOpAuditSink());

        var lista = await service.ListarAsync(new UserFilterRequest { Pagina = 1, TamanhoPagina = 50 }, CancellationToken.None);
        Assert.Contains(lista.Itens, u => u.Id == daRegional.Id);
        Assert.DoesNotContain(lista.Itens, u => u.Id == deFora.Id);

        await Assert.ThrowsAsync<CrmForbiddenException>(() => service.AtualizarAsync(
            deFora.Id, new UserUpdateRequest("De Fora", null, Roles.Comercial, outra.Id, null, null, null, true), CancellationToken.None));
        await Assert.ThrowsAsync<CrmForbiddenException>(() => service.ExcluirAsync(deFora.Id, CancellationToken.None));

        // Supervisor sem regional não consegue cadastrar.
        var semRegional = await factory.CriarUsuarioAsync(db, "Supervisor sem regional");
        var servicoSemRegional = new UserManagementService(db, userManager, Supervisor(semRegional.Id).Object, new NoOpAuditSink());
        await Assert.ThrowsAsync<CrmBusinessException>(() => servicoSemRegional.CriarAsync(Novo(Roles.Comercial, mg134.Id, "x@teste.com"), CancellationToken.None));
    }
}
