using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Authorization;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>
/// Formulário de caminhão: o lead entra no rodízio de quem recebe AGV TRUCK (Samys e Caroline),
/// dividindo entre elas — antes ia fixo para a Samys.
/// </summary>
public class FormularioCaminhaoTests
{
    private static PublicLeadCreateRequest Request(int i, string projeto) =>
        new($"Caminhoneiro {i}", $"3199990{i:0000}", $"caminhao{i}@site.com", "MG", null, null, null, null, null, null, null, null,
            "Facebook ADS", null, null, projeto);

    [Fact]
    public async Task LeadsDoFormularioDeCaminhao_DivididosEntreQuemRecebeAgvTruck()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var ana = await factory.CriarUsuarioAsync(db, "Ana Vendedora");
        var samys = await factory.CriarUsuarioAsync(db, "Samys Alexandre");
        var caroline = await factory.CriarUsuarioAsync(db, "Caroline Aguiar");
        foreach (var u in new[] { ana, samys, caroline }) await factory.AtribuirPapelAsync(db, u, Roles.Comercial);
        ana.RecebeSomenteOQue = "AGV,AGV ELÉTRICO";
        samys.RecebeSomenteOQue = "AGV TRUCK";
        caroline.RecebeSomenteOQue = "AGV TRUCK";
        await db.SaveChangesAsync();

        var service = new PublicLeadIntakeService(db, new LeadAssignmentService(db), NullLogger<PublicLeadIntakeService>.Instance);
        for (var i = 0; i < 4; i++)
        {
            // O formulário de caminhão nem manda o "O que?": é reconhecido pelo form_id.
            await service.CriarAsync(Request(i, "meta-instant-1148948400894957"), CancellationToken.None);
        }

        var porResponsavel = await db.CrmLeads.GroupBy(l => l.ResponsavelId).Select(g => new { g.Key, Qtd = g.Count() }).ToListAsync();
        Assert.Equal(2, porResponsavel.Single(x => x.Key == samys.Id).Qtd);
        Assert.Equal(2, porResponsavel.Single(x => x.Key == caroline.Id).Qtd);
        Assert.DoesNotContain(porResponsavel, x => x.Key == ana.Id);
    }
}
