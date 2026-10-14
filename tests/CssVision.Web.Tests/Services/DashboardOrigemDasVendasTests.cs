using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Visão geral: o gráfico é "Origem das vendas" — vendas do período de tráfego pago (CRM e Notion) e de indicação.</summary>
public class DashboardOrigemDasVendasTests
{
    [Fact]
    public async Task ContaAsVendasDoPeriodoPorOrigem_TrafegoPagoIndicacaoESemOrigem()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var ana = await factory.CriarUsuarioAsync(db, "Ana");
        var ganho = await factory.CriarEtapaAsync(db, "Ganho", 9, TipoEtapaPipeline.Ganho);
        var notion = OrigemLead.MarcadorSincronizacaoNotion;

        CrmLead L(string nome, bool manual, string? tipo = null, string? meta = null, string? origem = null, string? campanha = null) => new()
        {
            NomeOuRazaoSocial = nome, TipoPessoa = TipoPessoa.Fisica, ResponsavelId = ana.Id,
            CriadoManualmente = manual, TipoIndicacao = tipo, MetaLeadId = meta, ConsentimentoOrigem = origem, Campanha = campanha,
        };
        var trafegoCrm = L("trafego-crm", false, "Lead", meta: "m1");
        var trafegoNotion = L("trafego-notion", false, "Lead", origem: notion, campanha: "UGC");
        var indicacao = L("indicacao", true, "Pessoal");
        var anuncioQueFechouIndicacao = L("indicacao-lead", false, "Indicação Lead", meta: "m2");
        var semOrigem = L("sem-origem", false, "Lead");
        db.CrmLeads.AddRange(trafegoCrm, trafegoNotion, indicacao, anuncioQueFechouIndicacao, semOrigem);
        await db.SaveChangesAsync();
        foreach (var lead in new[] { trafegoCrm, trafegoNotion, indicacao, anuncioQueFechouIndicacao, semOrigem })
        {
            db.CrmOpportunities.Add(new CrmOpportunity
            {
                LeadId = lead.Id, Titulo = lead.NomeOuRazaoSocial, ResponsavelId = ana.Id, EtapaId = ganho.Id, DataEfetivaFechamento = DateTimeOffset.UtcNow,
            });
        }
        await db.SaveChangesAsync();

        var currentUser = TestDbContextFactory.MockCurrentUser(ana.Id, visaoTotal: true);
        var equipe = new EquipeComercialService(db, currentUser.Object);
        var service = new DashboardService(db, equipe, new ActivityService(db, currentUser.Object, equipe, new NoOpAuditSink()), currentUser.Object);
        var hoje = HorarioBrasilia.Hoje;

        var painel = await service.ObterAsync(new DashboardFilterRequest(HorarioBrasilia.PrimeiroDiaDoMes(hoje), hoje, null), CancellationToken.None);

        var porOrigem = painel.OrigemVendas.ToDictionary(o => o.Origem, o => o.Quantidade);
        Assert.Equal(2, porOrigem["Tráfego pago"]);
        Assert.Equal(2, porOrigem["Indicação"]);       // a de indicação e o anúncio que fechou como "Indicação Lead"
        Assert.Equal(1, porOrigem["Sem origem identificada"]);
        Assert.Equal(painel.Indicadores.VendasGanhasQuantidade, porOrigem.Values.Sum()); // fecha com o card de vendas
    }
}
