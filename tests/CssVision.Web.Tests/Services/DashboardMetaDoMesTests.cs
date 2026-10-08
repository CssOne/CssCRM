using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Domain.Identity;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>
/// "Meta do mês" do dashboard (Portal do Consultor e Visão geral): a meta da regional vale NO LUGAR das metas individuais, não soma com elas.
/// Caso real que motivou: 745 (individuais) + 500 (MG132) + 200 (MG134) = 1445, quando a meta definida para a regional era 500.
/// </summary>
public class DashboardMetaDoMesTests
{
    private sealed class Cenario
    {
        public required TestDbContextFactory Factory { get; init; }
        public required ApplicationDbContext Db { get; init; }
        public required CrmRegional Mg132 { get; init; }
        public required CrmRegional Mg134 { get; init; }
        public required ApplicationUser Admin { get; init; }
        public required ApplicationUser Ana { get; init; }   // MG132, individual 30 / R$ 6.000
        public required ApplicationUser Bia { get; init; }   // MG132, individual 45
        public required ApplicationUser Cris { get; init; }  // MG134, individual 20 / R$ 3.000
        public required ApplicationUser Dora { get; init; }  // sem regional, individual 10
        public required ApplicationUser Eva { get; init; }   // regional MG135 SEM meta cadastrada, individual 15

        public DashboardService Servico(ApplicationUser quem, bool visaoTotal = false)
        {
            var currentUser = TestDbContextFactory.MockCurrentUser(quem.Id, visaoTotal: visaoTotal);
            var equipe = new EquipeComercialService(Db, currentUser.Object);
            var atividades = new ActivityService(Db, currentUser.Object, equipe, new NoOpAuditSink());
            return new DashboardService(Db, equipe, atividades, currentUser.Object);
        }

        public async Task<MetaResultadoDto> MetaAsync(ApplicationUser quem, bool visaoTotal = false, Guid? vendedor = null)
        {
            var hoje = HorarioBrasilia.Hoje;
            var r = await Servico(quem, visaoTotal).ObterAsync(new DashboardFilterRequest(HorarioBrasilia.PrimeiroDiaDoMes(hoje), hoje, vendedor), CancellationToken.None);
            return r.Meta;
        }
    }

    private static async Task<Cenario> MontarAsync(TestDbContextFactory factory)
    {
        var db = factory.CreateContext();
        var mg132 = await factory.CriarRegionalAsync(db, "MG132");
        var mg134 = await factory.CriarRegionalAsync(db, "MG134");
        var mg135 = await factory.CriarRegionalAsync(db, "MG135");
        var admin = await factory.CriarUsuarioAsync(db, "Admin");
        var ana = await factory.CriarUsuarioAsync(db, "Ana");
        var bia = await factory.CriarUsuarioAsync(db, "Bia");
        var cris = await factory.CriarUsuarioAsync(db, "Cris");
        var dora = await factory.CriarUsuarioAsync(db, "Dora");
        var eva = await factory.CriarUsuarioAsync(db, "Eva");
        ana.RegionalId = mg132.Id;
        bia.RegionalId = mg132.Id;
        cris.RegionalId = mg134.Id;
        eva.RegionalId = mg135.Id;
        await db.SaveChangesAsync();

        var mes = HorarioBrasilia.PrimeiroDiaDoMes(HorarioBrasilia.Hoje);
        db.CrmRegionalGoals.AddRange(
            new CrmRegionalGoal { RegionalId = mg132.Id, MesReferencia = mes, MetaQuantidadeVendas = 500, MetaValor = 100000m },
            new CrmRegionalGoal { RegionalId = mg134.Id, MesReferencia = mes, MetaQuantidadeVendas = 200, MetaValor = null }); // só quantidade
        db.CrmSalesGoals.AddRange(
            new CrmSalesGoal { VendedorId = ana.Id, MesReferencia = mes, MetaQuantidadeVendas = 30, MetaValor = 6000m },
            new CrmSalesGoal { VendedorId = bia.Id, MesReferencia = mes, MetaQuantidadeVendas = 45 },
            new CrmSalesGoal { VendedorId = cris.Id, MesReferencia = mes, MetaQuantidadeVendas = 20, MetaValor = 3000m },
            new CrmSalesGoal { VendedorId = dora.Id, MesReferencia = mes, MetaQuantidadeVendas = 10 },
            new CrmSalesGoal { VendedorId = eva.Id, MesReferencia = mes, MetaQuantidadeVendas = 15, MetaValor = 1500m });
        await db.SaveChangesAsync();

        // Vendas do mês: Ana 2 (R$ 300 + 200), Bia 1 (R$ 100), Cris 1 (R$ 50), Dora 1 (R$ 70), Eva 1 (R$ 25).
        var ganho = await factory.CriarEtapaAsync(db, "Ganho", 9, TipoEtapaPipeline.Ganho);
        async Task Venda(ApplicationUser quem, decimal adesao)
        {
            var lead = new CrmLead { NomeOuRazaoSocial = "Cliente", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = quem.Id };
            db.CrmLeads.Add(lead);
            await db.SaveChangesAsync();
            db.CrmOpportunities.Add(new CrmOpportunity { LeadId = lead.Id, Titulo = "V", ResponsavelId = quem.Id, EtapaId = ganho.Id, PagamentoAdesao = adesao, DataEfetivaFechamento = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }

        await Venda(ana, 300m);
        await Venda(ana, 200m);
        await Venda(bia, 100m);
        await Venda(cris, 50m);
        await Venda(dora, 70m);
        await Venda(eva, 25m);
        return new Cenario { Factory = factory, Db = db, Mg132 = mg132, Mg134 = mg134, Admin = admin, Ana = ana, Bia = bia, Cris = cris, Dora = dora, Eva = eva };
    }

    [Fact]
    public async Task Consultora_VeAMetaDaPropriaRegional_ENaoASomaComAsIndividuais()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);

        var meta = await c.MetaAsync(c.Ana);

        Assert.Equal(500, meta.MetaQuantidade); // não 530 (30 dela + 500) nem 1445
        Assert.Equal(100000m, meta.MetaValor);
    }

