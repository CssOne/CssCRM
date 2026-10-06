using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Authorization;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Domain.Identity;
using CssVision.Web.Services.Crm;
using CssVision.Web.Services.Notion;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Perfil Financeiro (vê só a regional dele) e avisos de pagamento em aberto para os consultores.</summary>
public class FinanceiroEAvisosTests
{
    private sealed record Cenario(
        ApplicationDbContext Db, ApplicationUser Fin, ApplicationUser Ana, ApplicationUser Bruno, ApplicationUser Gestor134, ApplicationUser Admin,
        Mock<ICurrentUserService> UsuarioFin, Mock<IPushService> Push);

    private static async Task<Cenario> MontarAsync(TestDbContextFactory factory)
    {
        var db = factory.CreateContext();
        await factory.SeedRolesAsync(db);
        var mg132 = await factory.CriarRegionalAsync(db, "MG132");
        var mg134 = await factory.CriarRegionalAsync(db, "MG134");
        async Task<ApplicationUser> Novo(string nome, string papel, Guid? regional)
        {
            var u = await factory.CriarUsuarioAsync(db, nome);
            await factory.AtribuirPapelAsync(db, u, papel);
            u.RegionalId = regional;
            return u;
        }
        var fin = await Novo("Financeiro 132", Roles.Financeiro, mg132.Id);
        var ana = await Novo("Ana", Roles.Comercial, mg132.Id);
        var bruno = await Novo("Bruno", Roles.Comercial, mg134.Id);
        var gestor = await Novo("Gestor 132", Roles.GestorComercial, mg132.Id);
        var admin = await Novo("Admin", Roles.Admin, null);
        await db.SaveChangesAsync();

        var usuarioFin = TestDbContextFactory.MockCurrentUser(fin.Id);          // sem visão total nem gestão
        usuarioFin.Setup(m => m.IsInRole(Roles.Comercial)).Returns(false);
        usuarioFin.Setup(m => m.IsInRole(Roles.Financeiro)).Returns(true);
        return new Cenario(db, fin, ana, bruno, gestor, admin, usuarioFin, new Mock<IPushService>());
    }

    private static ManagementService Gestao(Cenario c) =>
        new(c.Db, c.UsuarioFin.Object, new EquipeComercialService(c.Db, c.UsuarioFin.Object), TestDbContextFactory.CreateUserManager(c.Db));

    private static UserManagementService Usuarios(Cenario c) =>
        new(c.Db, TestDbContextFactory.CreateUserManager(c.Db), c.UsuarioFin.Object, new NoOpAuditSink());

    private static AvisosPagamentoService Avisos(Cenario c, ICurrentUserService quem) =>
        new(c.Db, new EquipeComercialService(c.Db, quem), quem, c.Push.Object, new NoOpAuditSink(), NullLogger<AvisosPagamentoService>.Instance);

    [Fact]
    public async Task Financeiro_VeNaGestaoComercialSoOsConsultoresDaRegionalDele_ELigaDesligaOLead()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        c.Db.CrmLeads.AddRange(
            new CrmLead { NomeOuRazaoSocial = "L1", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = c.Ana.Id },
            new CrmLead { NomeOuRazaoSocial = "L2", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = c.Ana.Id },
            new CrmLead { NomeOuRazaoSocial = "L3", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = c.Bruno.Id });
        await c.Db.SaveChangesAsync();
        var gestao = Gestao(c);

        var vendedores = await gestao.ObterVendedoresAsync(CancellationToken.None);
        Assert.Contains(vendedores, v => v.Id == c.Ana.Id && v.LeadsAtivos == 2);   // quantidade de leads do consultor
        Assert.DoesNotContain(vendedores, v => v.Id == c.Bruno.Id);                  // outra regional: não aparece

        await gestao.AtualizarRecebeLeadsAsync(c.Ana.Id, new AtualizarRecebeLeadsRequest(false), CancellationToken.None);
        Assert.False((await c.Db.Users.AsNoTracking().SingleAsync(u => u.Id == c.Ana.Id)).RecebeLeads);
        await Assert.ThrowsAsync<CrmForbiddenException>(() =>
            gestao.AtualizarRecebeLeadsAsync(c.Bruno.Id, new AtualizarRecebeLeadsRequest(false), CancellationToken.None));

