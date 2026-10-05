using CssVision.Web.Authorization;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Xunit;

namespace CssVision.Web.Tests.Services;

public class EquipeComercialServiceTests
{
    [Fact]
    public async Task ObterVendedoresVisiveisAsync_GestorRegional_VeSomenteASuaRegional()
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
        consultorReporteDireto.RegionalId = outraRegional.Id; // regional diferente: o gestor regional não a enxerga, mesmo reportando a ele

        var consultorOutraRegional = await factory.CriarUsuarioAsync(db, "ConsultorFora");
        consultorOutraRegional.RegionalId = outraRegional.Id;

        await db.SaveChangesAsync();

        var currentUser = TestDbContextFactory.MockCurrentUser(gestor.Id, gestorComercial: true);
        var service = new EquipeComercialService(db, currentUser.Object);

        var visiveis = await service.ObterVendedoresVisiveisAsync(CancellationToken.None);

        Assert.NotNull(visiveis);
        Assert.Contains(gestor.Id, visiveis);
        Assert.Contains(consultorMesmaRegional.Id, visiveis); // mesma regional
        Assert.DoesNotContain(consultorReporteDireto.Id, visiveis); // só a regional vale
        Assert.DoesNotContain(consultorOutraRegional.Id, visiveis);
    }

    [Fact]
    public async Task ObterVendedoresVisiveisAsync_GestorRegionalDeRegionalNova_VeSoQuemEDelaEGestorSemRegionalVeOsReportes()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var mg134 = await factory.CriarRegionalAsync(db, "MG134");
        var nova = await factory.CriarRegionalAsync(db, "Regional Nova");

        var gestorMg134 = await factory.CriarUsuarioAsync(db, "Gestor MG134");
        gestorMg134.RegionalId = mg134.Id;
        var consultorMg134 = await factory.CriarUsuarioAsync(db, "Consultor MG134");
        consultorMg134.RegionalId = mg134.Id;
        var consultorNova = await factory.CriarUsuarioAsync(db, "Consultor Nova");
        consultorNova.RegionalId = nova.Id;

        var gestorSemRegional = await factory.CriarUsuarioAsync(db, "Gestor antigo");
        var reporte = await factory.CriarUsuarioAsync(db, "Reporte", gestorId: gestorSemRegional.Id);
        await db.SaveChangesAsync();

        var daMg134 = await new EquipeComercialService(db, TestDbContextFactory.MockCurrentUser(gestorMg134.Id, gestorComercial: true).Object)
            .ObterVendedoresVisiveisAsync(CancellationToken.None);
        Assert.Equal(new[] { gestorMg134.Id, consultorMg134.Id }.Order(), daMg134!.Order());

        var antigo = await new EquipeComercialService(db, TestDbContextFactory.MockCurrentUser(gestorSemRegional.Id, gestorComercial: true).Object)
            .ObterVendedoresVisiveisAsync(CancellationToken.None);
        Assert.Contains(reporte.Id, antigo!);
        Assert.DoesNotContain(consultorMg134.Id, antigo!);
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
