using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Authorization;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Xunit;

namespace CssVision.Web.Tests.Services;

public class GoalServiceTests
{
    private static DateOnly MesAtual() => new(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);

    [Fact]
    public async Task ListarAsync_Admin_DeveListarTodoConsultorMesmoSemMetaCadastrada()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        await factory.SeedRolesAsync(db);
        using var userManager = TestDbContextFactory.CreateUserManager(db);

        var comSenhaId = await factory.CriarUsuarioAsync(db, "ComMeta");
        await userManager.AddToRoleAsync(comSenhaId, Roles.Comercial);
        var semMeta = await factory.CriarUsuarioAsync(db, "SemMeta");
        await userManager.AddToRoleAsync(semMeta, Roles.Comercial);

        db.CrmSalesGoals.Add(new CrmSalesGoal { VendedorId = comSenhaId.Id, MesReferencia = MesAtual(), MetaQuantidadeVendas = 10, MetaValor = 5000m });
        await db.SaveChangesAsync();

        var admin = await factory.CriarUsuarioAsync(db, "Admin");
        var currentUser = TestDbContextFactory.MockCurrentUser(admin.Id, visaoTotal: true);
        var service = new GoalService(db, currentUser.Object, new EquipeComercialService(db, currentUser.Object), userManager);

        var resultado = await service.ListarAsync(null, CancellationToken.None);

        Assert.Equal(2, resultado.Count);

        var comMetaDto = Assert.Single(resultado, d => d.VendedorId == comSenhaId.Id);
        Assert.NotNull(comMetaDto.Id);
        Assert.Equal(10, comMetaDto.MetaQuantidadeVendas);
        Assert.Equal(5000m, comMetaDto.MetaValor);

