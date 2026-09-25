using CssVision.Web.Authorization;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Consultor que só recebe leads de certos "O que?" (ex.: Samys só AGV TRUCK).</summary>
public class RecebeSomenteOQueTests
{
    private static async Task<(Guid Samys, Guid Ana)> PrepararAsync(TestDbContextFactory factory, CssVision.Web.Data.ApplicationDbContext db)
    {
        // "Ana" vem antes no alfabeto e ninguém recebeu nada: sem a regra, ela ganharia todos.
        var ana = await factory.CriarUsuarioAsync(db, "Ana Vendedora");
        var samys = await factory.CriarUsuarioAsync(db, "Samys Alexandre");
        await factory.AtribuirPapelAsync(db, ana, Roles.Comercial);
        await factory.AtribuirPapelAsync(db, samys, Roles.Comercial);
        samys.RecebeSomenteOQue = "AGV TRUCK";
        await db.SaveChangesAsync();
        return (samys.Id, ana.Id);
    }

    [Theory]
    [InlineData("AGV", false)]
    [InlineData("AGV ELÉTRICO", false)]
    [InlineData(null, false)]
    [InlineData("AGV TRUCK", true)]
    [InlineData("agv truck", true)]
    public async Task Rodizio_SamysSoRecebeAgvTruck_ETemPreferenciaNeles(string? oQue, bool vaiParaSamys)
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var (samys, ana) = await PrepararAsync(factory, db);

        var escolhido = await new LeadAssignmentService(db).ProximoResponsavelAsync(oQue, CancellationToken.None);

        Assert.Equal(vaiParaSamys ? samys : ana, escolhido);
    }

    [Fact]
    public async Task Rodizio_AgvTruckVaiParaOsDemais_QuandoAEspecialistaBateuOLimite()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var (samys, ana) = await PrepararAsync(factory, db);
        var usuario = await db.Users.SingleAsync(u => u.Id == samys);
        usuario.LimiteMensalLeads = 1;
        db.CrmLeads.Add(new CrmLead { NomeOuRazaoSocial = "Caminhão", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = samys, MetaLeadId = "t1" });
        await db.SaveChangesAsync();

        Assert.Equal(ana, await new LeadAssignmentService(db).ProximoResponsavelAsync("AGV TRUCK", CancellationToken.None));
    }

    [Fact]
    public async Task DistribuirPendentes_UsaOOQueDoLead()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var (samys, ana) = await PrepararAsync(factory, db);
        db.CrmLeads.AddRange(
            new CrmLead { NomeOuRazaoSocial = "Caminhão", TipoPessoa = TipoPessoa.Fisica, MetaLeadId = "a", ProdutoInteresse = "AGV TRUCK", CriadoManualmente = false },
            new CrmLead { NomeOuRazaoSocial = "Carro", TipoPessoa = TipoPessoa.Fisica, MetaLeadId = "b", ProdutoInteresse = "AGV", CriadoManualmente = false });
        await db.SaveChangesAsync();

        await new LeadAssignmentService(db).DistribuirPendentesAsync(CancellationToken.None);

        var leads = await db.CrmLeads.AsNoTracking().ToDictionaryAsync(l => l.NomeOuRazaoSocial, l => l.ResponsavelId);
        Assert.Equal(samys, leads["Caminhão"]);
        Assert.Equal(ana, leads["Carro"]);
    }

    [Fact]
    public void FiltroOQue_JuntaESeparaSemRepetir()
    {
        Assert.Equal("AGV TRUCK,AGV ELÉTRICO", FiltroOQue.Juntar([" AGV TRUCK ", "agv truck", "AGV ELÉTRICO", ""]));
        Assert.Null(FiltroOQue.Juntar([]));
        Assert.Equal(["AGV TRUCK", "AGV"], FiltroOQue.Separar("AGV TRUCK,AGV"));
        Assert.True(FiltroOQue.Aceita("AGV ELÉTRICO", "AGV ELETRICO"));
        Assert.True(FiltroOQue.Aceita(null, "qualquer"));
    }
}
