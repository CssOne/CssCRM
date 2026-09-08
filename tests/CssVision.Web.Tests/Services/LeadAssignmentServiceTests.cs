using CssVision.Web.Authorization;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Xunit;

namespace CssVision.Web.Tests.Services;

public class LeadAssignmentServiceTests
{
    [Fact]
    public async Task ProximoResponsavelAsync_DeveRetornarNulo_QuandoNaoHaVendedorComercial()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        await factory.CriarUsuarioAsync(db, "Gestora"); // sem papel Comercial

        var service = new LeadAssignmentService(db);
        Assert.Null(await service.ProximoResponsavelAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ProximoResponsavelAsync_DeveEscolherOrdemAlfabetica_QuandoNinguemRecebeuAindaNoMes()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var zeta = await factory.CriarUsuarioAsync(db, "Zeta Vendedora");
        var ana = await factory.CriarUsuarioAsync(db, "Ana Vendedora");
        await factory.AtribuirPapelAsync(db, zeta, Roles.Comercial);
        await factory.AtribuirPapelAsync(db, ana, Roles.Comercial);

        var service = new LeadAssignmentService(db);
        var escolhido = await service.ProximoResponsavelAsync(CancellationToken.None);

        Assert.Equal(ana.Id, escolhido);
    }

    [Fact]
    public async Task ProximoResponsavelAsync_DeveEscolherQuemRecebeuMenosLeadsNoMes()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var ana = await factory.CriarUsuarioAsync(db, "Ana Vendedora");
        var bruna = await factory.CriarUsuarioAsync(db, "Bruna Vendedora");
        await factory.AtribuirPapelAsync(db, ana, Roles.Comercial);
        await factory.AtribuirPapelAsync(db, bruna, Roles.Comercial);

        // Ana já recebeu 2 este mês, Bruna nenhum — deve escolher Bruna mesmo Ana vindo antes no alfabeto.
        db.CrmLeads.AddRange(
            new CrmLead { NomeOuRazaoSocial = "L1", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = ana.Id },
            new CrmLead { NomeOuRazaoSocial = "L2", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = ana.Id });
        await db.SaveChangesAsync();

        var service = new LeadAssignmentService(db);
        var escolhido = await service.ProximoResponsavelAsync(CancellationToken.None);

        Assert.Equal(bruna.Id, escolhido);
    }

    [Fact]
    public async Task ProximoResponsavelAsync_DevePular_QuemBateuLimiteMensal()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var ana = await factory.CriarUsuarioAsync(db, "Ana Vendedora");
        var bruna = await factory.CriarUsuarioAsync(db, "Bruna Vendedora");
        await factory.AtribuirPapelAsync(db, ana, Roles.Comercial);
        await factory.AtribuirPapelAsync(db, bruna, Roles.Comercial);

        ana.LimiteMensalLeads = 1;
        db.CrmLeads.Add(new CrmLead { NomeOuRazaoSocial = "L1", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = ana.Id });
        await db.SaveChangesAsync();

        var service = new LeadAssignmentService(db);
        var escolhido = await service.ProximoResponsavelAsync(CancellationToken.None);

        // Ana já bateu o limite (1/1) — mesmo tendo "menos" leads que ninguém mais, Bruna (0 recebidos, sem limite) é escolhida.
        Assert.Equal(bruna.Id, escolhido);
    }

    [Fact]
    public async Task ProximoResponsavelAsync_DeveRetornarNulo_QuandoTodosBateramLimite()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var ana = await factory.CriarUsuarioAsync(db, "Ana Vendedora");
        await factory.AtribuirPapelAsync(db, ana, Roles.Comercial);

        ana.LimiteMensalLeads = 1;
        db.CrmLeads.Add(new CrmLead { NomeOuRazaoSocial = "L1", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = ana.Id });
        await db.SaveChangesAsync();

        var service = new LeadAssignmentService(db);
        Assert.Null(await service.ProximoResponsavelAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ProximoResponsavelAsync_DeveIgnorarVendedorInativo()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var ana = await factory.CriarUsuarioAsync(db, "Ana Vendedora");
        await factory.AtribuirPapelAsync(db, ana, Roles.Comercial);
        ana.Ativo = false;
        await db.SaveChangesAsync();

        var service = new LeadAssignmentService(db);
        Assert.Null(await service.ProximoResponsavelAsync(CancellationToken.None));
    }
}
