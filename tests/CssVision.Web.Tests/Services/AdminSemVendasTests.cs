using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Authorization;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Administrador que só administra (não atua nas vendas) some da Gestão comercial e dos rankings; o ranking do Portal é o da TV.</summary>
public class AdminSemVendasTests
{
    [Fact]
    public async Task OpcaoSoValeParaAdministrador_ESomeDaGestaoDoRankingEDaTv_EPortalEspelhaATv()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        await factory.SeedRolesAsync(db);
        using var userManager = TestDbContextFactory.CreateUserManager(db);
        var root = await factory.CriarUsuarioAsync(db, "Root");
        var mg132 = await factory.CriarRegionalAsync(db, "MG132");
        var mg134 = await factory.CriarRegionalAsync(db, "MG134");
        var ana = await factory.CriarUsuarioAsync(db, "Ana");
        var bruno = await factory.CriarUsuarioAsync(db, "Bruno");
        ana.RegionalId = mg132.Id; bruno.RegionalId = mg134.Id;
        await factory.AtribuirPapelAsync(db, ana, Roles.Comercial);
        await factory.AtribuirPapelAsync(db, bruno, Roles.Comercial);

        // Cadastro: administrador pode "só administrar"; consultor nunca.
        var usuarioAdmin = TestDbContextFactory.MockCurrentUser(root.Id, visaoTotal: true, podeGerir: true);
        var cadastro = new UserManagementService(db, userManager, usuarioAdmin.Object, new NoOpAuditSink());
        var admin = await cadastro.CriarAsync(new UserCreateRequest("Admin Sem Vendas", "adm@teste.com", "Senha@123", null, Roles.Admin, null, null, null, null, AtuaNasVendas: false), CancellationToken.None);
        Assert.False(admin.AtuaNasVendas);
        var consultor = await cadastro.CriarAsync(new UserCreateRequest("Consultor Novo", "cons@teste.com", "Senha@123", null, Roles.Comercial, mg132.Id, null, null, null, AtuaNasVendas: false), CancellationToken.None);
        Assert.True(consultor.AtuaNasVendas);

        // Vendas: o administrador (oculto) e os consultores.
        var ganho = await factory.CriarEtapaAsync(db, "Ganho", 9, TipoEtapaPipeline.Ganho);
        foreach (var dono in new[] { ana.Id, bruno.Id, admin.Id })
        {
            var lead = new CrmLead { NomeOuRazaoSocial = "C", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = dono };
            db.CrmLeads.Add(lead);
            await db.SaveChangesAsync();
            db.CrmOpportunities.Add(new CrmOpportunity { LeadId = lead.Id, Titulo = "V", ResponsavelId = dono, EtapaId = ganho.Id, PagamentoAdesao = 100m, DataEfetivaFechamento = DateTimeOffset.UtcNow });
        }
        await db.SaveChangesAsync();

        // Gestão comercial: carteira e lista de consultores sem ele.
        var gestao = new ManagementService(db, usuarioAdmin.Object, new EquipeComercialService(db, usuarioAdmin.Object), userManager);
        var vendedores = await gestao.ObterVendedoresAsync(CancellationToken.None, incluirInativos: true);
        Assert.DoesNotContain(vendedores, v => v.Id == admin.Id);
        Assert.Contains(vendedores, v => v.Id == ana.Id && v.RegionalNome == "MG132");

        // TV: ranking sem ele.
        var tv = new TvComercialService(db, new EquipeComercialService(db, usuarioAdmin.Object), usuarioAdmin.Object);
        var painel = await tv.ObterAsync(null, null, CancellationToken.None);
        Assert.Equal(["Ana", "Bruno"], painel.RankingConsultores.Select(c => c.Nome).Order().ToArray());

        // Portal: um consultor (escopo só dele) vê o MESMO ranking da TV, de todas as regionais.
        var usuarioConsultor = TestDbContextFactory.MockCurrentUser(ana.Id);
        var tvDoConsultor = new TvComercialService(db, new EquipeComercialService(db, usuarioConsultor.Object), usuarioConsultor.Object);
        var portal = await tvDoConsultor.ObterRankingGeralAsync(null, null, CancellationToken.None);
        Assert.Equal(painel.RankingConsultores.Select(c => (c.Nome, c.QuantidadeVendas, c.ValorVendido)), portal.Ranking.Select(c => (c.Nome, c.QuantidadeVendas, c.ValorVendido)));

        // O administrador volta a atuar nas vendas: reaparece.
        await cadastro.AtualizarAsync(admin.Id, new UserUpdateRequest("Admin Sem Vendas", null, Roles.Admin, null, null, null, null, true, AtuaNasVendas: true), CancellationToken.None);
        Assert.Contains((await gestao.ObterVendedoresAsync(CancellationToken.None, incluirInativos: true)), v => v.Id == admin.Id);
    }
}
