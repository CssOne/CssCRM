using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Services.Marketing;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>
/// Concluir a venda leva o card do lead para "Venda concluída (Leads)" ou "(Indicação)" na mesma operação. Antes a tela movia o card num segundo
/// passo e, quando ele falhava, a venda ficava ganha com o card parado em "Em atendimento"/"Cotação".
/// </summary>
public class VendaLevaOCardParaVendaConcluidaTests
{
    private sealed class ConversaoGravadora : IMetaConversionService
    {
        public List<(string Etapa, decimal? Valor)> EventosDeEtapa { get; } = [];

        public Task<bool> EnviarConversaoVendaAsync(CrmLead lead, CrmOpportunity opportunity, CancellationToken ct) => Task.FromResult(false);

        public Task<bool> EnviarEventoEtapaAsync(CrmLead lead, Guid etapaId, string etapaNome, CancellationToken ct, decimal? valor = null)
        {
            EventosDeEtapa.Add((etapaNome, valor));
            return Task.FromResult(true);
        }
    }

    private sealed class Cenario
    {
        public required OpportunityService Servico { get; init; }
        public required ApplicationDbContext Db { get; init; }
        public required CrmPipelineStage Aberta { get; init; }
        public required CrmPipelineStage Ganho { get; init; }
        public required Guid VendedorId { get; init; }
        public required CrmLeadStage EmAtendimento { get; init; }
        public required CrmLeadStage VendaLeads { get; init; }
        public required CrmLeadStage VendaIndicacao { get; init; }
        public required ConversaoGravadora Conversao { get; init; }
    }

    private static async Task<Cenario> MontarAsync(TestDbContextFactory factory, bool comColunasDeVenda = true)
    {
        var db = factory.CreateContext();
        var vendedor = await factory.CriarUsuarioAsync(db, "Vendedor1");
        var aberta = await factory.CriarEtapaAsync(db, "Novo lead", 1);
        var ganho = await factory.CriarEtapaAsync(db, "Ganho", 2, TipoEtapaPipeline.Ganho);
        var emAtendimento = await factory.ObterOuCriarEtapaLeadAsync(db, "Em atendimento (Leads)", 1);
        CrmLeadStage? leads = null, indicacao = null;
        if (comColunasDeVenda)
        {
            leads = await factory.ObterOuCriarEtapaLeadAsync(db, "Venda concluída (Leads)", 2);
            indicacao = await factory.ObterOuCriarEtapaLeadAsync(db, "Venda concluída (Indicação)", 3);
        }

        var conversao = new ConversaoGravadora();
        var usuario = TestDbContextFactory.MockCurrentUser(vendedor.Id);
        var servico = new OpportunityService(db, usuario.Object, new EquipeComercialService(db, usuario.Object), conversao, new NoOpAuditSink(), new FakeFileStorageService());
        return new Cenario { Servico = servico, Db = db, Aberta = aberta, Ganho = ganho, VendedorId = vendedor.Id, EmAtendimento = emAtendimento, VendaLeads = leads!, VendaIndicacao = indicacao!, Conversao = conversao };
    }

    private static async Task<CrmLead> LeadAsync(Cenario c, string? tipo, bool manual, Guid? etapaId = null, decimal? valorAdesao = null)
    {
        var lead = new CrmLead
        {
            NomeOuRazaoSocial = "Cliente", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = c.VendedorId, EtapaId = etapaId ?? c.EmAtendimento.Id,
            TipoIndicacao = tipo, CriadoManualmente = manual, ValorAdesao = valorAdesao,
        };
        c.Db.CrmLeads.Add(lead);
        await c.Db.SaveChangesAsync();
        return lead;
    }

    private static async Task<OpportunityDto> ConcluirAsync(Cenario c, CrmLead lead, bool? indicacao = null, decimal adesao = 150m)
    {
        var proposta = await c.Servico.CriarAsync(
            new OpportunityCreateRequest(lead.Id, "Venda concluída", c.VendedorId, c.Aberta.Id, null, 1000m, null, null, null, null, null, null, null, null, null, null, false, false, null),
            CancellationToken.None);
        return await c.Servico.MudarEtapaAsync(proposta.Id,
            new ChangeStageRequest(c.Ganho.Id, proposta.RowVersion, null, null, 1200m, HorarioBrasilia.Hoje, Indicacao: indicacao, PagamentoAdesao: adesao), CancellationToken.None);
    }

