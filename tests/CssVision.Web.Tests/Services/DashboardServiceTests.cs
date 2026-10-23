using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Xunit;

namespace CssVision.Web.Tests.Services;

public class DashboardServiceTests
{
    [Fact]
    public async Task ObterAsync_DeveCalcularIndicadores_ComBaseNasOportunidadesFechadas()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var vendedor = await factory.CriarUsuarioAsync(db, "Vendedor1");

        var aberta = await factory.CriarEtapaAsync(db, "Novo lead", 1);
        var ganho = await factory.CriarEtapaAsync(db, "Ganho", 2, TipoEtapaPipeline.Ganho);
        var perdido = await factory.CriarEtapaAsync(db, "Perdido", 3, TipoEtapaPipeline.Perdido);
        var etapaLead = await factory.ObterOuCriarEtapaLeadAsync(db);

        var lead1 = new CrmLead { EtapaId = etapaLead.Id, NomeOuRazaoSocial = "Cliente 1", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = vendedor.Id };
        var lead2 = new CrmLead { EtapaId = etapaLead.Id, NomeOuRazaoSocial = "Cliente 2", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = vendedor.Id };
        db.CrmLeads.AddRange(lead1, lead2);
        await db.SaveChangesAsync();

        var agora = DateTimeOffset.UtcNow;
        db.CrmOpportunities.AddRange(
            new CrmOpportunity { LeadId = lead1.Id, Titulo = "Op1", ResponsavelId = vendedor.Id, EtapaId = ganho.Id, ValorEstimado = 1000m, ValorFinal = 1000m, DataEfetivaFechamento = agora },
            new CrmOpportunity { LeadId = lead2.Id, Titulo = "Op2", ResponsavelId = vendedor.Id, EtapaId = perdido.Id, ValorEstimado = 500m, DataEfetivaFechamento = agora },
            new CrmOpportunity { LeadId = lead1.Id, Titulo = "Op3 aberta", ResponsavelId = vendedor.Id, EtapaId = aberta.Id, ValorEstimado = 2000m }
        );
        await db.SaveChangesAsync();

        var currentUser = TestDbContextFactory.MockCurrentUser(vendedor.Id);
        var equipe = new EquipeComercialService(db, currentUser.Object);
        var activityService = new ActivityService(db, currentUser.Object, equipe, new NoOpAuditSink());
        var service = new DashboardService(db, equipe, activityService, currentUser.Object);

        var hoje = DateOnly.FromDateTime(DateTime.UtcNow);
        var resultado = await service.ObterAsync(new DashboardFilterRequest(new DateOnly(hoje.Year, hoje.Month, 1), hoje, null), CancellationToken.None);

