using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Painel da TV: rankings de vendas/adesão/conversão, regionais, evolução e últimas vendas, direto do CRM.</summary>
public class TvComercialServiceTests
{
    private static TvComercialService Servico(ApplicationDbContext db, Guid usuarioId, bool visaoTotal = true)
    {
        var usuario = TestDbContextFactory.MockCurrentUser(usuarioId, visaoTotal: visaoTotal, gestorComercial: !visaoTotal, podeGerir: true).Object;
        return new TvComercialService(db, new EquipeComercialService(db, usuario));
    }

    [Fact]
    public async Task Painel_RankeiaPorVendasEAdesao_CalculaConversaoRegionaisEEvolucao()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var admin = await factory.CriarUsuarioAsync(db, "Admin");
        var ana = await factory.CriarUsuarioAsync(db, "Ana");
        var bruna = await factory.CriarUsuarioAsync(db, "Bruna");
        var regional = new CrmRegional { Nome = "MG132" };
        db.CrmRegionais.Add(regional);
        await db.SaveChangesAsync();
        ana.RegionalId = regional.Id;
        bruna.RegionalId = regional.Id;
        var ganho = await factory.CriarEtapaAsync(db, "Ganho", 9, TipoEtapaPipeline.Ganho);

        // Ana: 2 vendas, adesão 100; Bruna: 1 venda, adesão 400 (ganha no ranking de adesão, perde no de vendas).
        // Ana recebeu 4 leads no mês (conversão 50%), Bruna 1 (conversão 100%).
        CrmLead L(string nome, Guid resp) => new() { NomeOuRazaoSocial = nome, TipoPessoa = TipoPessoa.Fisica, ResponsavelId = resp };
        var leads = new[] { L("A1", ana.Id), L("A2", ana.Id), L("A3", ana.Id), L("A4", ana.Id), L("B1", bruna.Id) };
        db.CrmLeads.AddRange(leads);
        await db.SaveChangesAsync();
        CrmOpportunity V(CrmLead lead, Guid resp, decimal adesao) => new()
        {
            LeadId = lead.Id, Titulo = "V", ResponsavelId = resp, EtapaId = ganho.Id, PagamentoAdesao = adesao, ValorFinal = adesao * 3,
            DataEfetivaFechamento = DateTimeOffset.UtcNow,
        };
        db.CrmOpportunities.AddRange(V(leads[0], ana.Id, 60), V(leads[1], ana.Id, 40), V(leads[4], bruna.Id, 400));
        db.CrmRegionalGoals.Add(new CrmRegionalGoal { RegionalId = regional.Id, MesReferencia = HorarioBrasilia.PrimeiroDiaDoMes(HorarioBrasilia.Hoje), MetaQuantidadeVendas = 6 });
        await db.SaveChangesAsync();

        var r = await Servico(db, admin.Id).ObterAsync(null, null, CancellationToken.None);

        Assert.Equal(["Ana", "Bruna"], r.RankingConsultores.Select(c => c.Nome));
        Assert.Equal(["Bruna", "Ana"], r.RankingValorAdesao.Select(c => c.Nome));
        Assert.Equal(100m, r.RankingConsultores[0].ValorVendido);
        Assert.Equal(["Bruna", "Ana"], r.RankingConversao.Select(c => c.Nome));
        Assert.Equal(100m, r.RankingConversao[0].TaxaConversao);
        Assert.Equal(50m, r.RankingConversao[1].TaxaConversao);

        Assert.Equal(3, r.Resumo.VendasNoMes);
        Assert.Equal(3, r.Resumo.VendasHoje);
        Assert.Equal(500m, r.Resumo.ValorNoMes);
        Assert.Equal(50m, r.Resumo.PercentualMetaGeral); // 3 vendas ÷ meta de 6

        var mg132 = Assert.Single(r.RankingRegionais);
        Assert.Equal("MG132", mg132.Nome);
        Assert.Equal(3, mg132.QuantidadeVendas);
        Assert.Equal(50m, mg132.PercentualMeta);

