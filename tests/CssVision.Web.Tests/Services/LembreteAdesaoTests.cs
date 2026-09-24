using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Venda concluída com a adesão paga depois: data do pagamento, lembrete e exclusão em tempo real.</summary>
public class LembreteAdesaoTests
{
    private static DateOnly Hoje => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-3));

    private static async Task<CrmOpportunity> CriarVendaAsync(
        TestDbContextFactory factory, CssVision.Web.Data.ApplicationDbContext db, Guid responsavelId, DateOnly? dataPagamento,
        string? comprovante = null, bool arquivada = false, string nome = "Cliente")
    {
        var etapa = await db.CrmPipelineStages.FirstOrDefaultAsync() ?? await factory.CriarEtapaAsync(db, "Ganho", 1, TipoEtapaPipeline.Ganho);
        var etapaLead = await factory.ObterOuCriarEtapaLeadAsync(db);
        var lead = new CrmLead { EtapaId = etapaLead.Id, NomeOuRazaoSocial = nome, TipoPessoa = TipoPessoa.Fisica, ResponsavelId = responsavelId };
        var oportunidade = new CrmOpportunity
        {
            Lead = lead,
            Titulo = "Venda concluída",
            ResponsavelId = responsavelId,
            EtapaId = etapa.Id,
            PagamentoAdesao = 350m,
            DataPagamentoAdesaoPrevista = dataPagamento,
            PagamentoAdesaoArquivoUrl = comprovante,
            Arquivado = arquivada,
        };
        db.CrmLeads.Add(lead);
        db.CrmOpportunities.Add(oportunidade);
        await db.SaveChangesAsync();
        return oportunidade;
    }

    private static OpportunityService CriarServico(CssVision.Web.Data.ApplicationDbContext db, Guid usuarioId, ICrmEventHub? eventos = null, bool visaoTotal = false)
    {
        var usuario = TestDbContextFactory.MockCurrentUser(usuarioId, visaoTotal: visaoTotal).Object;
        return new OpportunityService(db, usuario, new EquipeComercialService(db, usuario), new NoOpMetaConversionService(),
            new NoOpAuditSink(), new FakeFileStorageService(), eventos);
    }

    [Fact]
    public async Task Lembretes_TrazSoAsVendasDoUsuarioComPagamentoVencidoESemComprovante()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var consultor = await factory.CriarUsuarioAsync(db, "Consultor");
        var outro = await factory.CriarUsuarioAsync(db, "Outro");

        var hoje = await CriarVendaAsync(factory, db, consultor.Id, Hoje, nome: "Paga hoje");
        var atrasada = await CriarVendaAsync(factory, db, consultor.Id, Hoje.AddDays(-2), nome: "Atrasada");
        await CriarVendaAsync(factory, db, consultor.Id, Hoje.AddDays(1), nome: "Amanhã");
        await CriarVendaAsync(factory, db, consultor.Id, Hoje, comprovante: "/uploads/x.pdf", nome: "Já pagou");
        await CriarVendaAsync(factory, db, consultor.Id, Hoje, arquivada: true, nome: "Excluída");
        await CriarVendaAsync(factory, db, consultor.Id, null, nome: "Sem data");
        await CriarVendaAsync(factory, db, outro.Id, Hoje, nome: "De outro consultor");

        var lembretes = await CriarServico(db, consultor.Id).ListarLembretesAdesaoAsync(CancellationToken.None);

        Assert.Equal([atrasada.Id, hoje.Id], lembretes.Select(l => l.OpportunityId));
        Assert.Equal("Atrasada", lembretes[0].LeadNome);
        Assert.Equal(350m, lembretes[0].PagamentoAdesao);
    }

    [Fact]
    public async Task ConcluirVenda_GravaDataDoPagamentoDaAdesao()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var consultor = await factory.CriarUsuarioAsync(db, "Consultor");
        var aberta = await factory.CriarEtapaAsync(db, "Novo lead", 1);
        var ganho = await factory.CriarEtapaAsync(db, "Ganho", 2, TipoEtapaPipeline.Ganho);
        var etapaLead = await factory.ObterOuCriarEtapaLeadAsync(db);
        var lead = new CrmLead { EtapaId = etapaLead.Id, NomeOuRazaoSocial = "Cliente", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = consultor.Id };
        db.CrmLeads.Add(lead);
        await db.SaveChangesAsync();

        var service = CriarServico(db, consultor.Id);
        var oportunidade = await service.CriarAsync(
            new OpportunityCreateRequest(lead.Id, "Proposta", consultor.Id, aberta.Id, null, 100m, null, null, null, null, null, null, null, null, null, null, false, false, null),
            CancellationToken.None);

        var dataPagamento = Hoje.AddDays(5);
        var concluida = await service.MudarEtapaAsync(oportunidade.Id,
            new ChangeStageRequest(ganho.Id, oportunidade.RowVersion, null, null, 100m, Hoje, DataPagamentoAdesaoPrevista: dataPagamento),
            CancellationToken.None);

        Assert.Equal(dataPagamento, concluida.DataPagamentoAdesaoPrevista);
    }

    [Fact]
    public async Task Editar_SemMandarAData_NaoApagaADataDoPagamento()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var consultor = await factory.CriarUsuarioAsync(db, "Consultor");
        var venda = await CriarVendaAsync(factory, db, consultor.Id, Hoje.AddDays(3));
        db.ChangeTracker.Clear();

        var service = CriarServico(db, consultor.Id);
        var atual = await service.ObterPorIdAsync(venda.Id, CancellationToken.None);
        var editada = await service.AtualizarAsync(venda.Id,
            new OpportunityUpdateRequest("Venda concluída", consultor.Id, null, 100m, null, null, null, "obs", null, null, null, null, null, 350m, null, false, false, null, atual.RowVersion),
            CancellationToken.None);

        Assert.Equal(Hoje.AddDays(3), editada.DataPagamentoAdesaoPrevista);
    }

    [Fact]
    public async Task ExcluirOportunidade_AvisaAsTelasAbertas()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var admin = await factory.CriarUsuarioAsync(db, "Admin");
        var consultor = await factory.CriarUsuarioAsync(db, "Consultor");
        var venda = await CriarVendaAsync(factory, db, consultor.Id, null);

        var hub = new CrmEventHub();
        using var telaDoConsultor = hub.Assinar();

        await CriarServico(db, admin.Id, hub, visaoTotal: true).ExcluirAsync(venda.Id, CancellationToken.None);

        Assert.True(telaDoConsultor.Leitor.TryRead(out var evento));
        Assert.Equal("quadro-atualizado", evento!.Tipo);
    }
}