    private static async Task<CrmLead> RecarregarAsync(Cenario c, Guid id)
    {
        c.Db.ChangeTracker.Clear();
        return await c.Db.CrmLeads.AsNoTracking().SingleAsync(l => l.Id == id);
    }

    [Fact]
    public async Task LeadComum_VaiParaVendaConcluidaLeads()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var lead = await LeadAsync(c, "Lead", manual: false);

        await ConcluirAsync(c, lead);

        Assert.Equal(c.VendaLeads.Id, (await RecarregarAsync(c, lead.Id)).EtapaId);
    }

    [Fact]
    public async Task ClienteDeIndicacao_VaiParaVendaConcluidaIndicacao()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var lead = await LeadAsync(c, "Pessoal", manual: true);

        await ConcluirAsync(c, lead);

        var salvo = await RecarregarAsync(c, lead.Id);
        Assert.Equal(c.VendaIndicacao.Id, salvo.EtapaId);
        Assert.Equal("Pessoal", salvo.TipoIndicacao); // a etiqueta que já tinha não muda
    }

    [Fact]
    public async Task LeadQueFechouComoIndicacao_VaiParaAColunaDasIndicacoes_ComAEtiquetaIndicacaoLead()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var lead = await LeadAsync(c, "Lead", manual: false);

        await ConcluirAsync(c, lead, indicacao: true);

        var salvo = await RecarregarAsync(c, lead.Id);
        Assert.Equal(c.VendaIndicacao.Id, salvo.EtapaId);
        Assert.Equal("Indicação Lead", salvo.TipoIndicacao);
    }

    [Fact]
    public async Task CardParadoEmCotacaoOuPerdido_SaiDeLaELimpaOMotivoDaPerda()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var cotacao = await factory.ObterOuCriarEtapaLeadAsync(c.Db, "Cotação", 9);
        var lead = await LeadAsync(c, "Lead", manual: false, etapaId: cotacao.Id, valorAdesao: 200m);
        lead.VeiculoNaoAtendido = "Trator";
        await c.Db.SaveChangesAsync();

        await ConcluirAsync(c, lead);

        var salvo = await RecarregarAsync(c, lead.Id);
        Assert.Equal(c.VendaLeads.Id, salvo.EtapaId);
        Assert.Null(salvo.VeiculoNaoAtendido);
    }

    [Fact]
    public async Task CardJaNaColunaCerta_NaoMudaDeNovo_ENaoManda2oEventoDeEtapa()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var lead = await LeadAsync(c, "Lead", manual: false, etapaId: null);
        lead.EtapaId = c.VendaLeads.Id;
        await c.Db.SaveChangesAsync();

        await ConcluirAsync(c, lead);

        Assert.Equal(c.VendaLeads.Id, (await RecarregarAsync(c, lead.Id)).EtapaId);
        Assert.Empty(c.Conversao.EventosDeEtapa);
    }

    [Fact]
    public async Task AoMoverOCard_Manda_OEventoDaEtapaVendaConcluida_ComOValorDaAdesao()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var lead = await LeadAsync(c, "Lead", manual: false);

        await ConcluirAsync(c, lead, adesao: 321m);

        var evento = Assert.Single(c.Conversao.EventosDeEtapa);
        Assert.Equal(("Venda concluída (Leads)", (decimal?)321m), evento);
    }

    [Fact]
    public async Task SemAsColunasDeVendaNoQuadro_AVendaConcluiMesmoAssim_ECardFicaOndeEstava()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory, comColunasDeVenda: false);
        var lead = await LeadAsync(c, "Lead", manual: false);

        var concluida = await ConcluirAsync(c, lead);

        Assert.Equal(c.Ganho.Id, concluida.EtapaId);
        Assert.Equal(c.EmAtendimento.Id, (await RecarregarAsync(c, lead.Id)).EtapaId);
    }
}
