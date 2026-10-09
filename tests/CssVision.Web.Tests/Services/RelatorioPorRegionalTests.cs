using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Authorization;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Relatório comercial em abas: "Todas" soma tudo e cada regional mostra só os consultores dela.</summary>
public class RelatorioPorRegionalTests
{
    [Fact]
    public async Task AbaDaRegional_SoTemOsConsultoresDela_ETodasSomaTudo()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var admin = await factory.CriarUsuarioAsync(db, "Admin");
        var mg132 = await factory.CriarRegionalAsync(db, "MG132");
        var mg134 = await factory.CriarRegionalAsync(db, "MG134");
        var ana = await factory.CriarUsuarioAsync(db, "Ana");
        var bruno = await factory.CriarUsuarioAsync(db, "Bruno");
        ana.RegionalId = mg132.Id; bruno.RegionalId = mg134.Id;
        await factory.AtribuirPapelAsync(db, ana, Roles.Comercial);
        await factory.AtribuirPapelAsync(db, bruno, Roles.Comercial);
        var ganho = await factory.CriarEtapaAsync(db, "Ganho", 9, TipoEtapaPipeline.Ganho);
        var dia = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddHours(15);
        foreach (var (dono, adesao) in new[] { (ana, 100m), (ana, 50m), (bruno, 400m) })
        {
            var lead = new CrmLead { NomeOuRazaoSocial = "C", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = dono.Id };
            db.CrmLeads.Add(lead);
            await db.SaveChangesAsync();
            db.CrmOpportunities.Add(new CrmOpportunity { LeadId = lead.Id, Titulo = "V", ResponsavelId = dono.Id, EtapaId = ganho.Id, PagamentoAdesao = adesao, DataEfetivaFechamento = dia });
        }
        await db.SaveChangesAsync();
        var usuario = TestDbContextFactory.MockCurrentUser(admin.Id, visaoTotal: true, podeGerir: true).Object;
        var rel = new RelatorioComercialService(db, usuario, new EquipeComercialService(db, usuario));
        var de = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-2)); var ate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));

        var todas = await rel.ObterAsync(de, ate, null, null, new RelatorioFiltroExtra(), CancellationToken.None);
        var soMg132 = await rel.ObterAsync(de, ate, null, null, new RelatorioFiltroExtra(RegionalId: mg132.Id), CancellationToken.None);
        var soMg134 = await rel.ObterAsync(de, ate, null, null, new RelatorioFiltroExtra(RegionalId: mg134.Id), CancellationToken.None);

        Assert.Equal((3, 550m), (todas.Totais.Vendas, todas.Totais.Adesao));
        Assert.Equal((2, 150m), (soMg132.Totais.Vendas, soMg132.Totais.Adesao));
        Assert.Equal((1, 400m), (soMg134.Totais.Vendas, soMg134.Totais.Adesao));
    }
}
