using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Authorization;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Services.Notion;
using CssVision.Web.Tests.Infrastructure;
using Moq;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Administrador restrito a uma regional (ex.: MG132) e Tráfego pago separado por regional.</summary>
public class AdminRestritoARegionalTests
{
    private static Mock<ICurrentUserService> Admin(Guid id)
    {
        var mock = TestDbContextFactory.MockCurrentUser(id, visaoTotal: true);
        mock.Setup(m => m.IsInRole(Roles.Admin)).Returns(true);
        return mock;
    }

    [Fact]
    public async Task AdminSemRestricao_DefineRestricaoDeOutroAdmin_EelaValeNoQueEleEnxerga()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        await factory.SeedRolesAsync(db);
        using var userManager = TestDbContextFactory.CreateUserManager(db);
        var mg132 = await factory.CriarRegionalAsync(db, "MG132");
        var mg134 = await factory.CriarRegionalAsync(db, "MG134");
        var chefe = await factory.CriarUsuarioAsync(db, "Chefe");
        var ana = await factory.CriarUsuarioAsync(db, "Ana");   ana.RegionalId = mg132.Id;
        var bruno = await factory.CriarUsuarioAsync(db, "Bruno"); bruno.RegionalId = mg134.Id;
        await db.SaveChangesAsync();

        var servicoChefe = new UserManagementService(db, userManager, Admin(chefe.Id).Object, new NoOpAuditSink());
        var restrito = await servicoChefe.CriarAsync(
            new UserCreateRequest("Admin da 132", "admin132@teste.com", "Senha@123", "31999990000", Roles.Admin, null, null, null, null, RegionalRestritaId: mg132.Id),
            CancellationToken.None);
        Assert.Equal(mg132.Id, restrito.RegionalRestritaId);
        Assert.Equal("MG132", restrito.RegionalRestritaNome);

        // Vendedores visíveis: só a equipe da MG132 (e ele mesmo).
        var usuarioRestrito = Admin(restrito.Id).Object;
        var equipe = new EquipeComercialService(db, usuarioRestrito);
        var visiveis = await equipe.ObterVendedoresVisiveisAsync(CancellationToken.None);
        Assert.NotNull(visiveis);
        Assert.Contains(ana.Id, visiveis!);
        Assert.DoesNotContain(bruno.Id, visiveis);
        Assert.False(await equipe.PodeAcessarVendedorAsync(bruno.Id, CancellationToken.None));

        // O chefe, sem restrição, continua vendo tudo.
        Assert.Null(await new EquipeComercialService(db, Admin(chefe.Id).Object).ObterVendedoresVisiveisAsync(CancellationToken.None));

        // Usuários: o restrito só lista a regional dele.
        var servicoRestrito = new UserManagementService(db, userManager, usuarioRestrito, new NoOpAuditSink());
        var lista = await servicoRestrito.ListarAsync(new UserFilterRequest(), CancellationToken.None);
        Assert.Contains(lista.Itens, u => u.Id == ana.Id);
        Assert.DoesNotContain(lista.Itens, u => u.Id == bruno.Id);
        Assert.DoesNotContain(lista.Itens, u => u.Id == chefe.Id);

        // E não pode se soltar nem criar outro administrador.
        await Assert.ThrowsAsync<CrmForbiddenException>(() => servicoRestrito.CriarAsync(
            new UserCreateRequest("Outro Admin", "outro@teste.com", "Senha@123", null, Roles.Admin, null, null, null, null), CancellationToken.None));
        await Assert.ThrowsAsync<CrmForbiddenException>(() => servicoRestrito.AtualizarAsync(restrito.Id,
            new UserUpdateRequest("Admin da 132", null, Roles.Admin, null, null, null, null, true, AlterarRestricaoRegional: true), CancellationToken.None));

