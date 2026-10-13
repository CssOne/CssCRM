using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CssVision.Web.Tests.Services;

public class OpportunityServiceTests
{
    private static async Task<(CrmLead Lead, CrmPipelineStage Aberta, CrmPipelineStage Ganho, CrmPipelineStage Perdido, CrmLossReason Motivo)> PrepararCenarioAsync(
        TestDbContextFactory factory, CssVision.Web.Data.ApplicationDbContext db, Guid vendedorId)
    {
        var aberta = await factory.CriarEtapaAsync(db, "Novo lead", 1);
        var ganho = await factory.CriarEtapaAsync(db, "Ganho", 2, TipoEtapaPipeline.Ganho);
        var perdido = await factory.CriarEtapaAsync(db, "Perdido", 3, TipoEtapaPipeline.Perdido);
        var motivo = new CrmLossReason { Descricao = "Preço" };
        db.CrmLossReasons.Add(motivo);
        var etapaLead = await factory.ObterOuCriarEtapaLeadAsync(db);

        var lead = new CrmLead { EtapaId = etapaLead.Id, NomeOuRazaoSocial = "Cliente", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = vendedorId };
        db.CrmLeads.Add(lead);
        await db.SaveChangesAsync();

        return (lead, aberta, ganho, perdido, motivo);
    }

    [Fact]
    public async Task MudarEtapaAsync_DeveExigirMotivo_QuandoMovePraPerdido()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var vendedor = await factory.CriarUsuarioAsync(db, "Vendedor1");
        var (lead, aberta, _, perdido, _) = await PrepararCenarioAsync(factory, db, vendedor.Id);

        var currentUser = TestDbContextFactory.MockCurrentUser(vendedor.Id);
        var equipe = new EquipeComercialService(db, currentUser.Object);
        var service = new OpportunityService(db, currentUser.Object, equipe, new NoOpMetaConversionService(), new NoOpAuditSink(), new FakeFileStorageService());

        var oportunidade = await service.CriarAsync(
            new OpportunityCreateRequest(lead.Id, "Proposta", vendedor.Id, aberta.Id, null, 1000m, null, null, null, null, null, null, null, null, null, null, false, false, null),
            CancellationToken.None);

