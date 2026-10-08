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

/// <summary>Administrador com regionais ocultas (ex.: MG134), Gestor regional e Tráfego pago separado por regional.</summary>
public class AdminRestritoARegionalTests
{
    private static Mock<ICurrentUserService> Admin(Guid id)
    {
        var mock = TestDbContextFactory.MockCurrentUser(id, visaoTotal: true);
        mock.Setup(m => m.IsInRole(Roles.Admin)).Returns(true);
        return mock;
    }

    private static UserCreateRequest NovoAdmin(string email, params Guid[] ocultas) =>
        new("Admin", email, "Senha@123", "31999990000", Roles.Admin, null, null, null, null, RegionaisOcultasIds: ocultas);

    [Fact]
    public async Task AdminOcultaUmaRegional_NaoVeUsuariosNemLeadsNemRegionalDela_MasVeOsSemResponsavel()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        await factory.SeedRolesAsync(db);
        using var userManager = TestDbContextFactory.CreateUserManager(db);
        var mg132 = await factory.CriarRegionalAsync(db, "MG132");
        var mg134 = await factory.CriarRegionalAsync(db, "MG134");
        var chefe = await factory.CriarUsuarioAsync(db, "Chefe");
        var ana = await factory.CriarUsuarioAsync(db, "Ana");     ana.RegionalId = mg132.Id;
        var bruno = await factory.CriarUsuarioAsync(db, "Bruno"); bruno.RegionalId = mg134.Id;
        db.CrmLeads.AddRange(
            new CrmLead { NomeOuRazaoSocial = "Lead da Ana", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = ana.Id },
            new CrmLead { NomeOuRazaoSocial = "Lead do Bruno", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = bruno.Id },
            new CrmLead { NomeOuRazaoSocial = "Lead sem dono", TipoPessoa = TipoPessoa.Fisica });
        await db.SaveChangesAsync();

        var servicoChefe = new UserManagementService(db, userManager, Admin(chefe.Id).Object, new NoOpAuditSink());
        var oculto = await servicoChefe.CriarAsync(NovoAdmin("admin@teste.com", mg134.Id), CancellationToken.None);
        Assert.Equal([mg134.Id], oculto.RegionaisOcultasIds);
        Assert.Equal(["MG134"], oculto.RegionaisOcultasNomes);

        var usuarioOculto = Admin(oculto.Id).Object;
        var equipe = new EquipeComercialService(db, usuarioOculto);

        // Quem ele enxerga: tudo, menos a MG134; leads sem responsável continuam (marcador Guid.Empty).
        var visiveis = await equipe.ObterVendedoresVisiveisAsync(CancellationToken.None);
        Assert.NotNull(visiveis);
        Assert.Contains(ana.Id, visiveis!);
        Assert.Contains(chefe.Id, visiveis);
        Assert.Contains(Guid.Empty, visiveis);
        Assert.DoesNotContain(bruno.Id, visiveis);
        Assert.False(await equipe.PodeAcessarVendedorAsync(bruno.Id, CancellationToken.None));
        // O painel da TV ignora o que está oculto: mostra todas as regionais.
        Assert.Null(await equipe.ObterVendedoresVisiveisAsync(CancellationToken.None, ignorarRegionaisOcultas: true));
        // O administrador sem regionais ocultas continua vendo tudo.
        Assert.Null(await new EquipeComercialService(db, Admin(chefe.Id).Object).ObterVendedoresVisiveisAsync(CancellationToken.None));

        // Lista de leads: os da MG132 e os sem dono; o da MG134 some.
        var leads = new LeadService(db, usuarioOculto, equipe, new NoOpLeadAssignmentService(), new NoOpMetaConversionService(), new NoOpAuditSink());
        var lista = await leads.ListarAsync(new LeadFilterRequest(), CancellationToken.None);
        Assert.Equal(["Lead da Ana", "Lead sem dono"], lista.Itens.Select(l => l.NomeOuRazaoSocial).Order());