        // Regionais listadas: só a dele.
        var lookup = new LookupService(db, usuarioRestrito);
        Assert.Equal(["MG132"], (await lookup.ObterRegionaisAsync(CancellationToken.None)).Select(r => r.Nome));
        Assert.Equal(2, (await new LookupService(db, Admin(chefe.Id).Object).ObterRegionaisAsync(CancellationToken.None)).Count);
    }

    [Fact]
    public async Task AdminSemRestricao_TiraARestricaoDoOutro()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        await factory.SeedRolesAsync(db);
        using var userManager = TestDbContextFactory.CreateUserManager(db);
        var mg132 = await factory.CriarRegionalAsync(db, "MG132");
        var chefe = await factory.CriarUsuarioAsync(db, "Chefe");
        var servico = new UserManagementService(db, userManager, Admin(chefe.Id).Object, new NoOpAuditSink());
        var criado = await servico.CriarAsync(
            new UserCreateRequest("Admin da 132", "a132@teste.com", "Senha@123", null, Roles.Admin, null, null, null, null, RegionalRestritaId: mg132.Id), CancellationToken.None);

        db.ChangeTracker.Clear();
        var solto = await servico.AtualizarAsync(criado.Id,
            new UserUpdateRequest("Admin da 132", null, Roles.Admin, null, null, null, null, true, RegionalRestritaId: null, AlterarRestricaoRegional: true), CancellationToken.None);

        Assert.Null(solto.RegionalRestritaId);
    }

    [Fact]
    public async Task TrafegoPago_SeparaPorRegional_FiltraEORestritoSoVeADele()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var chefe = await factory.CriarUsuarioAsync(db, "Chefe");
        var mg132 = await factory.CriarRegionalAsync(db, "MG132");
        var restrito = await factory.CriarUsuarioAsync(db, "Restrito");
        restrito.RegionalRestritaId = mg132.Id;
        await db.SaveChangesAsync();
        CrmLead L(string nome, string? regional) => new()
        {
            NomeOuRazaoSocial = nome, TipoPessoa = TipoPessoa.Fisica, MetaLeadId = Guid.NewGuid().ToString(), Regional = regional,
        };
        db.CrmLeads.AddRange(L("a", "MG132"), L("b", "MG132"), L("c", "MG132"), L("d", "MG134"), L("e", "MG134"), L("f", null));
        await db.SaveChangesAsync();

        var servicoChefe = new MarketingService(db, Admin(chefe.Id).Object);
        var todos = await servicoChefe.ObterAsync(new MarketingFilterRequest(), CancellationToken.None);
        Assert.Equal(6, todos.Indicadores.TotalLeads);
        Assert.Equal(["MG132", "MG134", "Sem regional"], todos.PorRegional!.Select(r => r.Regional));
        Assert.Equal([3, 2, 1], todos.PorRegional!.Select(r => r.TotalLeads));
        Assert.Equal(50m, todos.PorRegional![0].ParticipacaoPercentual);
        Assert.Equal(["MG132", "MG134", "Sem regional"], todos.Opcoes!.Regionais);

        // Filtro por regional: os números mudam, mas a comparação lado a lado continua mostrando as duas.
        var so134 = await servicoChefe.ObterAsync(new MarketingFilterRequest { Regional = ["MG134"] }, CancellationToken.None);
        Assert.Equal(2, so134.Indicadores.TotalLeads);
        Assert.Equal(3, so134.PorRegional!.Count);

        // Administrador restrito: só a MG132, mesmo pedindo outra ou tentando liberar tudo.
        var servicoRestrito = new MarketingService(db, Admin(restrito.Id).Object);
        var dele = await servicoRestrito.ObterAsync(new MarketingFilterRequest { Regional = ["MG134"], RegionaisPermitidas = ["MG134", "MG132"] }, CancellationToken.None);
        Assert.Equal(0, dele.Indicadores.TotalLeads);              // pediu a MG134, que ele não vê
        var semFiltro = await servicoRestrito.ObterAsync(new MarketingFilterRequest(), CancellationToken.None);
        Assert.Equal(3, semFiltro.Indicadores.TotalLeads);
        Assert.Equal(["MG132"], semFiltro.PorRegional!.Select(r => r.Regional));
        Assert.Equal(["MG132"], semFiltro.Opcoes!.Regionais);
        var lista = await servicoRestrito.ListarLeadsAsync(new MarketingFilterRequest(), 1, 20, CancellationToken.None);
        Assert.Equal(3, lista.TotalRegistros);
    }
}
