using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Domain.Identity;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Consultor com usuário inativo não recebe lead nem oportunidade.</summary>
public class ConsultorInativoTests
{
    private sealed record Cenario(ApplicationDbContext Db, ApplicationUser Admin, ApplicationUser Ativo, ApplicationUser Inativo, CrmLead Lead, CrmPipelineStage Etapa);

    private static async Task<Cenario> PrepararAsync(TestDbContextFactory factory)
    {
        var db = factory.CreateContext();
        var admin = await factory.CriarUsuarioAsync(db, "Admin");
        var ativo = await factory.CriarUsuarioAsync(db, "Ativo");
        var inativo = await factory.CriarUsuarioAsync(db, "Inativo");
        inativo.Ativo = false;
        var etapa = await factory.CriarEtapaAsync(db, "Novo lead", 1);
        var lead = new CrmLead { NomeOuRazaoSocial = "Cliente", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = ativo.Id };
        db.CrmLeads.Add(lead);
        await db.SaveChangesAsync();
        return new Cenario(db, admin, ativo, inativo, lead, etapa);
    }

    private static LeadService LeadService(ApplicationDbContext db, ICurrentUserService usuario) =>
        new(db, usuario, new EquipeComercialService(db, usuario), new NoOpLeadAssignmentService(), new NoOpMetaConversionService(), new NoOpAuditSink());

    private static OpportunityService OpportunityService(ApplicationDbContext db, ICurrentUserService usuario) =>
        new(db, usuario, new EquipeComercialService(db, usuario), new NoOpMetaConversionService(), new NoOpAuditSink(), new FakeFileStorageService());

    private static OpportunityCreateRequest NovaOportunidade(Guid leadId, Guid responsavelId, Guid etapaId) =>
        new(leadId, "AGV", responsavelId, etapaId, null, 100m, null, null, null, null, null, null, null, null, null, null, false, false, null);

    [Fact]
    public async Task AtribuirLead_AConsultorInativo_ERecusado()
    {
        using var factory = new TestDbContextFactory();
        var c = await PrepararAsync(factory);
        var admin = TestDbContextFactory.MockCurrentUser(c.Admin.Id, visaoTotal: true).Object;

        var erro = await Assert.ThrowsAsync<CrmBusinessException>(() =>
            LeadService(c.Db, admin).AtribuirAsync(c.Lead.Id, new LeadAssignRequest(c.Inativo.Id, null), CancellationToken.None));

        Assert.Equal("responsavel_inativo", erro.Codigo);
        Assert.Equal(c.Ativo.Id, (await c.Db.CrmLeads.AsNoTracking().SingleAsync()).ResponsavelId);
    }

    [Fact]
    public async Task RedistribuirEmLote_AConsultorInativo_ERecusado()
    {
        using var factory = new TestDbContextFactory();
        var c = await PrepararAsync(factory);
        var admin = TestDbContextFactory.MockCurrentUser(c.Admin.Id, visaoTotal: true).Object;

        await Assert.ThrowsAsync<CrmBusinessException>(() =>
            LeadService(c.Db, admin).AtribuirEmLoteAsync(new LeadBulkAssignRequest([c.Lead.Id], c.Inativo.Id, null), CancellationToken.None));
    }

    [Fact]
    public async Task AtribuirLead_AConsultorAtivo_Funciona()
    {
        using var factory = new TestDbContextFactory();
        var c = await PrepararAsync(factory);
        var outro = await factory.CriarUsuarioAsync(c.Db, "Outro");
        var admin = TestDbContextFactory.MockCurrentUser(c.Admin.Id, visaoTotal: true).Object;

        await LeadService(c.Db, admin).AtribuirAsync(c.Lead.Id, new LeadAssignRequest(outro.Id, null), CancellationToken.None);

        Assert.Equal(outro.Id, (await c.Db.CrmLeads.AsNoTracking().SingleAsync()).ResponsavelId);
    }

    [Fact]
    public async Task CriarOportunidade_ParaConsultorInativo_ERecusado()
    {
        using var factory = new TestDbContextFactory();
        var c = await PrepararAsync(factory);
        var admin = TestDbContextFactory.MockCurrentUser(c.Admin.Id, visaoTotal: true).Object;

        await Assert.ThrowsAsync<CrmBusinessException>(() =>
            OpportunityService(c.Db, admin).CriarAsync(NovaOportunidade(c.Lead.Id, c.Inativo.Id, c.Etapa.Id), CancellationToken.None));
    }

    [Fact]
    public async Task EditarOportunidade_DeQuemFicouInativo_SemTrocarResponsavel_Funciona()
    {
        using var factory = new TestDbContextFactory();
        var c = await PrepararAsync(factory);
        var admin = TestDbContextFactory.MockCurrentUser(c.Admin.Id, visaoTotal: true).Object;
        var service = OpportunityService(c.Db, admin);
        var oportunidade = await service.CriarAsync(NovaOportunidade(c.Lead.Id, c.Ativo.Id, c.Etapa.Id), CancellationToken.None);

        // O consultor fica inativo depois de já ter a oportunidade.
        var consultor = await c.Db.Users.SingleAsync(u => u.Id == c.Ativo.Id);
        consultor.Ativo = false;
        await c.Db.SaveChangesAsync();

        var atualizada = await service.AtualizarAsync(oportunidade.Id,
            new OpportunityUpdateRequest("AGV editado", c.Ativo.Id, null, 150m, null, null, null, null, null, null, null, null, null, null, null, false, false, null, oportunidade.RowVersion),
            CancellationToken.None);

        Assert.Equal("AGV editado", atualizada.Titulo);
    }
}
