using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Administrador com "ver as outras regionais no Relatório comercial" desligado vê só a regional do próprio cadastro.</summary>
public class RelatorioSoDaMinhaRegionalTests
{
    [Fact]
    public async Task OpcaoDesligada_LimitaORelatorioAMinhaRegional_ELigadaMostraTodas()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var admin = await factory.CriarUsuarioAsync(db, "Admin");
        var mg132 = await factory.CriarRegionalAsync(db, "MG132");
        var mg134 = await factory.CriarRegionalAsync(db, "MG134");
        var ana = await factory.CriarUsuarioAsync(db, "Ana");
        var bruno = await factory.CriarUsuarioAsync(db, "Bruno");
        admin.RegionalId = mg132.Id; ana.RegionalId = mg132.Id; bruno.RegionalId = mg134.Id;
        var ganho = await factory.CriarEtapaAsync(db, "Ganho", 9, TipoEtapaPipeline.Ganho);
        foreach (var dono in new[] { ana.Id, bruno.Id })
        {
            var lead = new CrmLead { NomeOuRazaoSocial = "C", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = dono };
            db.CrmLeads.Add(lead);
            await db.SaveChangesAsync();
            db.CrmOpportunities.Add(new CrmOpportunity { LeadId = lead.Id, Titulo = "V", ResponsavelId = dono, EtapaId = ganho.Id, PagamentoAdesao = 100m, DataEfetivaFechamento = DateTimeOffset.UtcNow });
        }
        await db.SaveChangesAsync();

        var usuario = TestDbContextFactory.MockCurrentUser(admin.Id, visaoTotal: true, podeGerir: true);
        var hoje = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-3));
        async Task<(int Vendas, Guid? Fixa)> Consultar()
        {
            var r = await new RelatorioComercialService(db, usuario.Object, new EquipeComercialService(db, usuario.Object))
                .ObterAsync(hoje.AddDays(-1), hoje, null, null, CancellationToken.None);
            return (r.Totais.Vendas, r.RegionalFixaId);
        }

        Assert.Equal((2, null), await Consultar());

        admin.VerOutrasRegionaisNoRelatorio = false;
        await db.SaveChangesAsync();
        Assert.Equal((1, mg132.Id), await Consultar());
    }
}
