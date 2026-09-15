using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Authorization;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Xunit;

namespace CssVision.Web.Tests.Services;

public class ManagementServiceConsultoresTests
{
    [Fact]
    public async Task ObterDesempenhoConsultoresAsync_Admin_DeveVerTodosOsConsultores()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        await factory.SeedRolesAsync(db);
        using var userManager = TestDbContextFactory.CreateUserManager(db);

        var regional = await factory.CriarRegionalAsync(db, "Grande BH");
        var consultor1 = await factory.CriarUsuarioAsync(db, "Consultor1");
        consultor1.RegionalId = regional.Id;
        var consultor2 = await factory.CriarUsuarioAsync(db, "Consultor2");
        var gestor = await factory.CriarUsuarioAsync(db, "Gestor1");
        await db.SaveChangesAsync();
        await userManager.AddToRoleAsync(consultor1, Roles.Comercial);
        await userManager.AddToRoleAsync(consultor2, Roles.Comercial);
        await userManager.AddToRoleAsync(gestor, Roles.GestorComercial);

        var admin = await factory.CriarUsuarioAsync(db, "Admin");
        var currentUser = TestDbContextFactory.MockCurrentUser(admin.Id, visaoTotal: true);
        var service = new ManagementService(db, currentUser.Object, new EquipeComercialService(db, currentUser.Object), userManager);

        var resultado = await service.ObterDesempenhoConsultoresAsync(null, CancellationToken.None);

        Assert.Equal(2, resultado.Count);
        Assert.Contains(resultado, c => c.Id == consultor1.Id && c.RegionalNome == "Grande BH");
        Assert.Contains(resultado, c => c.Id == consultor2.Id);
        Assert.DoesNotContain(resultado, c => c.Id == gestor.Id);
    }

    [Fact]
    public async Task ObterDesempenhoConsultoresAsync_GestorComercial_SoVeConsultoresDaPropriaRegional()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        await factory.SeedRolesAsync(db);
        using var userManager = TestDbContextFactory.CreateUserManager(db);

        var regionalDoGestor = await factory.CriarRegionalAsync(db, "Grande BH");
        var outraRegional = await factory.CriarRegionalAsync(db, "Interior");

        var gestor = await factory.CriarUsuarioAsync(db, "Gestora");
        gestor.RegionalId = regionalDoGestor.Id;

        var consultorMesmaRegional = await factory.CriarUsuarioAsync(db, "ConsultorDentro");
        consultorMesmaRegional.RegionalId = regionalDoGestor.Id;

        var consultorOutraRegional = await factory.CriarUsuarioAsync(db, "ConsultorFora");
        consultorOutraRegional.RegionalId = outraRegional.Id;
        await db.SaveChangesAsync();

        await userManager.AddToRoleAsync(gestor, Roles.GestorComercial);
        await userManager.AddToRoleAsync(consultorMesmaRegional, Roles.Comercial);
        await userManager.AddToRoleAsync(consultorOutraRegional, Roles.Comercial);

        var currentUser = TestDbContextFactory.MockCurrentUser(gestor.Id, gestorComercial: true, podeGerir: true);
        var service = new ManagementService(db, currentUser.Object, new EquipeComercialService(db, currentUser.Object), userManager);

        var resultado = await service.ObterDesempenhoConsultoresAsync(null, CancellationToken.None);

        Assert.Single(resultado);
        Assert.Equal(consultorMesmaRegional.Id, resultado[0].Id);
    }

    [Fact]
    public async Task ObterDesempenhoConsultoresAsync_Comercial_NaoPodeAcessar()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        await factory.SeedRolesAsync(db);
        using var userManager = TestDbContextFactory.CreateUserManager(db);

        var consultor = await factory.CriarUsuarioAsync(db, "Consultor1");
        await db.SaveChangesAsync();
        await userManager.AddToRoleAsync(consultor, Roles.Comercial);

        var currentUser = TestDbContextFactory.MockCurrentUser(consultor.Id);
        var service = new ManagementService(db, currentUser.Object, new EquipeComercialService(db, currentUser.Object), userManager);

        await Assert.ThrowsAsync<CrmForbiddenException>(() => service.ObterDesempenhoConsultoresAsync(null, CancellationToken.None));
    }

    [Fact]
    public async Task ObterDesempenhoConsultoresAsync_DeveCalcularMetaEVendasDoMes()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        await factory.SeedRolesAsync(db);
        using var userManager = TestDbContextFactory.CreateUserManager(db);

        var consultor = await factory.CriarUsuarioAsync(db, "Consultor1");
        await db.SaveChangesAsync();
        await userManager.AddToRoleAsync(consultor, Roles.Comercial);

        var etapaGanho = await factory.CriarEtapaAsync(db, "Ganho", 1, TipoEtapaPipeline.Ganho);
        var lead = new CrmLead { NomeOuRazaoSocial = "Cliente", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = consultor.Id };
        db.CrmLeads.Add(lead);
        await db.SaveChangesAsync();

        var agora = DateTimeOffset.UtcNow;
        db.CrmOpportunities.Add(new CrmOpportunity
        {
            LeadId = lead.Id, Titulo = "Venda", ResponsavelId = consultor.Id, EtapaId = etapaGanho.Id,
            ValorEstimado = 1000m, ValorFinal = 1200m, DataEfetivaFechamento = agora
        });
        var mesReferencia = new DateOnly(agora.Year, agora.Month, 1);
        db.CrmSalesGoals.Add(new CrmSalesGoal { VendedorId = consultor.Id, MesReferencia = mesReferencia, MetaValor = 2400m });
        await db.SaveChangesAsync();

        var admin = await factory.CriarUsuarioAsync(db, "Admin");
        var currentUser = TestDbContextFactory.MockCurrentUser(admin.Id, visaoTotal: true);
        var service = new ManagementService(db, currentUser.Object, new EquipeComercialService(db, currentUser.Object), userManager);

        var resultado = await service.ObterDesempenhoConsultoresAsync(mesReferencia, CancellationToken.None);

        var ficha = Assert.Single(resultado);
        Assert.Equal(1, ficha.VendasGanhas);
        Assert.Equal(1200m, ficha.ValorGanho);
        Assert.Equal(2400m, ficha.MetaValor);
        Assert.Equal(50m, ficha.PercentualMeta);
    }
}
