using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Excluir lead (quadro de leads) e oportunidade (Pipeline) é só para Admin/GestorMaster.</summary>
public class ExclusaoAdminTests
{
    private static async Task<(CrmLead Lead, CrmOpportunity Oportunidade)> CriarLeadComOportunidadeAsync(
        TestDbContextFactory factory, ApplicationDbContext db, Guid vendedorId, string? tipoIndicacao = "Lead")
    {
        var etapa = await factory.CriarEtapaAsync(db, "Novo lead", 1);
        var lead = new CrmLead { NomeOuRazaoSocial = "Cliente", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = vendedorId, TipoIndicacao = tipoIndicacao };
        db.CrmLeads.Add(lead);
        var oportunidade = new CrmOpportunity { LeadId = lead.Id, Titulo = "AGV", ResponsavelId = vendedorId, EtapaId = etapa.Id };
        db.CrmOpportunities.Add(oportunidade);
        await db.SaveChangesAsync();
        return (lead, oportunidade);
    }

    private static LeadService LeadService(ApplicationDbContext db, ICurrentUserService usuario) =>
        new(db, usuario, new EquipeComercialService(db, usuario), new NoOpLeadAssignmentService(), new NoOpMetaConversionService(), new NoOpAuditSink());

    private static OpportunityService OpportunityService(ApplicationDbContext db, ICurrentUserService usuario) =>
        new(db, usuario, new EquipeComercialService(db, usuario), new NoOpMetaConversionService(), new NoOpAuditSink(), new FakeFileStorageService());

    [Fact]
    public async Task Admin_ExcluiQualquerLead_EArquivaAsOportunidadesDele()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var admin = await factory.CriarUsuarioAsync(db, "Admin");
        var vendedor = await factory.CriarUsuarioAsync(db, "Vendedor");
        // Lead com etiqueta "Lead" (antes só "Indicação" podia ser excluído).
        var (lead, oportunidade) = await CriarLeadComOportunidadeAsync(factory, db, vendedor.Id, tipoIndicacao: "Lead");

        await LeadService(db, TestDbContextFactory.MockCurrentUser(admin.Id, visaoTotal: true).Object).ExcluirAsync(lead.Id, CancellationToken.None);

        db.ChangeTracker.Clear();
        Assert.True((await db.CrmLeads.SingleAsync(l => l.Id == lead.Id)).Arquivado);
        var arquivada = await db.CrmOpportunities.SingleAsync(o => o.Id == oportunidade.Id);
        Assert.True(arquivada.Arquivado);
        Assert.Equal(admin.Id, arquivada.ArquivadoPorId);
    }

    [Theory]
    [InlineData("Indicação")] // consultores podiam excluir Indicação; agora não mais
    [InlineData("Lead")]
    public async Task Consultor_NaoExcluiLead(string tipoIndicacao)
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var vendedor = await factory.CriarUsuarioAsync(db, "Vendedor");
        var (lead, _) = await CriarLeadComOportunidadeAsync(factory, db, vendedor.Id, tipoIndicacao);

        await Assert.ThrowsAsync<CrmForbiddenException>(() =>
            LeadService(db, TestDbContextFactory.MockCurrentUser(vendedor.Id).Object).ExcluirAsync(lead.Id, CancellationToken.None));

        Assert.False((await db.CrmLeads.SingleAsync(l => l.Id == lead.Id)).Arquivado);
    }

    [Fact]
    public async Task GestorComercial_NaoExcluiLead()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var gestor = await factory.CriarUsuarioAsync(db, "Gestor");
        var vendedor = await factory.CriarUsuarioAsync(db, "Vendedor", gestor.Id);
        var (lead, _) = await CriarLeadComOportunidadeAsync(factory, db, vendedor.Id);

        await Assert.ThrowsAsync<CrmForbiddenException>(() =>
            LeadService(db, TestDbContextFactory.MockCurrentUser(gestor.Id, gestorComercial: true, podeGerir: true).Object)
                .ExcluirAsync(lead.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Admin_ExcluiCardDoPipeline_SemMexerNoLead()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var admin = await factory.CriarUsuarioAsync(db, "Admin");
        var vendedor = await factory.CriarUsuarioAsync(db, "Vendedor");
        var (lead, oportunidade) = await CriarLeadComOportunidadeAsync(factory, db, vendedor.Id);

        await OpportunityService(db, TestDbContextFactory.MockCurrentUser(admin.Id, visaoTotal: true).Object)
            .ExcluirAsync(oportunidade.Id, CancellationToken.None);

        db.ChangeTracker.Clear();
        Assert.True((await db.CrmOpportunities.SingleAsync(o => o.Id == oportunidade.Id)).Arquivado);
        Assert.False((await db.CrmLeads.SingleAsync(l => l.Id == lead.Id)).Arquivado);
    }

    [Fact]
    public async Task Consultor_NaoExcluiCardDoPipeline()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var vendedor = await factory.CriarUsuarioAsync(db, "Vendedor");
        var (_, oportunidade) = await CriarLeadComOportunidadeAsync(factory, db, vendedor.Id);

        await Assert.ThrowsAsync<CrmForbiddenException>(() =>
            OpportunityService(db, TestDbContextFactory.MockCurrentUser(vendedor.Id).Object).ExcluirAsync(oportunidade.Id, CancellationToken.None));
    }
}
