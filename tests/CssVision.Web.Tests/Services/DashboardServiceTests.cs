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

        var lead1 = new CrmLead { NomeOuRazaoSocial = "Cliente 1", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = vendedor.Id };
        var lead2 = new CrmLead { NomeOuRazaoSocial = "Cliente 2", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = vendedor.Id };
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
        var service = new DashboardService(db, equipe, activityService);

        var hoje = DateOnly.FromDateTime(DateTime.UtcNow);
        var resultado = await service.ObterAsync(new DashboardFilterRequest(new DateOnly(hoje.Year, hoje.Month, 1), hoje, null), CancellationToken.None);

        Assert.Equal(1, resultado.Indicadores.OportunidadesAbertas);
        Assert.Equal(2000m, resultado.Indicadores.ValorPipeline);
        Assert.Equal(1, resultado.Indicadores.VendasGanhasQuantidade);
        Assert.Equal(1000m, resultado.Indicadores.VendasGanhasValor);
        Assert.Equal(1000m, resultado.Indicadores.TicketMedio);
        Assert.Equal(50m, resultado.Indicadores.TaxaConversao); // 1 ganha / (1 ganha + 1 perdida)
    }
}