        // O resto da gestão (limites, janela, tipos) continua só da gestão comercial.
        await Assert.ThrowsAsync<CrmForbiddenException>(() =>
            gestao.AtualizarLimiteMensalAsync(c.Ana.Id, new AtualizarLimiteMensalRequest(5), CancellationToken.None));
    }

    [Fact]
    public async Task Financeiro_ListaSoConsultoresDaRegional_AtivaEInativa_MasNaoEditaNemCria()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var usuarios = Usuarios(c);

        var lista = await usuarios.ListarAsync(new UserFilterRequest { TamanhoPagina = 100 }, CancellationToken.None);
        Assert.Equal([c.Ana.Id], lista.Itens.Select(u => u.Id)); // não vê o gestor, o admin, nem a outra regional

        var inativo = await usuarios.AlterarAtivoAsync(c.Ana.Id, false, CancellationToken.None);
        Assert.False(inativo.Ativo);
        c.Db.ChangeTracker.Clear();
        Assert.True((await usuarios.AlterarAtivoAsync(c.Ana.Id, true, CancellationToken.None)).Ativo);

        await Assert.ThrowsAsync<CrmForbiddenException>(() => usuarios.AlterarAtivoAsync(c.Bruno.Id, false, CancellationToken.None));     // outra regional
        await Assert.ThrowsAsync<CrmForbiddenException>(() => usuarios.AlterarAtivoAsync(c.Gestor134.Id, false, CancellationToken.None));  // não é consultor
        await Assert.ThrowsAsync<CrmForbiddenException>(() => usuarios.AlterarAtivoAsync(c.Fin.Id, false, CancellationToken.None));       // a própria conta
        await Assert.ThrowsAsync<CrmForbiddenException>(() => usuarios.CriarAsync(
            new UserCreateRequest("Novo", "novo@teste.com", "Senha@123", null, Roles.Comercial, c.Fin.RegionalId, null, null, null), CancellationToken.None));
        await Assert.ThrowsAsync<CrmForbiddenException>(() => usuarios.AtualizarAsync(c.Ana.Id,
            new UserUpdateRequest("Ana", null, Roles.Comercial, c.Fin.RegionalId, null, null, null, true), CancellationToken.None));
    }

    [Fact]
    public async Task Admin_CriaUsuarioFinanceiro_ComRegionalObrigatoria()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var admin = TestDbContextFactory.MockCurrentUser(c.Admin.Id, visaoTotal: true);
        admin.Setup(m => m.IsInRole(Roles.Admin)).Returns(true);
        var servico = new UserManagementService(c.Db, TestDbContextFactory.CreateUserManager(c.Db), admin.Object, new NoOpAuditSink());

        await Assert.ThrowsAsync<CrmBusinessException>(() => servico.CriarAsync(
            new UserCreateRequest("Fin", "fin@teste.com", "Senha@123", null, Roles.Financeiro, null, null, null, null), CancellationToken.None));
        var criado = await servico.CriarAsync(
            new UserCreateRequest("Fin 134", "fin134@teste.com", "Senha@123", null, Roles.Financeiro, c.Bruno.RegionalId, null, null, null), CancellationToken.None);

        Assert.Contains(Roles.Financeiro, criado.Papeis);
        Assert.Equal(c.Bruno.RegionalId, criado.RegionalId);
    }

    [Fact]
    public async Task Aviso_FinanceiroAvisaConsultorDaRegional_ConsultorRecebePushVeNoCardEDaCiente_DepoisResolve()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var fin = Avisos(c, c.UsuarioFin.Object);

        var enviados = await fin.EnviarAsync(new AvisoPagamentoCreateRequest([c.Ana.Id], "Faltam 2 mensalidades do cliente João", 250m, "ABC1D23"), CancellationToken.None);

        var aviso = Assert.Single(enviados);
        Assert.Equal("Ana", aviso.ConsultorNome);
        Assert.Equal("Pagamento em aberto", aviso.Titulo);
        c.Push.Verify(p => p.EnviarAsync(c.Ana.Id, It.Is<PushMensagem>(m => m.Titulo == "Pagamento em aberto" && m.Corpo.Contains("mensalidades")), It.IsAny<CancellationToken>()), Times.Once);

        // O consultor vê o aviso (card "Avisos importantes"), dá ciente e continua vendo até resolverem.
        var ana = Avisos(c, TestDbContextFactory.MockCurrentUser(c.Ana.Id).Object);
        var meus = await ana.MeusAsync(CancellationToken.None);
        Assert.Equal(aviso.Id, Assert.Single(meus).Id);
        Assert.Null(meus[0].LidoEm);
        await ana.MarcarCienteAsync(aviso.Id, CancellationToken.None);
        Assert.NotNull((await ana.MeusAsync(CancellationToken.None))[0].LidoEm);

        // Outro consultor não vê nem dá ciente no aviso dela.
        var bruno = Avisos(c, TestDbContextFactory.MockCurrentUser(c.Bruno.Id).Object);
        Assert.Empty(await bruno.MeusAsync(CancellationToken.None));
        await Assert.ThrowsAsync<CrmNotFoundException>(() => bruno.MarcarCienteAsync(aviso.Id, CancellationToken.None));

        c.Db.ChangeTracker.Clear();
        var resolvido = await fin.ResolverAsync(aviso.Id, CancellationToken.None);
        Assert.Equal(StatusAvisoConsultor.Resolvido, resolvido.Status);
        Assert.Empty(await ana.MeusAsync(CancellationToken.None));
        Assert.Single(await fin.ListarAsync(StatusAvisoConsultor.Resolvido, null, CancellationToken.None));
    }

    [Fact]
    public async Task Aviso_FinanceiroNaoAvisaOutraRegional_NemQuemNaoEConsultor_ConsultorNaoEnvia_AdminAvisaQualquer()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var fin = Avisos(c, c.UsuarioFin.Object);

        await Assert.ThrowsAsync<CrmForbiddenException>(() => fin.EnviarAsync(new AvisoPagamentoCreateRequest([c.Bruno.Id], "Pagamento em aberto"), CancellationToken.None));
        await Assert.ThrowsAsync<CrmBusinessException>(() => fin.EnviarAsync(new AvisoPagamentoCreateRequest([c.Gestor134.Id], "Pagamento em aberto"), CancellationToken.None));
        await Assert.ThrowsAsync<CrmBusinessException>(() => fin.EnviarAsync(new AvisoPagamentoCreateRequest([], "Pagamento em aberto"), CancellationToken.None));
        await Assert.ThrowsAsync<CrmBusinessException>(() => fin.EnviarAsync(new AvisoPagamentoCreateRequest([c.Ana.Id], "  "), CancellationToken.None));

        var comoConsultor = Avisos(c, TestDbContextFactory.MockCurrentUser(c.Ana.Id).Object);
        await Assert.ThrowsAsync<CrmForbiddenException>(() => comoConsultor.EnviarAsync(new AvisoPagamentoCreateRequest([c.Ana.Id], "Pagamento em aberto"), CancellationToken.None));

        var admin = TestDbContextFactory.MockCurrentUser(c.Admin.Id, visaoTotal: true);
        admin.Setup(m => m.IsInRole(Roles.Admin)).Returns(true);
        var doAdmin = Avisos(c, admin.Object);
        var para2 = await doAdmin.EnviarAsync(new AvisoPagamentoCreateRequest([c.Ana.Id, c.Bruno.Id], "Pagamentos em aberto, falar com o financeiro"), CancellationToken.None);
        Assert.Equal(2, para2.Count); // um aviso para cada consultor
        c.Push.Verify(p => p.EnviarAsync(It.IsAny<Guid>(), It.IsAny<PushMensagem>(), It.IsAny<CancellationToken>()), Times.Exactly(2));

        // A lista do financeiro só traz a regional dele; a do admin traz tudo.
        Assert.Equal(["Ana"], (await fin.ListarAsync(null, null, CancellationToken.None)).Select(a => a.ConsultorNome));
        Assert.Equal(2, (await doAdmin.ListarAsync(null, null, CancellationToken.None)).Count);
    }

    [Fact]
    public async Task Aviso_ReenviaNotificacaoDoQueEstaEmAberto_EResumoContaPorConsultor()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var fin = Avisos(c, c.UsuarioFin.Object);
        var criado = Assert.Single(await fin.EnviarAsync(new AvisoPagamentoCreateRequest([c.Ana.Id], "Pagamento em aberto do cliente"), CancellationToken.None));
        await fin.EnviarAsync(new AvisoPagamentoCreateRequest([c.Ana.Id], "Outro pagamento em aberto"), CancellationToken.None);

        var ana = Avisos(c, TestDbContextFactory.MockCurrentUser(c.Ana.Id).Object);
        await ana.MarcarCienteAsync(criado.Id, CancellationToken.None);

        c.Db.ChangeTracker.Clear();
        var reenviado = await fin.ReenviarAsync(criado.Id, CancellationToken.None);
        Assert.Null(reenviado.LidoEm); // voltou a aparecer como novidade para o consultor
        c.Push.Verify(p => p.EnviarAsync(c.Ana.Id, It.Is<PushMensagem>(m => m.Titulo.StartsWith("Lembrete")), It.IsAny<CancellationToken>()), Times.Once);

        var resumo = await fin.ResumoAsync(CancellationToken.None);
        Assert.Equal(2, Assert.Single(resumo).Abertos);
        Assert.Equal(c.Ana.Id, resumo[0].ConsultorId);

        c.Db.ChangeTracker.Clear();
        await fin.ResolverAsync(criado.Id, CancellationToken.None);
        await Assert.ThrowsAsync<CrmBusinessException>(() => fin.ReenviarAsync(criado.Id, CancellationToken.None)); // resolvido não reenvia
        Assert.Equal(1, Assert.Single(await fin.ResumoAsync(CancellationToken.None)).Abertos);
    }
}
