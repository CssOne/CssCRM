using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Authorization;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Relatório comercial: venda pela data de ativação e "Indicação?" igual à regra do quadro de leads.</summary>
public class RelatorioFiltrosCorrigidosTests
{
    [Fact]
    public async Task Venda_ContaNoDiaDaAtivacao_ESemAtivacaoNoDiaDaVenda()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var admin = await factory.CriarUsuarioAsync(db, "Admin");
        var ana = await factory.CriarUsuarioAsync(db, "Ana");
        await factory.AtribuirPapelAsync(db, ana, Roles.Comercial);
        var ganho = await factory.CriarEtapaAsync(db, "Ganho", 9, TipoEtapaPipeline.Ganho);
        var lead = new CrmLead { NomeOuRazaoSocial = "C", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = ana.Id };
        db.CrmLeads.Add(lead);
        await db.SaveChangesAsync();
        CrmOpportunity Op(DateTimeOffset venda, DateTimeOffset? ativo) => new()
        {
            LeadId = lead.Id, Titulo = "V", ResponsavelId = ana.Id, EtapaId = ganho.Id, PagamentoAdesao = 100m, DataEfetivaFechamento = venda, AtivoEm = ativo,
        };
        db.CrmOpportunities.AddRange(
            Op(new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero)), // ativada em 1º/10
            Op(new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero), null));                                                    // sem ativação
        await db.SaveChangesAsync();
        var usuario = TestDbContextFactory.MockCurrentUser(admin.Id, visaoTotal: true, podeGerir: true).Object;
        var rel = new RelatorioComercialService(db, usuario, new EquipeComercialService(db, usuario));

        var setembro = await rel.ObterAsync(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), null, null, new RelatorioFiltroExtra(), CancellationToken.None);
        var outubro = await rel.ObterAsync(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), null, null, new RelatorioFiltroExtra(), CancellationToken.None);
        var dia1 = await rel.ObterAsync(new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 31), null, null, new RelatorioFiltroExtra(VendaInicio: new DateOnly(2026, 10, 1), VendaFim: new DateOnly(2026, 10, 1)), CancellationToken.None);

        Assert.Equal(0, setembro.Totais.Vendas);
        Assert.Equal(2, outubro.Totais.Vendas);
        Assert.Equal(1, dia1.Totais.Vendas); // só a ativada em 01/10
    }

    [Fact]
    public async Task IndicacaoSimENao_DoLead_SegueARegraDoQuadro_CadastroManualSemTipoEIndicacao()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var admin = await factory.CriarUsuarioAsync(db, "Admin");
        var ana = await factory.CriarUsuarioAsync(db, "Ana");
        await factory.AtribuirPapelAsync(db, ana, Roles.Comercial);
        CrmLead L(string nome, string? tipo, bool manual) => new() { NomeOuRazaoSocial = nome, TipoPessoa = TipoPessoa.Fisica, ResponsavelId = ana.Id, TipoIndicacao = tipo, CriadoManualmente = manual };
        db.CrmLeads.AddRange(L("trafego", "Lead", false), L("semTipo", null, false), L("manualSemTipo", null, true), L("pessoal", "Pessoal", false), L("manualLead", "Lead", true));
        await db.SaveChangesAsync();
        var usuario = TestDbContextFactory.MockCurrentUser(admin.Id, visaoTotal: true, podeGerir: true).Object;
        var rel = new RelatorioComercialService(db, usuario, new EquipeComercialService(db, usuario));
        var de = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-2)); var ate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));

        var sim = await rel.ObterAsync(de, ate, null, null, new RelatorioFiltroExtra(Indicacao: true), CancellationToken.None);
        var nao = await rel.ObterAsync(de, ate, null, null, new RelatorioFiltroExtra(Indicacao: false), CancellationToken.None);
        var todos = await rel.ObterAsync(de, ate, null, null, new RelatorioFiltroExtra(), CancellationToken.None);

        Assert.Equal(2, sim.Totais.Leads);          // manualSemTipo e pessoal
        Assert.Equal(3, nao.Totais.Leads);          // trafego, semTipo e manualLead (tipo "Lead" nunca é indicação)
        Assert.Equal(todos.Totais.Leads, sim.Totais.Leads + nao.Totais.Leads);
    }
}
