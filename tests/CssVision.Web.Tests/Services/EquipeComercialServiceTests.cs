using CssVision.Web.Authorization;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Xunit;

namespace CssVision.Web.Tests.Services;

public class EquipeComercialServiceTests
{
    [Fact]
    public async Task ObterVendedoresVisiveisAsync_GestorComercial_DeveIncluirEquipeDaMesmaRegional()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();

        var regional = await factory.CriarRegionalAsync(db, "Grande BH");
        var outraRegional = await factory.CriarRegionalAsync(db, "Interior");

        var gestor = await factory.CriarUsuarioAsync(db, "Gestora");
        gestor.RegionalId = regional.Id;

        var consultorMesmaRegional = await factory.CriarUsuarioAsync(db, "ConsultorMesmaRegional");
        consultorMesmaRegional.RegionalId = regional.Id;

        var consultorReporteDireto = await factory.CriarUsuarioAsync(db, "ConsultorReporte", gestorId: gestor.Id);
        consultorReporteDireto.RegionalId = outraRegional.Id; // regional diferente, mas reporta direto à gestora

        var consultorOutraRegional = await factory.CriarUsuarioAsync(db, "ConsultorFora");
        consultorOutraRegional.RegionalId = outraRegional.Id;

        await db.SaveChangesAsync();

        var currentUser = TestDbContextFactory.MockCurrentUser(gestor.Id, gestorComercial: true);
        var service = new EquipeComercialService(db, currentUser.Object);

        var visiveis = await service.ObterVendedoresVisiveisAsync(CancellationToken.None);

        Assert.NotNull(visiveis);
        Assert.Contains(gestor.Id, visiveis);
        Assert.Contains(consultorMesmaRegional.Id, visiveis); // mesma regional
        Assert.Contains(consultorReporteDireto.Id, visiveis); // reporte direto, regional diferente
        Assert.DoesNotContain(consultorOutraRegional.Id, visiveis); // nem regional nem reporte
    }

    [Fact]
    public async Task ObterVendedoresVisiveisAsync_Admin_DeveRetornarNull()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var admin = await factory.CriarUsuarioAsync(db, "Admin");

        var currentUser = TestDbContextFactory.MockCurrentUser(admin.Id, visaoTotal: true);
        var service = new EquipeComercialService(db, currentUser.Object);

        Assert.Null(await service.ObterVendedoresVisiveisAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ObterVendedoresVisiveisAsync_Comercial_DeveVerApenasASiMesmo()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var regional = await factory.CriarRegionalAsync(db, "Grande BH");
        var consultor = await factory.CriarUsuarioAsync(db, "Consultora");
        consultor.RegionalId = regional.Id;
        var colegaMesmaRegional = await factory.CriarUsuarioAsync(db, "Colega");
        colegaMesmaRegional.RegionalId = regional.Id;
        await db.SaveChangesAsync();

        var currentUser = TestDbContextFactory.MockCurrentUser(consultor.Id);
        var service = new EquipeComercialService(db, currentUser.Object);

        var visiveis = await service.ObterVendedoresVisiveisAsync(CancellationToken.None);

        Assert.Equal([consultor.Id], visiveis);
    }
}
