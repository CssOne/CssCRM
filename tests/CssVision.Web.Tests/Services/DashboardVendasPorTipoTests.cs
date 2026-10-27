using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>A barra de meta da Visão geral separa as vendas do mês por tipo (Lead, Indicação, Indicação Lead, Pessoal...) e cada tipo abre a lista das vendas dele.</summary>
public class DashboardVendasPorTipoTests
{
    private static async Task<(DashboardService Servico, Domain.Identity.ApplicationUser Ana)> MontarAsync(TestDbContextFactory factory)
    {
        var db = factory.CreateContext();
        var ana = await factory.CriarUsuarioAsync(db, "Ana");
        var ganho = await factory.CriarEtapaAsync(db, "Ganho", 9, TipoEtapaPipeline.Ganho);
        db.CrmSalesGoals.Add(new CrmSalesGoal { VendedorId = ana.Id, MesReferencia = HorarioBrasilia.PrimeiroDiaDoMes(HorarioBrasilia.Hoje), MetaQuantidadeVendas = 10 });
        await db.SaveChangesAsync();

        async Task Venda(string nome, string? tipoDoLead, string? tipoDaVenda)
        {
            var lead = new CrmLead { NomeOuRazaoSocial = nome, TipoPessoa = TipoPessoa.Fisica, ResponsavelId = ana.Id, TipoIndicacao = tipoDoLead, CriadoManualmente = false };
            db.CrmLeads.Add(lead);
            await db.SaveChangesAsync();
            db.CrmOpportunities.Add(new CrmOpportunity
            {
                LeadId = lead.Id, Titulo = "V", ResponsavelId = ana.Id, EtapaId = ganho.Id, PagamentoAdesao = 100m,
                DataEfetivaFechamento = DateTimeOffset.UtcNow, TipoIndicacao = tipoDaVenda,
            });
            await db.SaveChangesAsync();
        }

        await Venda("Do lead 1", "Lead", null);
        await Venda("Do lead 2", "lead", null);                  // maiúsculas/minúsculas não separam
        await Venda("Indicou 1", "Indicação", null);
        await Venda("Fechou como indicação", "Lead", "Indicação Lead"); // o tipo da venda vale mais que o do lead
        await Venda("Pessoal 1", null, "Pessoal");
        await Venda("Sem canal gravado", null, null);        // card do tráfego/Notion sem canal: é Lead, como no quadro
        await Venda("Ação externa", "Ação Externa", null);   // qualquer canal gravado aparece com o nome dele

        var currentUser = TestDbContextFactory.MockCurrentUser(ana.Id);
        var equipe = new EquipeComercialService(db, currentUser.Object);
        var atividades = new ActivityService(db, currentUser.Object, equipe, new NoOpAuditSink());
        return (new DashboardService(db, equipe, atividades, currentUser.Object), ana);
    }

    [Fact]
    public async Task Meta_SeparaAsVendasDoMesPorTipo()
    {
        using var factory = new TestDbContextFactory();
        var (servico, _) = await MontarAsync(factory);
        var hoje = HorarioBrasilia.Hoje;

        var painel = await servico.ObterAsync(new DashboardFilterRequest(HorarioBrasilia.PrimeiroDiaDoMes(hoje), hoje, null, null), CancellationToken.None);

        Assert.Equal(7, painel.Meta.RealizadoQuantidade);
        var porTipo = painel.Meta.PorTipo!.ToDictionary(t => t.Tipo, t => t.Quantidade, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(3, porTipo["Lead"]);                         // 2 com canal Lead + 1 sem canal (Venda concluída Leads)
        Assert.Equal(1, porTipo["Indicação"]);
        Assert.Equal(1, porTipo["Indicação Lead"]);
        Assert.Equal(1, porTipo["Pessoal"]);
        Assert.Equal(1, porTipo["Ação Externa"]);
        Assert.False(porTipo.ContainsKey("Sem tipo"));
        Assert.Equal(painel.Meta.RealizadoQuantidade, painel.Meta.PorTipo!.Sum(t => t.Quantidade)); // as cores fecham a barra
    }

    [Fact]
    public async Task Detalhe_ListaTodasOuSoAsDoTipo()
    {
        using var factory = new TestDbContextFactory();
        var (servico, _) = await MontarAsync(factory);
        var filtro = new DashboardFilterRequest(null, null, null, null);

        var todas = await servico.ObterVendasDoMesAsync(filtro, null, CancellationToken.None);
        Assert.Equal(7, todas.Total);

        var leads = await servico.ObterVendasDoMesAsync(filtro, "Lead", CancellationToken.None);
        Assert.Equal(3, leads.Total);
        Assert.All(leads.Itens, i => Assert.Equal("Lead", i.Canal, ignoreCase: true));

        var indicacaoLead = await servico.ObterVendasDoMesAsync(filtro, "Indicação Lead", CancellationToken.None);
        var unica = Assert.Single(indicacaoLead.Itens);
        Assert.Equal("Fechou como indicação", unica.Cliente);
    }
}
