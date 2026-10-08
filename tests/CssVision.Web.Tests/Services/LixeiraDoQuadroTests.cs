using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Lixeira do quadro de leads: o consultor exclui só card de indicação; qualquer um restaura o que pode excluir.</summary>
public class LixeiraDoQuadroTests
{
    private static LeadService Servico(ApplicationDbContext db, ICurrentUserService usuario) =>
        new(db, usuario, new EquipeComercialService(db, usuario), new NoOpLeadAssignmentService(), new NoOpMetaConversionService(), new NoOpAuditSink());

    private static async Task<(CrmLead Lead, CrmOpportunity Oportunidade)> LeadAsync(TestDbContextFactory factory, ApplicationDbContext db, Guid vendedorId, string tipo)
    {
        var etapa = await factory.CriarEtapaAsync(db, "Novo", 1);
        var lead = new CrmLead { NomeOuRazaoSocial = "Cliente " + tipo, TipoPessoa = TipoPessoa.Fisica, ResponsavelId = vendedorId, TipoIndicacao = tipo };
        db.CrmLeads.Add(lead);
        var oportunidade = new CrmOpportunity { LeadId = lead.Id, Titulo = "AGV", ResponsavelId = vendedorId, EtapaId = etapa.Id };
        db.CrmOpportunities.Add(oportunidade);
        await db.SaveChangesAsync();
        return (lead, oportunidade);
    }

    [Fact]
    public async Task Consultor_ExcluiCardDeIndicacao_VaiParaALixeira_ERestaura()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var consultor = await factory.CriarUsuarioAsync(db, "Consultor");
        var (lead, oportunidade) = await LeadAsync(factory, db, consultor.Id, "Pessoal");
        var servico = Servico(db, TestDbContextFactory.MockCurrentUser(consultor.Id).Object);

        await servico.ExcluirAsync(lead.Id, CancellationToken.None);

        var lixeira = Assert.Single(await servico.ListarLixeiraAsync(CancellationToken.None));
        Assert.Equal(lead.Id, lixeira.Id);
        Assert.Equal("Consultor", lixeira.ExcluidoPor);

        await servico.RestaurarAsync(lead.Id, CancellationToken.None);

        db.ChangeTracker.Clear();
        Assert.False((await db.CrmLeads.SingleAsync(l => l.Id == lead.Id)).Arquivado);
        Assert.False((await db.CrmOpportunities.SingleAsync(o => o.Id == oportunidade.Id)).Arquivado);
        Assert.Empty(await servico.ListarLixeiraAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Consultor_NaoExcluiNemRestauraCardDeLead_NemVeEleNaLixeira()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var consultor = await factory.CriarUsuarioAsync(db, "Consultor");
        var admin = await factory.CriarUsuarioAsync(db, "Admin");
        var (lead, _) = await LeadAsync(factory, db, consultor.Id, "Lead");
        var doConsultor = Servico(db, TestDbContextFactory.MockCurrentUser(consultor.Id).Object);

        await Assert.ThrowsAsync<CrmForbiddenException>(() => doConsultor.ExcluirAsync(lead.Id, CancellationToken.None));

        // O admin exclui o lead: o consultor não o vê na lixeira nem consegue trazê-lo de volta.
        await Servico(db, TestDbContextFactory.MockCurrentUser(admin.Id, visaoTotal: true).Object).ExcluirAsync(lead.Id, CancellationToken.None);
        Assert.Empty(await doConsultor.ListarLixeiraAsync(CancellationToken.None));
        await Assert.ThrowsAsync<CrmForbiddenException>(() => doConsultor.RestaurarAsync(lead.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Restaurar_NaoTrazDeVoltaOportunidadeExcluidaAntes_ENaoMexeEmLeadArquivadoSemSerExclusao()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var admin = await factory.CriarUsuarioAsync(db, "Admin");
        var vendedor = await factory.CriarUsuarioAsync(db, "Vendedor");
        var (lead, oportunidade) = await LeadAsync(factory, db, vendedor.Id, "Lead");
        var etapa = await db.CrmPipelineStages.FirstAsync();
        var antiga = new CrmOpportunity
        {
            LeadId = lead.Id, Titulo = "Antiga", ResponsavelId = vendedor.Id, EtapaId = etapa.Id,
            Arquivado = true, ArquivadoEm = DateTimeOffset.UtcNow.AddDays(-5), ArquivadoPorId = admin.Id,
        };
        var arquivadoNaMao = new CrmLead { NomeOuRazaoSocial = "Arquivado sem usuário", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = vendedor.Id, Arquivado = true, ArquivadoEm = DateTimeOffset.UtcNow };
        db.CrmOpportunities.Add(antiga);
        db.CrmLeads.Add(arquivadoNaMao);
        await db.SaveChangesAsync();
        var servico = Servico(db, TestDbContextFactory.MockCurrentUser(admin.Id, visaoTotal: true).Object);

        await servico.ExcluirAsync(lead.Id, CancellationToken.None);
        var lixeira = await servico.ListarLixeiraAsync(CancellationToken.None);
        Assert.Equal([lead.Id], lixeira.Select(l => l.Id).ToArray()); // o arquivado "na mão" (sem quem excluiu) não é lixeira
        await Assert.ThrowsAsync<CrmNotFoundException>(() => servico.RestaurarAsync(arquivadoNaMao.Id, CancellationToken.None));

        await servico.RestaurarAsync(lead.Id, CancellationToken.None);

        db.ChangeTracker.Clear();
        Assert.False((await db.CrmOpportunities.SingleAsync(o => o.Id == oportunidade.Id)).Arquivado);
        Assert.True((await db.CrmOpportunities.SingleAsync(o => o.Id == antiga.Id)).Arquivado);
    }
}
