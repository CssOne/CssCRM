using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Xunit;

namespace CssVision.Web.Tests.Services;

public class LookupServiceGrupoTests
{
    [Fact]
    public async Task CriarGrupoAsync_Admin_DeveCriarNaRegionalInformada()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var regional = await factory.CriarRegionalAsync(db, "Grande BH");
        var admin = await factory.CriarUsuarioAsync(db, "Admin");

        var currentUser = TestDbContextFactory.MockCurrentUser(admin.Id, visaoTotal: true);
        var service = new LookupService(db, currentUser.Object);

        var resultado = await service.CriarGrupoAsync(new CreateGrupoRequest(regional.Id, "Externos"), CancellationToken.None);

        Assert.Equal(regional.Id, resultado.RegionalId);
        Assert.Equal("Externos", resultado.Nome);
        Assert.True(resultado.Ativo);
    }

    [Fact]
    public async Task CriarGrupoAsync_GestorComercial_DeveInferirPropriaRegional()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var regional = await factory.CriarRegionalAsync(db, "Grande BH");
        var gestor = await factory.CriarUsuarioAsync(db, "Gestora");
        gestor.RegionalId = regional.Id;
        await db.SaveChangesAsync();

        var currentUser = TestDbContextFactory.MockCurrentUser(gestor.Id, gestorComercial: true, podeGerir: true);
        var service = new LookupService(db, currentUser.Object);

        var resultado = await service.CriarGrupoAsync(new CreateGrupoRequest(null, "Internos"), CancellationToken.None);

        Assert.Equal(regional.Id, resultado.RegionalId);
    }

    [Fact]
    public async Task CriarGrupoAsync_GestorComercial_NaoPodeCriarEmOutraRegional()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var regionalDoGestor = await factory.CriarRegionalAsync(db, "Grande BH");
        var outraRegional = await factory.CriarRegionalAsync(db, "Interior");
        var gestor = await factory.CriarUsuarioAsync(db, "Gestora");
        gestor.RegionalId = regionalDoGestor.Id;
        await db.SaveChangesAsync();

        var currentUser = TestDbContextFactory.MockCurrentUser(gestor.Id, gestorComercial: true, podeGerir: true);
        var service = new LookupService(db, currentUser.Object);

        await Assert.ThrowsAsync<CrmForbiddenException>(() =>
            service.CriarGrupoAsync(new CreateGrupoRequest(outraRegional.Id, "Externos"), CancellationToken.None));
    }

    [Fact]
    public async Task CriarGrupoAsync_Comercial_NaoPodeCriarGrupo()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var regional = await factory.CriarRegionalAsync(db, "Grande BH");
        var consultor = await factory.CriarUsuarioAsync(db, "Consultor");

        var currentUser = TestDbContextFactory.MockCurrentUser(consultor.Id);
        var service = new LookupService(db, currentUser.Object);

        await Assert.ThrowsAsync<CrmForbiddenException>(() =>
            service.CriarGrupoAsync(new CreateGrupoRequest(regional.Id, "Externos"), CancellationToken.None));
    }

    [Fact]
    public async Task CriarGrupoAsync_DeveRejeitarNomeDuplicadoNaMesmaRegional()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var regional = await factory.CriarRegionalAsync(db, "Grande BH");
        await factory.CriarGrupoAsync(db, regional.Id, "Externos");
        var admin = await factory.CriarUsuarioAsync(db, "Admin");

        var currentUser = TestDbContextFactory.MockCurrentUser(admin.Id, visaoTotal: true);
        var service = new LookupService(db, currentUser.Object);

        await Assert.ThrowsAsync<CrmBusinessException>(() =>
            service.CriarGrupoAsync(new CreateGrupoRequest(regional.Id, "Externos"), CancellationToken.None));
    }

    [Fact]
    public async Task ObterGruposAsync_GestorComercial_SoVeGruposDaPropriaRegional()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var regionalDoGestor = await factory.CriarRegionalAsync(db, "Grande BH");
        var outraRegional = await factory.CriarRegionalAsync(db, "Interior");
        await factory.CriarGrupoAsync(db, regionalDoGestor.Id, "Externos");
        await factory.CriarGrupoAsync(db, outraRegional.Id, "Internos");

        var gestor = await factory.CriarUsuarioAsync(db, "Gestora");
        gestor.RegionalId = regionalDoGestor.Id;
        await db.SaveChangesAsync();

        var currentUser = TestDbContextFactory.MockCurrentUser(gestor.Id, gestorComercial: true, podeGerir: true);
        var service = new LookupService(db, currentUser.Object);

        var resultado = await service.ObterGruposAsync(null, CancellationToken.None);

        var grupo = Assert.Single(resultado);
        Assert.Equal("Externos", grupo.Nome);
    }

    [Fact]
    public async Task AtualizarGrupoAsync_DeveRenomearEArquivar()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var regional = await factory.CriarRegionalAsync(db, "Grande BH");
        var grupo = await factory.CriarGrupoAsync(db, regional.Id, "Externos");
        var admin = await factory.CriarUsuarioAsync(db, "Admin");

        var currentUser = TestDbContextFactory.MockCurrentUser(admin.Id, visaoTotal: true);
        var service = new LookupService(db, currentUser.Object);

        var resultado = await service.AtualizarGrupoAsync(grupo.Id, new UpdateGrupoRequest("Externos MG", false), CancellationToken.None);

        Assert.Equal("Externos MG", resultado.Nome);
        Assert.False(resultado.Ativo);
    }

    [Fact]
    public async Task AtualizarGrupoAsync_GestorComercial_NaoPodeEditarGrupoDeOutraRegional()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var regionalDoGestor = await factory.CriarRegionalAsync(db, "Grande BH");
        var outraRegional = await factory.CriarRegionalAsync(db, "Interior");
        var grupoDeOutraRegional = await factory.CriarGrupoAsync(db, outraRegional.Id, "Externos");

        var gestor = await factory.CriarUsuarioAsync(db, "Gestora");
        gestor.RegionalId = regionalDoGestor.Id;
        await db.SaveChangesAsync();

        var currentUser = TestDbContextFactory.MockCurrentUser(gestor.Id, gestorComercial: true, podeGerir: true);
        var service = new LookupService(db, currentUser.Object);

        await Assert.ThrowsAsync<CrmForbiddenException>(() =>
            service.AtualizarGrupoAsync(grupoDeOutraRegional.Id, new UpdateGrupoRequest("Renomeado", true), CancellationToken.None));
    }

    [Fact]
    public async Task AtualizarMembrosGrupoAsync_DeveAdicionarERemoverConsultores()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var regional = await factory.CriarRegionalAsync(db, "Grande BH");
        var grupo = await factory.CriarGrupoAsync(db, regional.Id, "Externos");

        var consultor1 = await factory.CriarUsuarioAsync(db, "Consultor1");
        consultor1.RegionalId = regional.Id;
        consultor1.GrupoId = grupo.Id;
        var consultor2 = await factory.CriarUsuarioAsync(db, "Consultor2");
        consultor2.RegionalId = regional.Id;
        await db.SaveChangesAsync();

        var admin = await factory.CriarUsuarioAsync(db, "Admin");
        var currentUser = TestDbContextFactory.MockCurrentUser(admin.Id, visaoTotal: true);
        var service = new LookupService(db, currentUser.Object);

        // Remove consultor1 e adiciona consultor2.
        var resultado = await service.AtualizarMembrosGrupoAsync(grupo.Id, new UpdateGrupoMembrosRequest([consultor2.Id]), CancellationToken.None);

        Assert.Single(resultado.Consultores);
        Assert.Equal(consultor2.Id, resultado.Consultores[0].Id);

        await db.Entry(consultor1).ReloadAsync();
        Assert.Null(consultor1.GrupoId);
    }

    [Fact]
    public async Task AtualizarMembrosGrupoAsync_DeveRejeitarConsultorDeOutraRegional()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var regional = await factory.CriarRegionalAsync(db, "Grande BH");
        var outraRegional = await factory.CriarRegionalAsync(db, "Interior");
        var grupo = await factory.CriarGrupoAsync(db, regional.Id, "Externos");

        var consultorForaDaRegional = await factory.CriarUsuarioAsync(db, "ConsultorFora");
        consultorForaDaRegional.RegionalId = outraRegional.Id;
        await db.SaveChangesAsync();

        var admin = await factory.CriarUsuarioAsync(db, "Admin");
        var currentUser = TestDbContextFactory.MockCurrentUser(admin.Id, visaoTotal: true);
        var service = new LookupService(db, currentUser.Object);

        await Assert.ThrowsAsync<CrmBusinessException>(() =>
            service.AtualizarMembrosGrupoAsync(grupo.Id, new UpdateGrupoMembrosRequest([consultorForaDaRegional.Id]), CancellationToken.None));
    }
}