        await Assert.ThrowsAsync<CrmBusinessException>(() =>
            service.MudarEtapaAsync(oportunidade.Id, new ChangeStageRequest(perdido.Id, oportunidade.RowVersion, null, null, null, null), CancellationToken.None));
    }

    [Fact]
    public async Task MudarEtapaAsync_DeveExigirValorEData_QuandoMovePraGanho()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var vendedor = await factory.CriarUsuarioAsync(db, "Vendedor1");
        var (lead, aberta, ganho, _, _) = await PrepararCenarioAsync(factory, db, vendedor.Id);

        var currentUser = TestDbContextFactory.MockCurrentUser(vendedor.Id);
        var equipe = new EquipeComercialService(db, currentUser.Object);
        var service = new OpportunityService(db, currentUser.Object, equipe, new NoOpMetaConversionService(), new NoOpAuditSink(), new FakeFileStorageService());

        var oportunidade = await service.CriarAsync(
            new OpportunityCreateRequest(lead.Id, "Proposta", vendedor.Id, aberta.Id, null, 1000m, null, null, null, null, null, null, null, null, null, null, false, false, null),
            CancellationToken.None);

        await Assert.ThrowsAsync<CrmBusinessException>(() =>
            service.MudarEtapaAsync(oportunidade.Id, new ChangeStageRequest(ganho.Id, oportunidade.RowVersion, null, null, null, null), CancellationToken.None));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public async Task MudarEtapaAsync_ComComprovanteDeAdesaoSemValor_RecusaAVenda(int? adesao)
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var vendedor = await factory.CriarUsuarioAsync(db, "Vendedor1");
        var (lead, aberta, ganho, _, _) = await PrepararCenarioAsync(factory, db, vendedor.Id);

        var currentUser = TestDbContextFactory.MockCurrentUser(vendedor.Id);
        var equipe = new EquipeComercialService(db, currentUser.Object);
        var service = new OpportunityService(db, currentUser.Object, equipe, new NoOpMetaConversionService(), new NoOpAuditSink(), new FakeFileStorageService());

        var oportunidade = await service.CriarAsync(
            new OpportunityCreateRequest(lead.Id, "Proposta", vendedor.Id, aberta.Id, null, 1000m, null, null, null, null, null, null, null, null, null, null, false, false, null),
            CancellationToken.None);
        // O comprovante é enviado antes de o valor chegar (fluxo do quadro), então já está gravado.
        (await db.CrmOpportunities.FirstAsync(o => o.Id == oportunidade.Id)).PagamentoAdesaoArquivoUrl = "/uploads/comprovante.pdf";
        await db.SaveChangesAsync();
        var rowVersion = (await db.CrmOpportunities.AsNoTracking().FirstAsync(o => o.Id == oportunidade.Id)).RowVersion;

        var erro = await Assert.ThrowsAsync<CrmBusinessException>(() =>
            service.MudarEtapaAsync(oportunidade.Id,
                new ChangeStageRequest(ganho.Id, rowVersion, null, null, 0m, DateOnly.FromDateTime(DateTime.UtcNow), PagamentoAdesao: adesao), CancellationToken.None));

        Assert.Equal("adesao_valor_obrigatorio", erro.Codigo);
    }

    [Fact]
    public async Task MudarEtapaAsync_DeveRegistrarHistorico_QuandoEtapaValida()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var vendedor = await factory.CriarUsuarioAsync(db, "Vendedor1");
        var (lead, aberta, ganho, _, _) = await PrepararCenarioAsync(factory, db, vendedor.Id);

        var currentUser = TestDbContextFactory.MockCurrentUser(vendedor.Id);
        var equipe = new EquipeComercialService(db, currentUser.Object);
        var service = new OpportunityService(db, currentUser.Object, equipe, new NoOpMetaConversionService(), new NoOpAuditSink(), new FakeFileStorageService());

        var oportunidade = await service.CriarAsync(
            new OpportunityCreateRequest(lead.Id, "Proposta", vendedor.Id, aberta.Id, null, 1000m, null, null, null, null, null, null, null, null, null, null, false, false, null),
            CancellationToken.None);

        var atualizada = await service.MudarEtapaAsync(
            oportunidade.Id,
            new ChangeStageRequest(ganho.Id, oportunidade.RowVersion, null, null, 1200m, HorarioBrasilia.Hoje),
            CancellationToken.None);

        Assert.Equal(ganho.Id, atualizada.EtapaId);
        Assert.Equal(1200m, atualizada.ValorFinal);

        var historico = await db.CrmStageHistories.Where(h => h.OpportunityId == oportunidade.Id).ToListAsync();
        Assert.Equal(2, historico.Count); // criação (null -> aberta) + mudança (aberta -> ganho)
        Assert.Contains(historico, h => h.EtapaAnteriorId == null && h.EtapaNovaId == aberta.Id);
        Assert.Contains(historico, h => h.EtapaAnteriorId == aberta.Id && h.EtapaNovaId == ganho.Id);
    }

    [Fact]
    public async Task MudarEtapaAsync_DeveBloquear_QuandoOportunidadeJaFechada()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var vendedor = await factory.CriarUsuarioAsync(db, "Vendedor1");
        var (lead, aberta, ganho, perdido, motivo) = await PrepararCenarioAsync(factory, db, vendedor.Id);

        var currentUser = TestDbContextFactory.MockCurrentUser(vendedor.Id);
        var equipe = new EquipeComercialService(db, currentUser.Object);
        var service = new OpportunityService(db, currentUser.Object, equipe, new NoOpMetaConversionService(), new NoOpAuditSink(), new FakeFileStorageService());

        var oportunidade = await service.CriarAsync(
            new OpportunityCreateRequest(lead.Id, "Proposta", vendedor.Id, aberta.Id, null, 1000m, null, null, null, null, null, null, null, null, null, null, false, false, null),
            CancellationToken.None);

        var ganha = await service.MudarEtapaAsync(
            oportunidade.Id, new ChangeStageRequest(ganho.Id, oportunidade.RowVersion, null, null, 1000m, HorarioBrasilia.Hoje), CancellationToken.None);

        await Assert.ThrowsAsync<CrmBusinessException>(() =>
            service.MudarEtapaAsync(oportunidade.Id, new ChangeStageRequest(perdido.Id, ganha.RowVersion, motivo.Id, null, null, null), CancellationToken.None));
    }
}