        // Usuários: a lista não traz a MG134 (quem não tem regional continua aparecendo).
        var servicoOculto = new UserManagementService(db, userManager, usuarioOculto, new NoOpAuditSink());
        var usuarios = await servicoOculto.ListarAsync(new UserFilterRequest(), CancellationToken.None);
        Assert.Contains(usuarios.Itens, u => u.Id == ana.Id);
        Assert.Contains(usuarios.Itens, u => u.Id == chefe.Id);
        Assert.DoesNotContain(usuarios.Itens, u => u.Id == bruno.Id);
        await Assert.ThrowsAsync<CrmForbiddenException>(() => servicoOculto.ObterPorIdAsync(bruno.Id, CancellationToken.None));
        await Assert.ThrowsAsync<CrmForbiddenException>(() => servicoOculto.CriarAsync(
            new UserCreateRequest("Novo", "novo@teste.com", "Senha@123", null, Roles.Comercial, mg134.Id, null, null, null), CancellationToken.None));

        // Ele não oculta nem solta regional de OUTRO administrador (mas ajusta as próprias — ver o fim do teste).
        await Assert.ThrowsAsync<CrmForbiddenException>(() => servicoOculto.CriarAsync(NovoAdmin("outro@teste.com", mg132.Id), CancellationToken.None));
        await Assert.ThrowsAsync<CrmForbiddenException>(() => servicoOculto.AtualizarAsync(chefe.Id,
            new UserUpdateRequest("Admin", null, Roles.Admin, null, null, null, null, true, RegionaisOcultasIds: [mg134.Id], AlterarRegionaisOcultas: true), CancellationToken.None));

        // Regionais listadas e metas: sem a MG134.
        Assert.Equal(["MG132"], (await new LookupService(db, usuarioOculto).ObterRegionaisAsync(CancellationToken.None)).Select(r => r.Nome));
        Assert.Equal(2, (await new LookupService(db, Admin(chefe.Id).Object).ObterRegionaisAsync(CancellationToken.None)).Count);

