using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Mover para "Cotação" exige o valor da adesão; a oportunidade guarda os campos do formulário de venda.</summary>
public class CotacaoAdesaoTests
{
    private static LeadService LeadService(ApplicationDbContext db, ICurrentUserService usuario) =>
        new(db, usuario, new EquipeComercialService(db, usuario), new NoOpLeadAssignmentService(), new NoOpMetaConversionService(), new NoOpAuditSink());

    private static async Task<(ApplicationDbContext Db, ICurrentUserService Usuario, CrmLead Lead, CrmLeadStage Cotacao)> PrepararAsync(TestDbContextFactory factory)
    {
        var db = factory.CreateContext();
        var vendedor = await factory.CriarUsuarioAsync(db, "Vendedor");
        var cotacao = await factory.ObterOuCriarEtapaLeadAsync(db, "Cotação", 2);
        var lead = new CrmLead { NomeOuRazaoSocial = "Cliente", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = vendedor.Id };
        db.CrmLeads.Add(lead);
        await db.SaveChangesAsync();
        return (db, TestDbContextFactory.MockCurrentUser(vendedor.Id).Object, lead, cotacao);
    }

    [Fact]
    public async Task MoverParaCotacao_SemValorDaAdesao_ERecusado()
    {
        using var factory = new TestDbContextFactory();
        var (db, usuario, lead, cotacao) = await PrepararAsync(factory);

        var erro = await Assert.ThrowsAsync<CrmBusinessException>(() =>
            LeadService(db, usuario).MudarEtapaAsync(lead.Id, new ChangeLeadStageRequest(cotacao.Id, lead.RowVersion), CancellationToken.None));

        Assert.Equal("valor_adesao_obrigatorio", erro.Codigo);
        db.ChangeTracker.Clear();
        Assert.Null((await db.CrmLeads.SingleAsync()).EtapaId);
    }

    [Fact]
    public async Task MoverParaCotacao_ComValorDaAdesao_SalvaNoLead()
    {
        using var factory = new TestDbContextFactory();
        var (db, usuario, lead, cotacao) = await PrepararAsync(factory);

        var detalhe = await LeadService(db, usuario).MudarEtapaAsync(lead.Id,
            new ChangeLeadStageRequest(cotacao.Id, lead.RowVersion, ValorAdesao: 350.50m), CancellationToken.None);

        Assert.Equal(cotacao.Id, detalhe.EtapaId);
        Assert.Equal(350.50m, detalhe.ValorAdesao);
    }

    [Fact]
    public async Task OutraEtapa_NaoExigeValorDaAdesao()
    {
        using var factory = new TestDbContextFactory();
        var (db, usuario, lead, _) = await PrepararAsync(factory);
        var emAtendimento = await factory.ObterOuCriarEtapaLeadAsync(db, "Em atendimento (Leads)", 1);

        var detalhe = await LeadService(db, usuario).MudarEtapaAsync(lead.Id,
            new ChangeLeadStageRequest(emAtendimento.Id, lead.RowVersion), CancellationToken.None);

        Assert.Equal(emAtendimento.Id, detalhe.EtapaId);
    }

    [Fact]
    public async Task CriarOportunidade_GuardaOsCamposDoFormularioDeVenda()
    {
        using var factory = new TestDbContextFactory();
        var (db, usuario, lead, _) = await PrepararAsync(factory);
        var etapa = await factory.CriarEtapaAsync(db, "Novo lead", 1);
        var service = new OpportunityService(db, usuario, new EquipeComercialService(db, usuario), new NoOpMetaConversionService(), new NoOpAuditSink(), new FakeFileStorageService());

        var criada = await service.CriarAsync(new OpportunityCreateRequest(
            lead.Id, "Cliente", lead.ResponsavelId!.Value, etapa.Id, null, 0m, null, new DateOnly(2026, 10, 15), "Concorrente X", null,
            null, 150m, null, null, 350.50m, null, false, false, null,
            Cpf: "529.982.247-25", Estado: "mg", Indicacao: true, TipoIndicacao: "Lead", ValorIndicacao: 50m, Total: 300m), CancellationToken.None);

        var salva = await db.CrmOpportunities.AsNoTracking().SingleAsync(o => o.Id == criada.Id);
        Assert.Equal("529.982.247-25", salva.Cpf);
        Assert.Equal("MG", salva.Estado);
        Assert.True(salva.Indicacao);
        Assert.Equal(50m, salva.ValorIndicacao);
        Assert.Equal(300m, salva.Total);
        Assert.Equal(350.50m, salva.PagamentoAdesao);
        Assert.Equal("Concorrente X", salva.Concorrente);
        Assert.Equal(new DateOnly(2026, 10, 15), salva.DataPrevistaFechamento);
    }
}