        Assert.Equal(HorarioBrasilia.Hoje.Day, r.EvolucaoMensal.Count);
        Assert.Equal(3, r.EvolucaoMensal.Last().QuantidadeAcumulada);
        Assert.Equal(3, r.UltimasVendas.Count);
    }

    [Fact]
    public async Task Painel_NaoContaVendaComDataFutura_NemDeOutroMes()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var admin = await factory.CriarUsuarioAsync(db, "Admin");
        var ana = await factory.CriarUsuarioAsync(db, "Ana");
        var ganho = await factory.CriarEtapaAsync(db, "Ganho", 9, TipoEtapaPipeline.Ganho);
        var lead = new CrmLead { NomeOuRazaoSocial = "Cliente", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = ana.Id };
        db.CrmLeads.Add(lead);
        await db.SaveChangesAsync();
        CrmOpportunity V(DateTimeOffset data, decimal adesao) => new()
        {
            LeadId = lead.Id, Titulo = "V", ResponsavelId = ana.Id, EtapaId = ganho.Id, PagamentoAdesao = adesao, DataEfetivaFechamento = data,
        };
        var hoje = HorarioBrasilia.Hoje;
        db.CrmOpportunities.AddRange(
            V(HorarioBrasilia.Inicio(hoje).AddHours(12), 100),                                   // hoje: conta
            V(HorarioBrasilia.Inicio(hoje.AddDays(1)).AddHours(12), 999),                         // amanhã: venda futura, não conta
            V(HorarioBrasilia.Inicio(HorarioBrasilia.PrimeiroDiaDoMes(hoje)).AddDays(-2), 888));  // mês passado: não conta
        await db.SaveChangesAsync();

        var r = await Servico(db, admin.Id).ObterAsync(null, null, CancellationToken.None);

        Assert.Equal(1, r.Resumo.VendasNoMes);
        Assert.Equal(100m, r.Resumo.ValorNoMes);
        Assert.Equal(1, r.RankingConsultores.Single().QuantidadeVendas);
        Assert.Equal(r.Resumo.VendasNoMes, r.EvolucaoMensal.Last().QuantidadeAcumulada); // total e evolução diária batem
    }

    [Fact]
    public async Task Painel_GestorRegionalSoVeAPropriaRegional()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var gestor = await factory.CriarUsuarioAsync(db, "Gestor");
        var ana = await factory.CriarUsuarioAsync(db, "Ana");
        var bruna = await factory.CriarUsuarioAsync(db, "Bruna");
        var r132 = new CrmRegional { Nome = "MG132" };
        var r134 = new CrmRegional { Nome = "MG134" };
        db.CrmRegionais.AddRange(r132, r134);
        await db.SaveChangesAsync();
        gestor.RegionalId = r132.Id;
        ana.RegionalId = r132.Id;
        bruna.RegionalId = r134.Id;
        var ganho = await factory.CriarEtapaAsync(db, "Ganho", 9, TipoEtapaPipeline.Ganho);
        var la = new CrmLead { NomeOuRazaoSocial = "A", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = ana.Id };
        var lb = new CrmLead { NomeOuRazaoSocial = "B", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = bruna.Id };
        db.CrmLeads.AddRange(la, lb);
        await db.SaveChangesAsync();
        db.CrmOpportunities.AddRange(
            new CrmOpportunity { LeadId = la.Id, Titulo = "V", ResponsavelId = ana.Id, EtapaId = ganho.Id, PagamentoAdesao = 10, DataEfetivaFechamento = DateTimeOffset.UtcNow },
            new CrmOpportunity { LeadId = lb.Id, Titulo = "V", ResponsavelId = bruna.Id, EtapaId = ganho.Id, PagamentoAdesao = 99, DataEfetivaFechamento = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var r = await Servico(db, gestor.Id, visaoTotal: false).ObterAsync(null, null, CancellationToken.None);

        Assert.Equal(["Ana"], r.RankingConsultores.Select(c => c.Nome));
        Assert.Equal(1, r.Resumo.VendasNoMes);
        Assert.Equal(10m, r.Resumo.ValorNoMes);
    }
}