        var semMetaDto = Assert.Single(resultado, d => d.VendedorId == semMeta.Id);
        Assert.Null(semMetaDto.Id);
        Assert.Null(semMetaDto.MetaQuantidadeVendas);
        Assert.Null(semMetaDto.MetaValor);
    }

    [Fact]
    public async Task MetaDeValor_AcompanhaAAdesaoRecebida_MesmoSemValorFinalNaVenda()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        await factory.SeedRolesAsync(db);
        using var userManager = TestDbContextFactory.CreateUserManager(db);
        var consultor = await factory.CriarUsuarioAsync(db, "Consultor");
        await userManager.AddToRoleAsync(consultor, Roles.Comercial);
        var admin = await factory.CriarUsuarioAsync(db, "Admin");
        var ganho = await factory.CriarEtapaAsync(db, "Ganho", 9, TipoEtapaPipeline.Ganho);

        var lead = new CrmLead { NomeOuRazaoSocial = "Cliente", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = consultor.Id };
        db.CrmLeads.Add(lead);
        await db.SaveChangesAsync();
        // Venda lançada pela "Venda concluída": tem adesão, mas ValorFinal/ValorEstimado ficam zerados.
        var fechamento = MesAtual().ToDateTime(new TimeOnly(15, 0), DateTimeKind.Utc);
        db.CrmOpportunities.AddRange(
            new CrmOpportunity { LeadId = lead.Id, Titulo = "V1", ResponsavelId = consultor.Id, EtapaId = ganho.Id, PagamentoAdesao = 300m, DataEfetivaFechamento = fechamento },
            new CrmOpportunity { LeadId = lead.Id, Titulo = "V2", ResponsavelId = consultor.Id, EtapaId = ganho.Id, PagamentoAdesao = 250m, DataEfetivaFechamento = fechamento });
        await db.SaveChangesAsync();

        var currentUser = TestDbContextFactory.MockCurrentUser(admin.Id, visaoTotal: true, podeGerir: true);
        var service = new GoalService(db, currentUser.Object, new EquipeComercialService(db, currentUser.Object), userManager);

        var definida = await service.DefinirMetaAsync(new SalesGoalUpsertRequest(consultor.Id, MesAtual(), 30, 6000m), CancellationToken.None);
        Assert.Equal(550m, definida.RealizadoValor);
        Assert.Equal(2, definida.RealizadoQuantidade);

        var lista = Assert.Single(await service.ListarAsync(null, CancellationToken.None));
        Assert.Equal(550m, lista.RealizadoValor);
    }

    [Fact]
    public async Task ListarAsync_GestorComercial_SoListaConsultoresDaPropriaEquipe()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        await factory.SeedRolesAsync(db);
        using var userManager = TestDbContextFactory.CreateUserManager(db);

        var gestor = await factory.CriarUsuarioAsync(db, "Gestor");
        await userManager.AddToRoleAsync(gestor, Roles.GestorComercial);

        var daEquipe = await factory.CriarUsuarioAsync(db, "DaEquipe", gestor.Id);
        await userManager.AddToRoleAsync(daEquipe, Roles.Comercial);
        var deFora = await factory.CriarUsuarioAsync(db, "DeFora");
        await userManager.AddToRoleAsync(deFora, Roles.Comercial);

        var currentUser = TestDbContextFactory.MockCurrentUser(gestor.Id, gestorComercial: true, podeGerir: true);
        var service = new GoalService(db, currentUser.Object, new EquipeComercialService(db, currentUser.Object), userManager);

        var resultado = await service.ListarAsync(null, CancellationToken.None);

        var dto = Assert.Single(resultado);
        Assert.Equal(daEquipe.Id, dto.VendedorId);
        Assert.Null(dto.Id);
    }

    [Fact]
    public async Task DefinirMetaAsync_DeveAceitarQuantidadeSemValor()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        await factory.SeedRolesAsync(db);
        using var userManager = TestDbContextFactory.CreateUserManager(db);

        var admin = await factory.CriarUsuarioAsync(db, "Admin");
        var consultor = await factory.CriarUsuarioAsync(db, "Consultor");
        await userManager.AddToRoleAsync(consultor, Roles.Comercial);

        var currentUser = TestDbContextFactory.MockCurrentUser(admin.Id, visaoTotal: true);
        var service = new GoalService(db, currentUser.Object, new EquipeComercialService(db, currentUser.Object), userManager);

        var resultado = await service.DefinirMetaAsync(
            new SalesGoalUpsertRequest(consultor.Id, MesAtual(), MetaQuantidadeVendas: 8, MetaValor: null), CancellationToken.None);

        Assert.Equal(8, resultado.MetaQuantidadeVendas);
        Assert.Null(resultado.MetaValor);
    }

    [Fact]
    public async Task DefinirMetaAsync_DeveRejeitarQuantidadeNegativa()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        await factory.SeedRolesAsync(db);
        using var userManager = TestDbContextFactory.CreateUserManager(db);

        var admin = await factory.CriarUsuarioAsync(db, "Admin");
        var consultor = await factory.CriarUsuarioAsync(db, "Consultor");
        await userManager.AddToRoleAsync(consultor, Roles.Comercial);

        var currentUser = TestDbContextFactory.MockCurrentUser(admin.Id, visaoTotal: true);
        var service = new GoalService(db, currentUser.Object, new EquipeComercialService(db, currentUser.Object), userManager);

        await Assert.ThrowsAsync<CrmBusinessException>(() =>
            service.DefinirMetaAsync(new SalesGoalUpsertRequest(consultor.Id, MesAtual(), MetaQuantidadeVendas: -1, MetaValor: null), CancellationToken.None));
    }

    [Fact]
    public async Task DefinirMetaRegionalAsync_Admin_DeveDefinirMetaDaRegional()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        await factory.SeedRolesAsync(db);
        using var userManager = TestDbContextFactory.CreateUserManager(db);

        var admin = await factory.CriarUsuarioAsync(db, "Admin");
        var regional = await factory.CriarRegionalAsync(db, "Grande BH");

        var currentUser = TestDbContextFactory.MockCurrentUser(admin.Id, visaoTotal: true);
        var service = new GoalService(db, currentUser.Object, new EquipeComercialService(db, currentUser.Object), userManager);

        var resultado = await service.DefinirMetaRegionalAsync(
            new RegionalGoalUpsertRequest(regional.Id, MesAtual(), MetaQuantidadeVendas: 50, MetaValor: null), CancellationToken.None);

        Assert.Equal(50, resultado.MetaQuantidadeVendas);
        Assert.Equal(regional.Id, resultado.RegionalId);
    }

    [Fact]
    public async Task DefinirMetaRegionalAsync_GestorComercial_DeveSerNegado()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        await factory.SeedRolesAsync(db);
        using var userManager = TestDbContextFactory.CreateUserManager(db);

        var gestor = await factory.CriarUsuarioAsync(db, "Gestor");
        var regional = await factory.CriarRegionalAsync(db, "Grande BH");

        var currentUser = TestDbContextFactory.MockCurrentUser(gestor.Id, gestorComercial: true, podeGerir: true);
        var service = new GoalService(db, currentUser.Object, new EquipeComercialService(db, currentUser.Object), userManager);

        await Assert.ThrowsAsync<CrmForbiddenException>(() =>
            service.DefinirMetaRegionalAsync(new RegionalGoalUpsertRequest(regional.Id, MesAtual(), MetaQuantidadeVendas: 50, MetaValor: null), CancellationToken.None));
    }

    [Fact]
    public async Task ListarRegionaisAsync_NaoAdministrador_DeveSerNegado()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        await factory.SeedRolesAsync(db);
        using var userManager = TestDbContextFactory.CreateUserManager(db);

        var gestor = await factory.CriarUsuarioAsync(db, "Gestor");
        var currentUser = TestDbContextFactory.MockCurrentUser(gestor.Id, gestorComercial: true, podeGerir: true);
        var service = new GoalService(db, currentUser.Object, new EquipeComercialService(db, currentUser.Object), userManager);

        await Assert.ThrowsAsync<CrmForbiddenException>(() => service.ListarRegionaisAsync(null, CancellationToken.None));
    }

    [Fact]
    public async Task ListarRegionaisAsync_Admin_DeveListarRegionaisAtivasComRealizado()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        await factory.SeedRolesAsync(db);
        using var userManager = TestDbContextFactory.CreateUserManager(db);

        var admin = await factory.CriarUsuarioAsync(db, "Admin");
        var regionalComMeta = await factory.CriarRegionalAsync(db, "Grande BH");
        var regionalSemMeta = await factory.CriarRegionalAsync(db, "Interior");

        db.CrmRegionalGoals.Add(new CrmRegionalGoal { RegionalId = regionalComMeta.Id, MesReferencia = MesAtual(), MetaQuantidadeVendas = 100, MetaValor = 20000m });
        await db.SaveChangesAsync();

        var currentUser = TestDbContextFactory.MockCurrentUser(admin.Id, visaoTotal: true);
        var service = new GoalService(db, currentUser.Object, new EquipeComercialService(db, currentUser.Object), userManager);

        var resultado = await service.ListarRegionaisAsync(null, CancellationToken.None);

        Assert.Equal(2, resultado.Count);

        var comMetaDto = Assert.Single(resultado, d => d.RegionalId == regionalComMeta.Id);
        Assert.NotNull(comMetaDto.Id);
        Assert.Equal(100, comMetaDto.MetaQuantidadeVendas);

        var semMetaDto = Assert.Single(resultado, d => d.RegionalId == regionalSemMeta.Id);
        Assert.Null(semMetaDto.Id);
        Assert.Null(semMetaDto.MetaQuantidadeVendas);
    }
}
