using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Authorization;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Relatório comercial: datas próprias de chegada e de venda, "Indicação?" e tipo de indicação.</summary>
public class RelatorioFiltrosExtrasTests
{
    private static readonly DateOnly De = new(2025, 1, 1);
    private static readonly DateOnly Ate = new(2025, 12, 31);

    private static async Task<RelatorioComercialService> MontarAsync(TestDbContextFactory factory)
    {
        var db = factory.CreateContext();
        var admin = await factory.CriarUsuarioAsync(db, "Admin");
        var ana = await factory.CriarUsuarioAsync(db, "Ana");
        await factory.AtribuirPapelAsync(db, ana, Roles.Comercial);
        var ganho = await factory.CriarEtapaAsync(db, "Ganho", 9, TipoEtapaPipeline.Ganho);

        CrmLead Lead(string nome, string? tipo, int mesChegada) => new()
        {
            NomeOuRazaoSocial = nome, TipoPessoa = TipoPessoa.Fisica, ResponsavelId = ana.Id, TipoIndicacao = tipo,
        };
        // Lead A (tipo "Lead", chegou em março) e lead B (indicação "Pessoal", chegou em maio); os dois viram venda em junho.
        var a = Lead("A", "Lead", 3);
        var b = Lead("B", "Pessoal", 5);
        db.CrmLeads.AddRange(a, b);
        await db.SaveChangesAsync();
        a.CriadoEm = new DateTimeOffset(2025, 3, 10, 15, 0, 0, TimeSpan.Zero);
        b.CriadoEm = new DateTimeOffset(2025, 5, 10, 15, 0, 0, TimeSpan.Zero);
        db.CrmOpportunities.Add(new CrmOpportunity
        {
            LeadId = a.Id, Titulo = "A", ResponsavelId = ana.Id, EtapaId = ganho.Id, PagamentoAdesao = 100m,
            DataEfetivaFechamento = new DateTimeOffset(2025, 6, 5, 15, 0, 0, TimeSpan.Zero),
        });
        db.CrmOpportunities.Add(new CrmOpportunity
        {
            LeadId = b.Id, Titulo = "B", ResponsavelId = ana.Id, EtapaId = ganho.Id, PagamentoAdesao = 200m, Indicacao = true, TipoIndicacao = "Pessoal",
            DataEfetivaFechamento = new DateTimeOffset(2025, 7, 5, 15, 0, 0, TimeSpan.Zero),
        });
        await db.SaveChangesAsync();

        var usuario = TestDbContextFactory.MockCurrentUser(admin.Id, visaoTotal: true, podeGerir: true).Object;
        return new RelatorioComercialService(db, usuario, new EquipeComercialService(db, usuario));
    }

    [Fact]
    public async Task IndicacaoSimENao_SeparamLeadsEVendas()
    {
        using var factory = new TestDbContextFactory();
        var rel = await MontarAsync(factory);

        var sim = await rel.ObterAsync(De, Ate, null, null, new RelatorioFiltroExtra(Indicacao: true), CancellationToken.None);
        Assert.Equal(1, sim.Totais.Leads);
        Assert.Equal(1, sim.Totais.Vendas);
        Assert.Equal(200m, sim.Totais.Adesao);

        var nao = await rel.ObterAsync(De, Ate, null, null, new RelatorioFiltroExtra(Indicacao: false), CancellationToken.None);
        Assert.Equal(1, nao.Totais.Leads);
        Assert.Equal(1, nao.Totais.Vendas);
        Assert.Equal(100m, nao.Totais.Adesao);

        var todos = await rel.ObterAsync(De, Ate, null, null, new RelatorioFiltroExtra(), CancellationToken.None);
        Assert.Equal(2, todos.Totais.Leads);
        Assert.Equal(2, todos.Totais.Vendas);
    }

    [Fact]
    public async Task TipoDeIndicacao_IgnoraMaiusculas_EFiltraLeadsEVendas()
    {
        using var factory = new TestDbContextFactory();
        var rel = await MontarAsync(factory);

        var r = await rel.ObterAsync(De, Ate, null, null, new RelatorioFiltroExtra(TiposIndicacao: ["PESSOAL"]), CancellationToken.None);

        Assert.Equal(1, r.Totais.Leads);
        Assert.Equal(1, r.Totais.Vendas);
        Assert.Equal(200m, r.Totais.Adesao);
    }

