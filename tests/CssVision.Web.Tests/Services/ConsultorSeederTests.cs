using CssVision.Web.Data;
using CssVision.Web.Data.Seed;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Storage;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>O seed dos consultores reais não recria contas que foram unificadas em outra (alias).</summary>
public class ConsultorSeederTests
{
    [Fact]
    public async Task EmailComAlias_NaoERecriado_OsDemaisSaoCriados()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        await factory.SeedRolesAsync(db);
        using var userManager = TestDbContextFactory.CreateUserManager(db);
        var queFicou = await factory.CriarUsuarioAsync(db, "Carol Barcelos");
        db.CrmUsuarioAliases.Add(new CrmUsuarioAlias { EmailNormalizado = "CAROLBARCELOSAGV@GMAIL.COM", UsuarioId = queFicou.Id });
        await db.SaveChangesAsync();

        var services = new ServiceCollection()
            .AddSingleton(userManager)
            .AddSingleton<IFileStorageService>(new FakeFileStorageService())
            .AddSingleton(db)
            .BuildServiceProvider();

        await ConsultorSeeder.SeedAsync(services);

        Assert.Null(await userManager.FindByEmailAsync("carolbarcelosagv@gmail.com")); // unificada: não volta
        Assert.NotNull(await userManager.FindByEmailAsync("robertasobrinho28@gmail.com")); // consultora comum: criada
    }
}
