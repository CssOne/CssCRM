using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Authorization;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>"Recebe leads" da Gestão comercial e a lista de vendedores com os inativos.</summary>
public class RecebeLeadsTests
{
    [Fact]
    public async Task VendedorComRecebeLeadsDesligado_SaiDoRodizio()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var ana = await factory.CriarUsuarioAsync(db, "Ana Vendedora");
        var bruna = await factory.CriarUsuarioAsync(db, "Bruna Vendedora");
        await factory.AtribuirPapelAsync(db, ana, Roles.Comercial);
        await factory.AtribuirPapelAsync(db, bruna, Roles.Comercial);

        var admin = await factory.CriarUsuarioAsync(db, "Admin");
        var gestao = new ManagementService(db, TestDbContextFactory.MockCurrentUser(admin.Id, visaoTotal: true, podeGerir: true).Object,
            new EquipeComercialService(db, TestDbContextFactory.MockCurrentUser(admin.Id, visaoTotal: true, podeGerir: true).Object),
            TestDbContextFactory.CreateUserManager(db));
        await gestao.AtualizarRecebeLeadsAsync(ana.Id, new AtualizarRecebeLeadsRequest(false), CancellationToken.None);

        var distribuicao = new LeadAssignmentService(db);
        Assert.Equal(bruna.Id, await distribuicao.ProximoResponsavelAsync(null, CancellationToken.None));

        await gestao.AtualizarRecebeLeadsAsync(ana.Id, new AtualizarRecebeLeadsRequest(true), CancellationToken.None);
        Assert.Equal(ana.Id, await distribuicao.ProximoResponsavelAsync(null, CancellationToken.None));
    }

    [Fact]
    public async Task ListaDeVendedores_ComOuSemInativos()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var admin = await factory.CriarUsuarioAsync(db, "Admin");
        var ativa = await factory.CriarUsuarioAsync(db, "Ativa");
        var inativa = await factory.CriarUsuarioAsync(db, "Inativa");
        await factory.AtribuirPapelAsync(db, ativa, Roles.Comercial);
        await factory.AtribuirPapelAsync(db, inativa, Roles.Comercial);
        inativa.Ativo = false;
        await db.SaveChangesAsync();

        var usuario = TestDbContextFactory.MockCurrentUser(admin.Id, visaoTotal: true, podeGerir: true).Object;
        var gestao = new ManagementService(db, usuario, new EquipeComercialService(db, usuario), TestDbContextFactory.CreateUserManager(db));

        var soAtivos = await gestao.ObterVendedoresAsync(CancellationToken.None);
        var todos = await gestao.ObterVendedoresAsync(CancellationToken.None, incluirInativos: true);

        Assert.DoesNotContain(soAtivos, v => v.Id == inativa.Id);
        var inativo = Assert.Single(todos, v => v.Id == inativa.Id);
        Assert.False(inativo.Ativo);
        Assert.Contains(todos, v => v.Id == ativa.Id && v.Ativo && v.RecebeLeads);
    }
}