        // Mas ele mesmo volta a ver todas quando quiser (antes ficava travado: "só quem não tem regionais ocultas altera").
        var solto = await servicoOculto.AtualizarAsync(oculto.Id,
            new UserUpdateRequest("Admin", null, Roles.Admin, null, null, null, null, true, RegionaisOcultasIds: [], AlterarRegionaisOcultas: true), CancellationToken.None);
        Assert.Empty(solto.RegionaisOcultasIds!);
        Assert.Equal(2, (await new LookupService(db, usuarioOculto).ObterRegionaisAsync(CancellationToken.None)).Count);
    }

    [Fact]
    public async Task AdminSemRegionaisOcultas_OcultaMaisDeUmaEDepoisMostraDeNovo()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        await factory.SeedRolesAsync(db);
        using var userManager = TestDbContextFactory.CreateUserManager(db);
        var mg132 = await factory.CriarRegionalAsync(db, "MG132");
        var mg134 = await factory.CriarRegionalAsync(db, "MG134");
        var mg140 = await factory.CriarRegionalAsync(db, "MG140");
        var chefe = await factory.CriarUsuarioAsync(db, "Chefe");
        var servico = new UserManagementService(db, userManager, Admin(chefe.Id).Object, new NoOpAuditSink());
        var criado = await servico.CriarAsync(NovoAdmin("a@teste.com", mg134.Id, mg140.Id), CancellationToken.None);
        Assert.Equal(["MG134", "MG140"], criado.RegionaisOcultasNomes);
        Assert.Equal(["MG132"], (await new LookupService(db, Admin(criado.Id).Object).ObterRegionaisAsync(CancellationToken.None)).Select(r => r.Nome));

        db.ChangeTracker.Clear();
        var solto = await servico.AtualizarAsync(criado.Id,
            new UserUpdateRequest("Admin", null, Roles.Admin, null, null, null, null, true, RegionaisOcultasIds: [], AlterarRegionaisOcultas: true), CancellationToken.None);
        Assert.Empty(solto.RegionaisOcultasIds!);
        Assert.Equal(3, (await new LookupService(db, Admin(criado.Id).Object).ObterRegionaisAsync(CancellationToken.None)).Count);
        _ = mg132;
    }

    [Fact]
    public async Task GestorRegionalDaMg134_VeSoAPropriaRegional()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var mg132 = await factory.CriarRegionalAsync(db, "MG132");
        var mg134 = await factory.CriarRegionalAsync(db, "MG134");
        var gestor = await factory.CriarUsuarioAsync(db, "Gestor 134"); gestor.RegionalId = mg134.Id;
        var bruno = await factory.CriarUsuarioAsync(db, "Bruno");       bruno.RegionalId = mg134.Id;
        var ana = await factory.CriarUsuarioAsync(db, "Ana");           ana.RegionalId = mg132.Id;
        db.CrmLeads.AddRange(
            new CrmLead { NomeOuRazaoSocial = "Lead do Bruno", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = bruno.Id },
            new CrmLead { NomeOuRazaoSocial = "Lead da Ana", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = ana.Id },
            new CrmLead { NomeOuRazaoSocial = "Lead sem dono", TipoPessoa = TipoPessoa.Fisica });
        await db.SaveChangesAsync();
        var usuario = TestDbContextFactory.MockCurrentUser(gestor.Id, gestorComercial: true, podeGerir: true).Object;
        var equipe = new EquipeComercialService(db, usuario);

        var visiveis = await equipe.ObterVendedoresVisiveisAsync(CancellationToken.None);
        Assert.Contains(bruno.Id, visiveis!);
        Assert.DoesNotContain(ana.Id, visiveis);
        Assert.False(await equipe.PodeAcessarVendedorAsync(ana.Id, CancellationToken.None));

        var leads = new LeadService(db, usuario, equipe, new NoOpLeadAssignmentService(), new NoOpMetaConversionService(), new NoOpAuditSink());
        Assert.Equal(["Lead do Bruno"], (await leads.ListarAsync(new LeadFilterRequest(), CancellationToken.None)).Itens.Select(l => l.NomeOuRazaoSocial));
    }

    [Fact]
    public async Task TrafegoPago_SeparaPorRegional_FiltraEAdminComOcultaNaoVeALeadsDela()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var chefe = await factory.CriarUsuarioAsync(db, "Chefe");
        var mg134 = await factory.CriarRegionalAsync(db, "MG134");
        var oculto = await factory.CriarUsuarioAsync(db, "Oculto");
        oculto.RegionaisOcultas = EscopoRegional.Gravar([mg134.Id]);
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

        // Filtro por regional: os números mudam, mas a comparação lado a lado continua mostrando todas.
        var so134 = await servicoChefe.ObterAsync(new MarketingFilterRequest { Regional = ["MG134"] }, CancellationToken.None);
        Assert.Equal(2, so134.Indicadores.TotalLeads);
        Assert.Equal(3, so134.PorRegional!.Count);

        // Admin que oculta a MG134: não vê os leads dela, nem pedindo, nem mandando a lista de ocultas vazia.
        var servicoOculto = new MarketingService(db, Admin(oculto.Id).Object);
        var semFiltro = await servicoOculto.ObterAsync(new MarketingFilterRequest { RegionaisOcultas = [] }, CancellationToken.None);
        Assert.Equal(4, semFiltro.Indicadores.TotalLeads);
        Assert.Equal(["MG132", "Sem regional"], semFiltro.PorRegional!.Select(r => r.Regional));
        Assert.Equal(["MG132", "Sem regional"], semFiltro.Opcoes!.Regionais);
        var pediu134 = await servicoOculto.ObterAsync(new MarketingFilterRequest { Regional = ["MG134"] }, CancellationToken.None);
        Assert.Equal(0, pediu134.Indicadores.TotalLeads);
        Assert.Equal(4, (await servicoOculto.ListarLeadsAsync(new MarketingFilterRequest(), 1, 20, CancellationToken.None)).TotalRegistros);
    }
}
