using CssVision.Web.Authorization;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Administrador que atua nas vendas e está com "Recebe leads" ligado entra no rodízio; o que só administra (ou está com a opção desligada) não.</summary>
public class AdminQueAtuaNasVendasRecebeLeadsTests
{
    [Fact]
    public async Task AdminQueAtuaNasVendas_EntraNoRodizio_AdminQueSoAdministraOuDesligado_Nao()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        await factory.SeedRolesAsync(db);
        var jessica = await factory.CriarUsuarioAsync(db, "Jessica");
        await factory.AtribuirPapelAsync(db, jessica, Roles.Admin);
        var servico = new LeadAssignmentService(db);

        Assert.Equal(jessica.Id, await servico.ProximoResponsavelAsync(null, CancellationToken.None));

        jessica.AtuaNasVendas = false; // só administra a plataforma
        await db.SaveChangesAsync();
        Assert.Null(await servico.ProximoResponsavelAsync(null, CancellationToken.None));

        jessica.AtuaNasVendas = true;
        jessica.RecebeLeads = false; // "Recebe leads" desligado
        await db.SaveChangesAsync();
        Assert.Null(await servico.ProximoResponsavelAsync(null, CancellationToken.None));
    }
}