    [Fact]
    public async Task Consultora_AsVendasContadasSaoAsDaRegionalInteira_NaoSoAsDela()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);

        var meta = await c.MetaAsync(c.Ana);

        Assert.Equal(3, meta.RealizadoQuantidade); // Ana 2 + Bia 1 (Cris, Dora e Eva são de outras regionais ou sem regional)
        Assert.Equal(600m, meta.RealizadoValor);
        Assert.Equal(0.6m, meta.PercentualAtingido); // 3 de 500
    }

    [Fact]
    public async Task Administrador_SomaAsMetasDasRegionais_NaoAsRegionaisMaisTodasAsIndividuais()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);

        var meta = await c.MetaAsync(c.Admin, visaoTotal: true);

        // MG132: 500 (regional) + MG134: 200 (regional) + MG135 sem meta: 15 (soma das individuais) + Dora sem regional: 10 (individual) = 725.
        // A conta antiga dava 500 + 200 + 30+45+20+10+15 = 820 (e, em produção, 745 + 500 + 200 = 1445).
        Assert.Equal(725, meta.MetaQuantidade);
        Assert.Equal(6, meta.RealizadoQuantidade); // as 6 vendas do mês
        Assert.Equal(745m, meta.RealizadoValor);
    }

    [Fact]
    public async Task Administrador_MetaDeValor_RegionalSemValorCaiParaAsIndividuaisDelaENaoSomaZeroDuasVezes()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);

        var meta = await c.MetaAsync(c.Admin, visaoTotal: true);

        // MG132: 100.000 (regional) · MG134: a regional só definiu quantidade, então vale a individual da Cris (3.000) · MG135: 1.500 (individual da Eva) · Dora: sem valor.
        Assert.Equal(104500m, meta.MetaValor);
    }

    [Fact]
    public async Task RegionalQueSoDefiniuQuantidade_ComplementaOValorComAsIndividuais()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);

        var meta = await c.MetaAsync(c.Cris);

        Assert.Equal(200, meta.MetaQuantidade); // da regional
        Assert.Equal(3000m, meta.MetaValor);    // a regional não definiu valor: usa a individual dela
        Assert.Equal(1, meta.RealizadoQuantidade);
        Assert.Equal(50m, meta.RealizadoValor);
    }

    [Fact]
    public async Task RegionalSemMetaCadastrada_UsaAIndividual_ContraAsVendasDaPropriaPessoa()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);

        var meta = await c.MetaAsync(c.Eva);

        Assert.Equal(15, meta.MetaQuantidade);
        Assert.Equal(1500m, meta.MetaValor);
        Assert.Equal(1, meta.RealizadoQuantidade);
        Assert.Equal(25m, meta.RealizadoValor);
    }

    [Fact]
    public async Task ConsultoraSemRegional_UsaAIndividual()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);

        var meta = await c.MetaAsync(c.Dora);

        Assert.Equal(10, meta.MetaQuantidade);
        Assert.Equal(1, meta.RealizadoQuantidade);
        Assert.Equal(70m, meta.RealizadoValor);
    }

    [Fact]
    public async Task FiltrarPorUmConsultor_MostraAMetaIndividualDele_ENaoADaRegional()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);

        var meta = await c.MetaAsync(c.Admin, visaoTotal: true, vendedor: c.Ana.Id);

        Assert.Equal(30, meta.MetaQuantidade);
        Assert.Equal(6000m, meta.MetaValor);
        Assert.Equal(2, meta.RealizadoQuantidade); // só as vendas da Ana
        Assert.Equal(500m, meta.RealizadoValor);
    }

    [Fact]
    public async Task Administrador_ComRegionalOculta_NaoContaAMetaNemAsVendasDela()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var admin = await c.Db.Users.FindAsync(c.Admin.Id);
        admin!.RegionaisOcultas = c.Mg134.Id.ToString(); // oculta a MG134
        await c.Db.SaveChangesAsync();

        var meta = await c.MetaAsync(c.Admin, visaoTotal: true);

        // Sem a MG134 (200): MG132 500 + MG135 15 + Dora 10 = 525; e sem a venda da Cris.
        Assert.Equal(525, meta.MetaQuantidade);
        Assert.Equal(5, meta.RealizadoQuantidade);
    }

    [Fact]
    public async Task SemNenhumaMetaCadastrada_FicaZeradoSemErro()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var u = await factory.CriarUsuarioAsync(db, "Sozinha");
        var currentUser = TestDbContextFactory.MockCurrentUser(u.Id);
        var equipe = new EquipeComercialService(db, currentUser.Object);
        var service = new DashboardService(db, equipe, new ActivityService(db, currentUser.Object, equipe, new NoOpAuditSink()), currentUser.Object);
        var hoje = HorarioBrasilia.Hoje;

        var meta = (await service.ObterAsync(new DashboardFilterRequest(HorarioBrasilia.PrimeiroDiaDoMes(hoje), hoje, null), CancellationToken.None)).Meta;

        Assert.Equal(0, meta.MetaQuantidade);
        Assert.Equal(0m, meta.MetaValor);
        Assert.Equal(0m, meta.PercentualAtingido);
    }
}
