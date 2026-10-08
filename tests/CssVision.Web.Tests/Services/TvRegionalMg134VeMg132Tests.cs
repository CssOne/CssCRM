using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>A TV de quem é da regional MG134 mostra também a MG132; a de outras regionais continua só com a própria.</summary>
public class TvRegionalMg134VeMg132Tests
{
    [Fact]
    public async Task TvDoGestorDaMg134_IncluiAMg132_ETvDoGestorDaMg132NaoIncluiAMg134()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var mg132 = await factory.CriarRegionalAsync(db, "MG132");
        var mg134 = await factory.CriarRegionalAsync(db, "MG134");
        var g134 = await factory.CriarUsuarioAsync(db, "Gestor134");
        var g132 = await factory.CriarUsuarioAsync(db, "Gestor132");
        var ana = await factory.CriarUsuarioAsync(db, "Ana132");
        var bruno = await factory.CriarUsuarioAsync(db, "Bruno134");
        g134.RegionalId = mg134.Id; bruno.RegionalId = mg134.Id; g132.RegionalId = mg132.Id; ana.RegionalId = mg132.Id;
        var ganho = await factory.CriarEtapaAsync(db, "Ganho", 9, TipoEtapaPipeline.Ganho);
        foreach (var dono in new[] { ana, bruno })
        {
            var lead = new CrmLead { NomeOuRazaoSocial = "C", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = dono.Id };
            db.CrmLeads.Add(lead);
            await db.SaveChangesAsync();
            db.CrmOpportunities.Add(new CrmOpportunity { LeadId = lead.Id, Titulo = "V", ResponsavelId = dono.Id, EtapaId = ganho.Id, PagamentoAdesao = 100m, DataEfetivaFechamento = DateTimeOffset.UtcNow });
        }
        await db.SaveChangesAsync();

        TvComercialService Tv(Guid id)
        {
            var u = TestDbContextFactory.MockCurrentUser(id, gestorComercial: true, podeGerir: true).Object;
            return new TvComercialService(db, new EquipeComercialService(db, u), u);
        }

        var tv134 = await Tv(g134.Id).ObterAsync(null, null, CancellationToken.None);
        var tv132 = await Tv(g132.Id).ObterAsync(null, null, CancellationToken.None);

        Assert.Equal(["Ana132", "Bruno134"], tv134.RankingConsultores.Select(c => c.Nome).Order().ToArray());
        Assert.Equal(["Ana132"], tv132.RankingConsultores.Select(c => c.Nome).ToArray());
    }
}