    [Fact]
    public async Task DataDeChegada_FiltraLeads_EAsVendasDeles()
    {
        using var factory = new TestDbContextFactory();
        var rel = await MontarAsync(factory);

        // Só quem chegou em março: lead A e a venda dele (que foi em junho, dentro do período principal).
        var r = await rel.ObterAsync(De, Ate, null, null,
            new RelatorioFiltroExtra(ChegadaInicio: new DateOnly(2025, 3, 1), ChegadaFim: new DateOnly(2025, 3, 31)), CancellationToken.None);

        Assert.Equal(1, r.Totais.Leads);
        Assert.Equal(1, r.Totais.Vendas);
        Assert.Equal(100m, r.Totais.Adesao);
    }

    [Fact]
    public async Task DataDaVenda_ValeSoParaVendas_ELeadsSeguemOPeriodoPrincipal()
    {
        using var factory = new TestDbContextFactory();
        var rel = await MontarAsync(factory);

        var r = await rel.ObterAsync(De, Ate, null, null,
            new RelatorioFiltroExtra(VendaInicio: new DateOnly(2025, 7, 1), VendaFim: new DateOnly(2025, 7, 31)), CancellationToken.None);

        Assert.Equal(2, r.Totais.Leads); // leads: período principal
        Assert.Equal(1, r.Totais.Vendas); // só a de julho
        Assert.Equal(200m, r.Totais.Adesao);
    }

    [Fact]
    public async Task LeadDeTrafegoPassadoParaOutro_ContaParaQuemRecebeuNoMesDaAtribuicao_IndicacaoSegueAChegada()
    {
        using var factory = new TestDbContextFactory();
        var db = factory.CreateContext();
        var admin = await factory.CriarUsuarioAsync(db, "Admin");
        var ana = await factory.CriarUsuarioAsync(db, "Ana");
        var bruna = await factory.CriarUsuarioAsync(db, "Bruna");
        await factory.AtribuirPapelAsync(db, ana, Roles.Comercial);
        await factory.AtribuirPapelAsync(db, bruna, Roles.Comercial);

        // Dois leads de março com a Ana: um de tráfego (Meta) e uma indicação. Em maio, os dois passam para a Bruna.
        var trafego = new CrmLead { NomeOuRazaoSocial = "Trafego", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = ana.Id, MetaLeadId = "1" };
        var indicacao = new CrmLead { NomeOuRazaoSocial = "Indicacao", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = ana.Id, TipoIndicacao = "Pessoal", CriadoManualmente = true };
        db.CrmLeads.AddRange(trafego, indicacao);
        await db.SaveChangesAsync();
        var marco = new DateTimeOffset(2025, 3, 10, 15, 0, 0, TimeSpan.Zero);
        trafego.CriadoEm = marco; trafego.ResponsavelAtribuidoEm = marco;
        indicacao.CriadoEm = marco; indicacao.ResponsavelAtribuidoEm = marco;
        await db.SaveChangesAsync();
        var maio = new DateTimeOffset(2025, 5, 10, 15, 0, 0, TimeSpan.Zero);
        trafego.ResponsavelId = bruna.Id; indicacao.ResponsavelId = bruna.Id;
        await db.SaveChangesAsync();
        // O contexto carimba a atribuição com a data de hoje; aqui ela volta para maio de 2025.
        trafego.ResponsavelAtribuidoEm = maio; indicacao.ResponsavelAtribuidoEm = maio;
        await db.SaveChangesAsync();

        var usuario = TestDbContextFactory.MockCurrentUser(admin.Id, visaoTotal: true, podeGerir: true).Object;
        var rel = new RelatorioComercialService(db, usuario, new EquipeComercialService(db, usuario));
        int LeadsDe(RelatorioComercialDto r, Guid id) => r.PorVendedor.SingleOrDefault(v => v.VendedorId == id)?.Leads ?? 0;

        var marcoR = await rel.ObterAsync(new DateOnly(2025, 3, 1), new DateOnly(2025, 3, 31), null, null, new RelatorioFiltroExtra(), CancellationToken.None);
        Assert.Equal(0, LeadsDe(marcoR, ana.Id));    // o lead de tráfego saiu de março: foi atribuído à Bruna só em maio
        Assert.Equal(1, LeadsDe(marcoR, bruna.Id));  // a indicação continua em março (chegada), agora com a Bruna
        Assert.Equal(1, marcoR.Totais.Leads);

        var maioR = await rel.ObterAsync(new DateOnly(2025, 5, 1), new DateOnly(2025, 5, 31), null, null, new RelatorioFiltroExtra(), CancellationToken.None);
        Assert.Equal(1, LeadsDe(maioR, bruna.Id));   // o de tráfego entra em maio, na conta de quem recebeu
        Assert.Equal(0, LeadsDe(maioR, ana.Id));
        Assert.Equal(1, maioR.Totais.Leads);
    }
}
