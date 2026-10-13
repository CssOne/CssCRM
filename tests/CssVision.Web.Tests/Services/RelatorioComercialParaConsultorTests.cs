using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Authorization;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>O relatório comercial é aberto a todos os consultores: cada um vê só os próprios resultados e não recebe a origem dos leads.</summary>
public class RelatorioComercialParaConsultorTests
{
    private static async Task<(CssVision.Web.Data.ApplicationDbContext Db, Guid Ana, Guid Bruno, Guid Admin)> PrepararAsync(TestDbContextFactory factory)
    {
        var db = factory.CreateContext();
        var admin = await factory.CriarUsuarioAsync(db, "Admin");
        var ana = await factory.CriarUsuarioAsync(db, "Ana");
        var bruno = await factory.CriarUsuarioAsync(db, "Bruno");
        await factory.AtribuirPapelAsync(db, ana, Roles.Comercial);
        await factory.AtribuirPapelAsync(db, bruno, Roles.Comercial);
        var ganho = await factory.CriarEtapaAsync(db, "Ganho", 9, TipoEtapaPipeline.Ganho);
        var dia = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddHours(15);
        foreach (var (dono, adesao) in new[] { (ana, 100m), (ana, 50m), (bruno, 400m) })
        {
            var lead = new CrmLead { NomeOuRazaoSocial = "C", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = dono.Id, Origem = "Lookalike", CriadoEm = dia };
            db.CrmLeads.Add(lead);
            await db.SaveChangesAsync();
            db.CrmOpportunities.Add(new CrmOpportunity { LeadId = lead.Id, Titulo = "V", ResponsavelId = dono.Id, EtapaId = ganho.Id, PagamentoAdesao = adesao, DataEfetivaFechamento = dia });
        }
        await db.SaveChangesAsync();
        return (db, ana.Id, bruno.Id, admin.Id);
    }

    private static readonly DateOnly De = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-2));
    private static readonly DateOnly Ate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));

    [Fact]
    public async Task Consultor_VeSoOsProprios_ENaoRecebeAOrigemDosLeads()
    {
        using var factory = new TestDbContextFactory();
        var (db, ana, _, _) = await PrepararAsync(factory);
        var usuario = TestDbContextFactory.MockCurrentUser(ana).Object; // consultor comum: sem visão total
        var rel = new RelatorioComercialService(db, usuario, new EquipeComercialService(db, usuario));

        var resultado = await rel.ObterAsync(De, Ate, null, null, new RelatorioFiltroExtra(), CancellationToken.None);

        Assert.Equal((2, 150m), (resultado.Totais.Vendas, resultado.Totais.Adesao));
        Assert.Empty(resultado.PorOrigem);
    }

    [Fact]
    public async Task Consultor_NaoVeOsResultadosDeOutroConsultor_MesmoPedindoPeloFiltro()
    {
        using var factory = new TestDbContextFactory();
        var (db, ana, bruno, _) = await PrepararAsync(factory);
        var usuario = TestDbContextFactory.MockCurrentUser(ana).Object;
        var rel = new RelatorioComercialService(db, usuario, new EquipeComercialService(db, usuario));

        var doBruno = await rel.ObterAsync(De, Ate, bruno, null, new RelatorioFiltroExtra(), CancellationToken.None);

        Assert.Equal(0, doBruno.Totais.Vendas);
        Assert.Equal(0m, doBruno.Totais.Adesao);
    }

    [Fact]
    public async Task Gestao_VeTodos_EAOrigemDosLeads()
    {
        using var factory = new TestDbContextFactory();
        var (db, _, _, admin) = await PrepararAsync(factory);
        var usuario = TestDbContextFactory.MockCurrentUser(admin, visaoTotal: true, podeGerir: true).Object;
        var rel = new RelatorioComercialService(db, usuario, new EquipeComercialService(db, usuario));

        var resultado = await rel.ObterAsync(De, Ate, null, null, new RelatorioFiltroExtra(), CancellationToken.None);

        Assert.Equal((3, 550m), (resultado.Totais.Vendas, resultado.Totais.Adesao));
        Assert.NotEmpty(resultado.PorOrigem);
    }
}
