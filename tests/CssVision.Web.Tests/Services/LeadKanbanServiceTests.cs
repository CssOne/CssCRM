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

    [Fact]
    public async Task ObterBoardAsync_CartaoMostraPlacaDoLead_OuDoVeiculoDaOportunidade()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var vendedor = await factory.CriarUsuarioAsync(db, "Vendedor1");
        var etapaPipeline = await factory.CriarEtapaAsync(db, "Ganho", 1, TipoEtapaPipeline.Ganho);

        var comPlaca = new CrmLead { NomeOuRazaoSocial = "Com placa", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = vendedor.Id, Placa = "ABC1D23" };
        var soNoVeiculo = new CrmLead { NomeOuRazaoSocial = "Placa no veículo", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = vendedor.Id };
        db.CrmLeads.AddRange(comPlaca, soNoVeiculo);
        var oportunidade = new CrmOpportunity { LeadId = soNoVeiculo.Id, Titulo = "AGV", ResponsavelId = vendedor.Id, EtapaId = etapaPipeline.Id };
        oportunidade.Veiculo = new CrmVeiculo { OpportunityId = oportunidade.Id, Placa = "XYZ9K87" };
        db.CrmOpportunities.Add(oportunidade);
        await db.SaveChangesAsync();

        var currentUser = TestDbContextFactory.MockCurrentUser(vendedor.Id);
        var service = new LeadKanbanService(db, new EquipeComercialService(db, currentUser.Object));

        var cartoes = (await service.ObterBoardAsync(new LeadKanbanFilterRequest(), CancellationToken.None))
            .Colunas.SelectMany(c => c.Cartoes).ToDictionary(c => c.NomeOuRazaoSocial);

        Assert.Equal("ABC1D23", cartoes["Com placa"].Placa);
        Assert.Equal("XYZ9K87", cartoes["Placa no veículo"].Placa);
    }
}
