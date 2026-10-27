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
    public async Task MoverParaCotacao_SemOportunidade_CriaUmaEmPropostaEnviadaComAAdesao()
    {
        using var factory = new TestDbContextFactory();
        var (db, usuario, lead, cotacao) = await PrepararAsync(factory);
        var proposta = await factory.CriarEtapaAsync(db, "Proposta enviada", 5);

        await LeadService(db, usuario).MudarEtapaAsync(lead.Id, new ChangeLeadStageRequest(cotacao.Id, lead.RowVersion, ValorAdesao: 350m), CancellationToken.None);

        db.ChangeTracker.Clear();
        var oportunidade = await db.CrmOpportunities.SingleAsync();
        Assert.Equal(proposta.Id, oportunidade.EtapaId);
        Assert.Equal(350m, oportunidade.ValorEstimado);
        Assert.Equal(lead.ResponsavelId, oportunidade.ResponsavelId);
        Assert.Single(await db.CrmStageHistories.ToListAsync());
    }

    [Fact]
    public async Task MoverParaCotacao_NovamenteNaoDuplicaAOportunidade()
    {
        using var factory = new TestDbContextFactory();
        var (db, usuario, lead, cotacao) = await PrepararAsync(factory);
        await factory.CriarEtapaAsync(db, "Proposta enviada", 5);
        var service = LeadService(db, usuario);

        await service.MudarEtapaAsync(lead.Id, new ChangeLeadStageRequest(cotacao.Id, lead.RowVersion, ValorAdesao: 350m), CancellationToken.None);
        db.ChangeTracker.Clear();
        var atual = await db.CrmLeads.SingleAsync();
        var emAtendimento = await factory.ObterOuCriarEtapaLeadAsync(db, "Em atendimento (Indicação)", 1);
        await service.MudarEtapaAsync(lead.Id, new ChangeLeadStageRequest(emAtendimento.Id, atual.RowVersion), CancellationToken.None);
        db.ChangeTracker.Clear();
        atual = await db.CrmLeads.SingleAsync();
        await service.MudarEtapaAsync(lead.Id, new ChangeLeadStageRequest(cotacao.Id, atual.RowVersion, ValorAdesao: 400m), CancellationToken.None);

        db.ChangeTracker.Clear();
        Assert.Single(await db.CrmOpportunities.ToListAsync());
    }

    [Fact]
    public async Task MoverParaCotacao_OportunidadeEmEtapaAnterior_AvancaParaPropostaEnviada_MasNegociacaoFicaComoEsta()
    {
        using var factory = new TestDbContextFactory();
        var (db, usuario, lead, cotacao) = await PrepararAsync(factory);
        var novoLead = await factory.CriarEtapaAsync(db, "Novo lead", 1);
        var proposta = await factory.CriarEtapaAsync(db, "Proposta enviada", 5);
        var negociacao = await factory.CriarEtapaAsync(db, "Negociação", 6);
        db.CrmOpportunities.Add(new CrmOpportunity { LeadId = lead.Id, Titulo = "Op", ResponsavelId = lead.ResponsavelId!.Value, EtapaId = novoLead.Id, ValorEstimado = 1000m });
        await db.SaveChangesAsync();

        await LeadService(db, usuario).MudarEtapaAsync(lead.Id, new ChangeLeadStageRequest(cotacao.Id, lead.RowVersion, ValorAdesao: 350m), CancellationToken.None);
        db.ChangeTracker.Clear();
        var op = await db.CrmOpportunities.SingleAsync();
        Assert.Equal(proposta.Id, op.EtapaId);
        Assert.Equal(1000m, op.ValorEstimado);

        op.EtapaId = negociacao.Id;
        await db.SaveChangesAsync();
        var atual = await db.CrmLeads.SingleAsync();
        var emAtendimento = await factory.ObterOuCriarEtapaLeadAsync(db, "Em atendimento (Indicação)", 1);
        await LeadService(db, usuario).MudarEtapaAsync(lead.Id, new ChangeLeadStageRequest(emAtendimento.Id, atual.RowVersion), CancellationToken.None);
        db.ChangeTracker.Clear();
        atual = await db.CrmLeads.SingleAsync();
        await LeadService(db, usuario).MudarEtapaAsync(lead.Id, new ChangeLeadStageRequest(cotacao.Id, atual.RowVersion, ValorAdesao: 350m), CancellationToken.None);
        db.ChangeTracker.Clear();
        Assert.Equal(negociacao.Id, (await db.CrmOpportunities.SingleAsync()).EtapaId);
    }

    [Fact]
    public async Task OutraEtapa_NaoExigeValorDaAdesao()
    {
        using var factory = new TestDbContextFactory();
        var (db, usuario, lead, _) = await PrepararAsync(factory);
        // O lead padrão (criado manualmente, sem tipo) é de Indicação: a coluna "(Indicação)" é a dele.
        var emAtendimento = await factory.ObterOuCriarEtapaLeadAsync(db, "Em atendimento (Indicação)", 1);

        var detalhe = await LeadService(db, usuario).MudarEtapaAsync(lead.Id,
            new ChangeLeadStageRequest(emAtendimento.Id, lead.RowVersion), CancellationToken.None);

        Assert.Equal(emAtendimento.Id, detalhe.EtapaId);
    }

    [Fact]
    public async Task CriarOportunidade_ComCnpjFormatado_SalvaOsDigitosEmVezDeEstourarOLimite()
    {
        using var factory = new TestDbContextFactory();
        var (db, usuario, lead, _) = await PrepararAsync(factory);
        var etapa = await factory.CriarEtapaAsync(db, "Novo lead", 1);
        var service = new OpportunityService(db, usuario, new EquipeComercialService(db, usuario), new NoOpMetaConversionService(), new NoOpAuditSink(), new FakeFileStorageService());

        var criada = await service.CriarAsync(new OpportunityCreateRequest(
            lead.Id, "Empresa", lead.ResponsavelId!.Value, etapa.Id, null, 0m, null, new DateOnly(2026, 10, 15), null, null,
            null, 150m, null, null, 350.50m, null, false, false, null,
            Cpf: "12.345.678/0001-95", Estado: "mg", TipoIndicacao: "Lead", Total: 300m), CancellationToken.None);

        Assert.Equal("12345678000195", (await db.CrmOpportunities.AsNoTracking().SingleAsync(o => o.Id == criada.Id)).Cpf);
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

    [Fact]
    public async Task CarroZero_GuardaOChassiNoLugarDaPlaca()
    {
        using var factory = new TestDbContextFactory();
        var (db, usuario, lead, _) = await PrepararAsync(factory);
        var etapa = await factory.CriarEtapaAsync(db, "Novo lead", 1);
        var service = new OpportunityService(db, usuario, new EquipeComercialService(db, usuario), new NoOpMetaConversionService(), new NoOpAuditSink(), new FakeFileStorageService());

        var criada = await service.CriarAsync(new OpportunityCreateRequest(
            lead.Id, "Cliente", lead.ResponsavelId!.Value, etapa.Id, null, 0m, null, null, null, null,
            null, 150m, null, null, 350m, null, false, false,
            new VeiculoUpsertRequest("Onix 0km", null, 90000m, null, null, null, null, Chassi: " 9bw zzz377vt004251 ")), CancellationToken.None);

        Assert.Equal("9BWZZZ377VT004251", criada.Veiculo!.Chassi);
        Assert.Null(criada.Veiculo.Placa);
    }
}
