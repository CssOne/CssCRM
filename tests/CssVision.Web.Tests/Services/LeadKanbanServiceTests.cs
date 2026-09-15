using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Xunit;

namespace CssVision.Web.Tests.Services;

public class LeadKanbanServiceTests
{
    [Fact]
    public async Task ObterBoardAsync_SeparaLeadsManuaisDeAutomaticos_PorPadraoMostraSoManuais()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var vendedor = await factory.CriarUsuarioAsync(db, "Vendedor1");

        db.CrmLeads.AddRange(
            new CrmLead { NomeOuRazaoSocial = "Lead Manual", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = vendedor.Id, CriadoManualmente = true },
            new CrmLead { NomeOuRazaoSocial = "Lead Meta Ads", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = vendedor.Id, Origem = "Meta ads", CriadoManualmente = false }
        );
        await db.SaveChangesAsync();

        var currentUser = TestDbContextFactory.MockCurrentUser(vendedor.Id);
        var service = new LeadKanbanService(db, new EquipeComercialService(db, currentUser.Object));

        var boardManual = await service.ObterBoardAsync(new LeadKanbanFilterRequest { CriadoManualmente = true }, CancellationToken.None);
        var boardAutomatico = await service.ObterBoardAsync(new LeadKanbanFilterRequest { CriadoManualmente = false }, CancellationToken.None);

        var cartaoManual = Assert.Single(boardManual.Colunas.SelectMany(c => c.Cartoes));
        Assert.Equal("Lead Manual", cartaoManual.NomeOuRazaoSocial);

        var cartaoAutomatico = Assert.Single(boardAutomatico.Colunas.SelectMany(c => c.Cartoes));
        Assert.Equal("Lead Meta Ads", cartaoAutomatico.NomeOuRazaoSocial);
    }

    [Fact]
    public async Task ObterBoardAsync_LeadSemCriadoManualmenteExplicito_CaiNoGrupoManualPorPadrao()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var vendedor = await factory.CriarUsuarioAsync(db, "Vendedor1");

        db.CrmLeads.Add(new CrmLead { NomeOuRazaoSocial = "Lead Antigo", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = vendedor.Id });
        await db.SaveChangesAsync();

        var currentUser = TestDbContextFactory.MockCurrentUser(vendedor.Id);
        var service = new LeadKanbanService(db, new EquipeComercialService(db, currentUser.Object));

        var board = await service.ObterBoardAsync(new LeadKanbanFilterRequest(), CancellationToken.None);

        Assert.Single(board.Colunas.SelectMany(c => c.Cartoes));
    }
}