        Assert.Equal(1, resultado.Indicadores.OportunidadesAbertas);
        Assert.Equal(2000m, resultado.Indicadores.ValorPipeline);
        Assert.Equal(1, resultado.Indicadores.VendasGanhasQuantidade);
        Assert.Equal(1000m, resultado.Indicadores.VendasGanhasValor);
        Assert.Equal(1000m, resultado.Indicadores.TicketMedio);
        Assert.Equal(50m, resultado.Indicadores.TaxaConversao); // 1 ganha / (1 ganha + 1 perdida)
    }

    [Fact]
    public async Task ObterAsync_ValorPipeline_SomaAAdesaoInformadaNaCotacaoDoQuadroDeLeads()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var vendedor = await factory.CriarUsuarioAsync(db, "Vendedor1");
        var aberta = await factory.CriarEtapaAsync(db, "Novo lead", 1);
        var cotacao = new CrmLeadStage { Nome = "Cotação", Ordem = 2 };
        var outra = new CrmLeadStage { Nome = "Em atendimento", Ordem = 1 };
        db.CrmLeadStages.AddRange(cotacao, outra);
        await db.SaveChangesAsync();

        CrmLead Novo(string nome, Guid etapa, decimal? adesao) =>
            new() { EtapaId = etapa, NomeOuRazaoSocial = nome, TipoPessoa = TipoPessoa.Fisica, ResponsavelId = vendedor.Id, ValorAdesao = adesao };
        var semOportunidade = Novo("Só cotação", cotacao.Id, 300m);
        var comOportunidade = Novo("Já virou proposta", cotacao.Id, 999m);
        var outraEtapa = Novo("Outra etapa", outra.Id, 500m);
        db.CrmLeads.AddRange(semOportunidade, comOportunidade, outraEtapa);
        await db.SaveChangesAsync();
        db.CrmOpportunities.Add(new CrmOpportunity { LeadId = comOportunidade.Id, Titulo = "Op", ResponsavelId = vendedor.Id, EtapaId = aberta.Id, ValorEstimado = 2000m });
        await db.SaveChangesAsync();

        var currentUser = TestDbContextFactory.MockCurrentUser(vendedor.Id);
        var equipe = new EquipeComercialService(db, currentUser.Object);
        var service = new DashboardService(db, equipe, new ActivityService(db, currentUser.Object, equipe, new NoOpAuditSink()), currentUser.Object);

        var hoje = DateOnly.FromDateTime(DateTime.UtcNow);
        var resultado = await service.ObterAsync(new DashboardFilterRequest(new DateOnly(hoje.Year, hoje.Month, 1), hoje, null), CancellationToken.None);

        // 2.000 da oportunidade aberta + 300 da cotação sem proposta (a de 999 já conta pela oportunidade; a de outra etapa não conta).
        Assert.Equal(2300m, resultado.Indicadores.ValorPipeline);
        Assert.Equal(1, resultado.Indicadores.OportunidadesAbertas);
    }

    [Fact]
    public async Task ObterAsync_VendaComValorFinalZerado_ContaOTotalOuAAdesaoPaga()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var vendedor = await factory.CriarUsuarioAsync(db, "Vendedor1");
        var ganho = await factory.CriarEtapaAsync(db, "Ganho", 2, TipoEtapaPipeline.Ganho);
        var etapaLead = await factory.ObterOuCriarEtapaLeadAsync(db);

        var leads = Enumerable.Range(1, 3)
            .Select(i => new CrmLead { EtapaId = etapaLead.Id, NomeOuRazaoSocial = $"Cliente {i}", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = vendedor.Id })
            .ToList();
        db.CrmLeads.AddRange(leads);
        await db.SaveChangesAsync();

        var agora = DateTimeOffset.UtcNow;
        db.CrmOpportunities.AddRange(
            // Venda concluída pelo quadro: ValorFinal 0,00, adesão paga de 344,98 (Total = adesão - desconto).
            new CrmOpportunity { LeadId = leads[0].Id, Titulo = "Op1", ResponsavelId = vendedor.Id, EtapaId = ganho.Id, ValorFinal = 0m, PagamentoAdesao = 344.98m, Total = 344.98m, DataEfetivaFechamento = agora },
            // Sem Total: vale a adesão paga.
            new CrmOpportunity { LeadId = leads[1].Id, Titulo = "Op2", ResponsavelId = vendedor.Id, EtapaId = ganho.Id, ValorFinal = 0m, PagamentoAdesao = 100m, DataEfetivaFechamento = agora },
            // ValorFinal preenchido continua mandando.
            new CrmOpportunity { LeadId = leads[2].Id, Titulo = "Op3", ResponsavelId = vendedor.Id, EtapaId = ganho.Id, ValorFinal = 1000m, PagamentoAdesao = 200m, Total = 200m, DataEfetivaFechamento = agora });
        await db.SaveChangesAsync();

        var currentUser = TestDbContextFactory.MockCurrentUser(vendedor.Id);
        var equipe = new EquipeComercialService(db, currentUser.Object);
        var service = new DashboardService(db, equipe, new ActivityService(db, currentUser.Object, equipe, new NoOpAuditSink()), currentUser.Object);

        var hoje = DateOnly.FromDateTime(DateTime.UtcNow);
        var resultado = await service.ObterAsync(new DashboardFilterRequest(new DateOnly(hoje.Year, hoje.Month, 1), hoje, null), CancellationToken.None);

        Assert.Equal(3, resultado.Indicadores.VendasGanhasQuantidade);
        Assert.Equal(344.98m + 100m + 1000m, resultado.Indicadores.VendasGanhasValor);
        Assert.Equal(644.98m, resultado.Indicadores.VendasGanhasAdesaoValor);
    }

    [Fact]
    public async Task ObterAsync_UsaAMetaGeralDaRegionalDoUsuario_NoLugarDasIndividuais_NaMetaDoMes()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();

        var regional = await factory.CriarRegionalAsync(db, "Grande BH");
        var vendedor = await factory.CriarUsuarioAsync(db, "Vendedor1");
        vendedor.RegionalId = regional.Id;
        await db.SaveChangesAsync();

        var hoje = DateOnly.FromDateTime(DateTime.UtcNow);
        var mesReferencia = new DateOnly(hoje.Year, hoje.Month, 1);
        db.CrmRegionalGoals.Add(new CrmRegionalGoal { RegionalId = regional.Id, MesReferencia = mesReferencia, MetaQuantidadeVendas = 30, MetaValor = null });
        db.CrmSalesGoals.Add(new CrmSalesGoal { VendedorId = vendedor.Id, MesReferencia = mesReferencia, MetaQuantidadeVendas = 10, MetaValor = null });
        await db.SaveChangesAsync();

        var currentUser = TestDbContextFactory.MockCurrentUser(vendedor.Id);
        var equipe = new EquipeComercialService(db, currentUser.Object);
        var activityService = new ActivityService(db, currentUser.Object, equipe, new NoOpAuditSink());
        var service = new DashboardService(db, equipe, activityService, currentUser.Object);

        var resultado = await service.ObterAsync(new DashboardFilterRequest(new DateOnly(hoje.Year, hoje.Month, 1), hoje, null), CancellationToken.None);

        Assert.Equal(30, resultado.Meta.MetaQuantidade); // a meta da regional vale no lugar da individual (10): não soma 40
    }
}
