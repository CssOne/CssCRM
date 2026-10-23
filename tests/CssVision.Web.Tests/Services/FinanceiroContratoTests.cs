using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Authorization;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Moq;
using Xunit;

namespace CssVision.Web.Tests.Services;

public class ContratoFinanceiroCalculoTests
{
    private static readonly Guid Ana = Guid.NewGuid();
    private static readonly DateOnly Hoje = new(2026, 10, 21);

    private static CrmFinanceiroValor V(ParametroFinanceiro p, int mes, decimal valor) =>
        new() { UsuarioId = Ana, Parametro = p, VigenteDesde = new DateOnly(2026, mes, 1), Valor = valor };

    private static CrmFinanceiroPerfil Perfil(TipoContrato tipo, ModalidadeTransporte transporte = ModalidadeTransporte.Nenhum, int inicio = 1,
        ModeloRemuneracaoPj? modelo = ModeloRemuneracaoPj.BonusPorPlaca) =>
        new() { UsuarioId = Ana, Tipo = tipo, ModeloPj = tipo == TipoContrato.Pj ? modelo : null, Transporte = transporte, InicioContrato = new DateOnly(2026, inicio, 1) };

    [Fact]
    public void Bonus_ValeAteOProximoLancamento_EOHistoricoMostraAMudanca()
    {
        var valores = new[] { V(ParametroFinanceiro.BonusPorPlaca, 1, 100m), V(ParametroFinanceiro.BonusPorPlaca, 7, 200m) };

        var meses = ContratoFinanceiroCalculo.Meses(Perfil(TipoContrato.Pj), valores, 2026, Hoje);

        Assert.Equal(100m, meses[0].BonusPorPlaca);  // jan
        Assert.Equal(100m, meses[5].BonusPorPlaca);  // jun, ainda o antigo
        Assert.Equal(200m, meses[6].BonusPorPlaca);  // jul em diante
        Assert.Equal(200m, meses[11].BonusPorPlaca);
    }

    [Fact]
    public void Recorrencia_ValeSoNoMesLancado()
    {
        var valores = new[] { V(ParametroFinanceiro.Recorrencia, 8, 1500m), V(ParametroFinanceiro.Recorrencia, 9, 2300m) };

        var meses = ContratoFinanceiroCalculo.Meses(Perfil(TipoContrato.Pj, modelo: ModeloRemuneracaoPj.Recorrencia), valores, 2026, Hoje);

        Assert.Equal(0m, meses[6].Recorrencia);      // jul: nada lançado
        Assert.Equal(1500m, meses[7].Recorrencia);   // ago
        Assert.Equal(2300m, meses[8].Recorrencia);   // set
        Assert.Equal(0m, meses[9].Recorrencia);      // out: não repete sozinha
    }

    [Fact]
    public void Pj_TemCustosFixosPadrao_CoworkingEMei_SoDoInicioAteOMesAtual()
    {
        var meses = ContratoFinanceiroCalculo.Meses(Perfil(TipoContrato.Pj, inicio: 8), [], 2026, Hoje);

        Assert.Empty(meses[6].Custos); // jul: antes do início
        var out_ = meses[9].Custos;    // out: mês atual
        Assert.Equal(100m, out_.Single(c => c.Categoria == "Coworking").Valor);
        Assert.Equal(89m, out_.Single(c => c.Categoria == "Impostos / MEI").Valor);
        Assert.Empty(meses[10].Custos); // nov: ainda não aconteceu
    }

    [Fact]
    public void Pj_ValoresDefinidosSubstituemOsPadroes_EZeroRetiraOCusto()
    {
        var valores = new[] { V(ParametroFinanceiro.Coworking, 1, 150m), V(ParametroFinanceiro.ImpostoMei, 1, 0m) };

        var out_ = ContratoFinanceiroCalculo.Meses(Perfil(TipoContrato.Pj), valores, 2026, Hoje)[9].Custos;

        Assert.Equal(150m, out_.Single(c => c.Categoria == "Coworking").Valor);
        Assert.DoesNotContain(out_, c => c.Categoria == "Impostos / MEI");
        
    }

    [Fact]
    public void Transporte_CombustivelPorSemana_UberEOnibusPorDiaUtil()
    {
        // Outubro/2026: 31 dias (4,43 semanas) e 22 dias úteis.
        Assert.Equal(22, ContratoFinanceiroCalculo.DiasUteis(2026, 10));
        Assert.Equal(4.43m, ContratoFinanceiroCalculo.Semanas(2026, 10));
        Assert.Equal(443.00m, ContratoFinanceiroCalculo.CustoDeTransporte(ModalidadeTransporte.Combustivel, 100m, 2026, 10));
        Assert.Equal(880m, ContratoFinanceiroCalculo.CustoDeTransporte(ModalidadeTransporte.Uber, 40m, 2026, 10));
        Assert.Equal(110m, ContratoFinanceiroCalculo.CustoDeTransporte(ModalidadeTransporte.Onibus, 5m, 2026, 10));
        Assert.Equal(0m, ContratoFinanceiroCalculo.CustoDeTransporte(ModalidadeTransporte.Nenhum, 100m, 2026, 10));
    }

    [Fact]
    public void Clt_TemSalarioEPercentual_NaoTemCustosFixosNemRecorrenciaNemBonus()
    {
        var valores = new[]
        {
            V(ParametroFinanceiro.Salario, 1, 2500m), V(ParametroFinanceiro.PercentualAdesao, 1, 0.30m),
            V(ParametroFinanceiro.Coworking, 1, 999m), V(ParametroFinanceiro.BonusPorPlaca, 1, 999m), V(ParametroFinanceiro.Recorrencia, 10, 999m),
        };

        var m = ContratoFinanceiroCalculo.Meses(Perfil(TipoContrato.Clt, ModalidadeTransporte.Uber), valores, 2026, Hoje)[9];

        Assert.Equal(2500m, m.Salario);
        Assert.Equal(0.30m, m.PercentualAdesao);
        Assert.Empty(m.Custos);
        Assert.Equal(0m, m.Recorrencia);
        Assert.Equal(0m, m.BonusPorPlaca);
    }

    [Fact]
    public void SemContrato_NaoGeraNada() => Assert.Empty(ContratoFinanceiroCalculo.Meses(null, [], 2026, Hoje));
}

public class FinanceiroContratoServiceTests
{
    private static Mock<ICurrentUserService> Como(Guid id, params string[] papeis)
    {
        var mock = TestDbContextFactory.MockCurrentUser(id, visaoTotal: papeis.Contains(Roles.Admin));
        foreach (var p in papeis) mock.Setup(m => m.IsInRole(p)).Returns(true);
        return mock;
    }

    private static FinanceiroContratoService Contrato(ApplicationDbContext db, Mock<ICurrentUserService> eu) =>
        new(db, eu.Object, new NoOpAuditSink(), new EquipeComercialService(db, eu.Object));

    private static FinanceiroService Financeiro(ApplicationDbContext db, Mock<ICurrentUserService> eu) =>
        new(db, eu.Object, new NoOpAuditSink(), new EquipeComercialService(db, eu.Object));

    private static async Task<(Domain.Identity.ApplicationUser Ana, Domain.Identity.ApplicationUser Admin)> CenarioAsync(TestDbContextFactory factory, ApplicationDbContext db)
    {
        await factory.SeedRolesAsync(db);
        var ana = await factory.CriarUsuarioAsync(db, "Ana");
        var admin = await factory.CriarUsuarioAsync(db, "Admin");
        await factory.AtribuirPapelAsync(db, ana, Roles.Comercial);
        await factory.AtribuirPapelAsync(db, admin, Roles.Admin);
        return (ana, admin);
    }

    private static readonly DateOnly Hoje = HorarioBrasilia.Hoje;
    private static readonly DateOnly MesAtual = new(Hoje.Year, Hoje.Month, 1);

    [Fact]
    public async Task ConsultorNaoDefineOProprioContrato_SoConsulta()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var (ana, _) = await CenarioAsync(factory, db);
        var s = Contrato(db, Como(ana.Id, Roles.Comercial));

        await Assert.ThrowsAsync<CrmForbiddenException>(() => s.SalvarPerfilAsync(ana.Id, new FinanceiroPerfilRequest("Pj", null, MesAtual, "BonusPorPlaca"), CancellationToken.None));
        await Assert.ThrowsAsync<CrmForbiddenException>(() => s.DefinirValorAsync(ana.Id, new FinanceiroValorRequest("BonusPorPlaca", MesAtual, 100m, null), CancellationToken.None));

        var proprio = await s.ObterAsync(ana.Id, null, CancellationToken.None);
        Assert.False(proprio.PodeEditar);
    }

    [Fact]
    public async Task ConsultorNaoVeOContratoDeOutro()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var (ana, _) = await CenarioAsync(factory, db);
        var bia = await factory.CriarUsuarioAsync(db, "Bia");
        await factory.AtribuirPapelAsync(db, bia, Roles.Comercial);

        await Assert.ThrowsAsync<CrmForbiddenException>(() => Contrato(db, Como(bia.Id, Roles.Comercial)).ObterAsync(ana.Id, null, CancellationToken.None));
    }

    [Fact]
    public async Task Admin_DefinePerfilEValores_ComHistoricoDeQuemDefiniuEQuando()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var (ana, admin) = await CenarioAsync(factory, db);
        var s = Contrato(db, Como(admin.Id, Roles.Admin));

        await s.SalvarPerfilAsync(ana.Id, new FinanceiroPerfilRequest("PJ", "Uber", MesAtual.AddMonths(-3), "BonusPorPlaca"), CancellationToken.None);
        await s.DefinirValorAsync(ana.Id, new FinanceiroValorRequest("BonusPorPlaca", MesAtual.AddMonths(-2), 100m, "início"), CancellationToken.None);
        await s.DefinirValorAsync(ana.Id, new FinanceiroValorRequest("BonusPorPlaca", MesAtual, 200m, null), CancellationToken.None);
        await s.DefinirValorAsync(ana.Id, new FinanceiroValorRequest("BonusPorPlaca", MesAtual, 220m, "corrigido"), CancellationToken.None); // mesmo mês: corrige

        var c = await s.ObterAsync(ana.Id, null, CancellationToken.None);

        Assert.True(c.PodeEditar);
        Assert.Equal("Pj", c.Perfil.Tipo);
        Assert.Equal("Uber", c.Perfil.Transporte);
        var bonus = c.Historico.Where(h => h.Parametro == "BonusPorPlaca").ToList();
        Assert.Equal(2, bonus.Count);                       // o histórico tem um lançamento por mês de vigência
        Assert.Equal(220m, bonus[0].Valor);                 // mais recente primeiro
        Assert.Equal(100m, bonus[1].Valor);                 // quanto recebia antes
        Assert.Equal("Admin", bonus[0].DefinidoPor);
        Assert.Equal(220m, c.Atuais.Single(a => a.Parametro == "BonusPorPlaca").Valor);
        Assert.True(c.Atuais.Single(a => a.Parametro == "Coworking").Padrao);
        Assert.Equal(100m, c.Atuais.Single(a => a.Parametro == "Coworking").Valor);
    }

    [Fact]
    public async Task ValorSemContrato_ParametroDeOutroTipo_ePercentualForaDoLimite_SaoRecusados()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var (ana, admin) = await CenarioAsync(factory, db);
        var s = Contrato(db, Como(admin.Id, Roles.Admin));

        var semContrato = await Assert.ThrowsAsync<CrmBusinessException>(() => s.DefinirValorAsync(ana.Id, new FinanceiroValorRequest("BonusPorPlaca", MesAtual, 100m, null), CancellationToken.None));
        Assert.Equal("financeiro_sem_contrato", semContrato.Codigo);

        await s.SalvarPerfilAsync(ana.Id, new FinanceiroPerfilRequest("Clt", null, MesAtual), CancellationToken.None);
        await Assert.ThrowsAsync<CrmBusinessException>(() => s.DefinirValorAsync(ana.Id, new FinanceiroValorRequest("BonusPorPlaca", MesAtual, 100m, null), CancellationToken.None)); // CLT não tem bônus
        await Assert.ThrowsAsync<CrmBusinessException>(() => s.DefinirValorAsync(ana.Id, new FinanceiroValorRequest("PercentualAdesao", MesAtual, 150m, null), CancellationToken.None));
        await Assert.ThrowsAsync<CrmBusinessException>(() => s.DefinirValorAsync(ana.Id, new FinanceiroValorRequest("Salario", MesAtual, -1m, null), CancellationToken.None));

        await s.DefinirValorAsync(ana.Id, new FinanceiroValorRequest("PercentualAdesao", MesAtual, 30m, null), CancellationToken.None); // 30 = 30%
        Assert.Equal(0.3m, (await s.ObterAsync(ana.Id, null, CancellationToken.None)).Atuais.Single(a => a.Parametro == "PercentualAdesao").Valor);
    }

    [Fact]
    public async Task Perfil_ExigeInicio_ECltNaoTemTransporte()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var (ana, admin) = await CenarioAsync(factory, db);
        var s = Contrato(db, Como(admin.Id, Roles.Admin));

        await Assert.ThrowsAsync<CrmBusinessException>(() => s.SalvarPerfilAsync(ana.Id, new FinanceiroPerfilRequest("Pj", null, null, "BonusPorPlaca"), CancellationToken.None));
        await Assert.ThrowsAsync<CrmBusinessException>(() => s.SalvarPerfilAsync(ana.Id, new FinanceiroPerfilRequest("Terceirizado", null, MesAtual), CancellationToken.None));

        await s.SalvarPerfilAsync(ana.Id, new FinanceiroPerfilRequest("Clt", "Uber", MesAtual), CancellationToken.None);
        Assert.Equal("Nenhum", (await s.ObterAsync(ana.Id, null, CancellationToken.None)).Perfil.Transporte);
    }

    private static async Task VendaGanhaAsync(TestDbContextFactory factory, ApplicationDbContext db, Guid vendedor, decimal adesao, DateTimeOffset? quando = null)
    {
        var ganho = await factory.CriarEtapaAsync(db, $"Ganho {Guid.NewGuid():N}", 9, TipoEtapaPipeline.Ganho);
        var lead = new CrmLead { NomeOuRazaoSocial = "Cliente", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = vendedor };
        db.CrmLeads.Add(lead);
        await db.SaveChangesAsync();
        db.CrmOpportunities.Add(new CrmOpportunity { LeadId = lead.Id, Titulo = "V", ResponsavelId = vendedor, EtapaId = ganho.Id, PagamentoAdesao = adesao, DataEfetivaFechamento = quando ?? DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task PjPorBonus_NoControle_BonusPorPlacaVemDoContrato_ECustosFixosAparecem()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var (ana, admin) = await CenarioAsync(factory, db);
        await VendaGanhaAsync(factory, db, ana.Id, 300m);
        var gestao = Contrato(db, Como(admin.Id, Roles.Admin));
        await gestao.SalvarPerfilAsync(ana.Id, new FinanceiroPerfilRequest("Pj", "Onibus", MesAtual, "BonusPorPlaca"), CancellationToken.None);
        await gestao.DefinirValorAsync(ana.Id, new FinanceiroValorRequest("BonusPorPlaca", MesAtual, 150m, null), CancellationToken.None);
        await gestao.DefinirValorAsync(ana.Id, new FinanceiroValorRequest("Transporte", MesAtual, 10m, null), CancellationToken.None);

        var d = await Financeiro(db, Como(ana.Id, Roles.Comercial)).ObterAnoAsync(null, null, CancellationToken.None);

        Assert.Equal("Pj", d.TipoContrato);
        var venda = Assert.Single(d.Vendas);
        Assert.Equal(300m, venda.ValorAdesao);   // PJ recebe a adesão inteira
        Assert.Equal(150m, venda.ValorBonus);    // bônus por placa definido pela gestão
        Assert.True(venda.BonusAutomatico);
        Assert.Empty(d.Rendas!);                 // PJ por bônus não tem recorrência
        Assert.Contains(d.Custos, c => c.Fixo && c.Categoria == "Coworking" && c.Valor == 100m);
        Assert.Contains(d.Custos, c => c.Fixo && c.Categoria == "Impostos / MEI" && c.Valor == 89m);
        Assert.Contains(d.Custos, c => c.Fixo && c.Categoria == "Transporte / Combustível" && c.Valor == ContratoFinanceiroCalculo.CustoDeTransporte(ModalidadeTransporte.Onibus, 10m, Hoje.Year, Hoje.Month));
    }

    [Fact]
    public async Task Pj_GanhaBonusPorPlacaOuRecorrencia_NuncaOsDois()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var (ana, admin) = await CenarioAsync(factory, db);
        var gestao = Contrato(db, Como(admin.Id, Roles.Admin));

        // Sem escolher o modelo, o PJ não é salvo.
        await Assert.ThrowsAsync<CrmBusinessException>(() => gestao.SalvarPerfilAsync(ana.Id, new FinanceiroPerfilRequest("Pj", null, MesAtual), CancellationToken.None));

        // Por bônus: a recorrência é recusada e só o bônus aparece entre os valores.
        await gestao.SalvarPerfilAsync(ana.Id, new FinanceiroPerfilRequest("Pj", null, MesAtual, "BonusPorPlaca"), CancellationToken.None);
        var recusada = await Assert.ThrowsAsync<CrmBusinessException>(() => gestao.DefinirValorAsync(ana.Id, new FinanceiroValorRequest("Recorrencia", MesAtual, 500m, null), CancellationToken.None));
        Assert.Contains("não os dois", recusada.Message);
        var porBonus = await gestao.ObterAsync(ana.Id, null, CancellationToken.None);
        Assert.Contains(porBonus.Atuais, a => a.Parametro == "BonusPorPlaca");
        Assert.DoesNotContain(porBonus.Atuais, a => a.Parametro == "Recorrencia");

        // Por recorrência: o bônus é recusado.
        await gestao.SalvarPerfilAsync(ana.Id, new FinanceiroPerfilRequest("Pj", null, MesAtual, "Recorrencia"), CancellationToken.None);
        await Assert.ThrowsAsync<CrmBusinessException>(() => gestao.DefinirValorAsync(ana.Id, new FinanceiroValorRequest("BonusPorPlaca", MesAtual, 100m, null), CancellationToken.None));
        var porRecorrencia = await gestao.ObterAsync(ana.Id, null, CancellationToken.None);
        Assert.Contains(porRecorrencia.Atuais, a => a.Parametro == "Recorrencia");
        Assert.DoesNotContain(porRecorrencia.Atuais, a => a.Parametro == "BonusPorPlaca");
    }

    [Fact]
    public async Task PjPorRecorrencia_NoControle_VendaNaoGanhaBonus_ERecorrenciaEntraComoRenda()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var (ana, admin) = await CenarioAsync(factory, db);
        await VendaGanhaAsync(factory, db, ana.Id, 300m);
        var gestao = Contrato(db, Como(admin.Id, Roles.Admin));
        await gestao.SalvarPerfilAsync(ana.Id, new FinanceiroPerfilRequest("Pj", null, MesAtual.AddMonths(-3), "BonusPorPlaca"), CancellationToken.None);
        await gestao.DefinirValorAsync(ana.Id, new FinanceiroValorRequest("BonusPorPlaca", MesAtual, 150m, null), CancellationToken.None);
        // Troca para recorrência: o bônus antigo ainda existe no histórico, mas não conta mais.
        await gestao.SalvarPerfilAsync(ana.Id, new FinanceiroPerfilRequest("Pj", null, MesAtual.AddMonths(-3), "Recorrencia"), CancellationToken.None);
        // A recorrência é paga 2 meses depois: a de 2 meses atrás cai neste mês; a deste mês só será paga daqui a 2 meses.
        await gestao.DefinirValorAsync(ana.Id, new FinanceiroValorRequest("Recorrencia", MesAtual.AddMonths(-2), 800m, null), CancellationToken.None);
        await gestao.DefinirValorAsync(ana.Id, new FinanceiroValorRequest("Recorrencia", MesAtual, 999m, null), CancellationToken.None);

        var d = await Financeiro(db, Como(ana.Id, Roles.Comercial)).ObterAnoAsync(null, null, CancellationToken.None);

        var venda = Assert.Single(d.Vendas);
        Assert.Equal(300m, venda.ValorAdesao);
        Assert.Equal(0m, venda.ValorBonus);
        var renda = Assert.Single(d.Rendas!, r => r.Mes == MesAtual.Month);
        Assert.Equal(800m, renda.Valor);
        Assert.Equal("Recorrência", renda.Tipo);
        Assert.DoesNotContain(d.Rendas!, r => r.Valor == 999m);   // a recorrência deste mês ainda não foi paga
    }

    [Fact]
    public async Task Pj_BonusEPagoDoisMesesDepoisDaVenda_ComDataPrevistaEStatus()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var (ana, admin) = await CenarioAsync(factory, db);
        await VendaGanhaAsync(factory, db, ana.Id, 300m);
        var gestao = Contrato(db, Como(admin.Id, Roles.Admin));
        await gestao.SalvarPerfilAsync(ana.Id, new FinanceiroPerfilRequest("Pj", null, MesAtual, "BonusPorPlaca"), CancellationToken.None);
        await gestao.DefinirValorAsync(ana.Id, new FinanceiroValorRequest("BonusPorPlaca", MesAtual, 150m, null), CancellationToken.None);

        var d = await Financeiro(db, Como(ana.Id, Roles.Comercial)).ObterAnoAsync(null, null, CancellationToken.None);

        var venda = Assert.Single(d.Vendas);
        Assert.Equal(MesAtual.AddMonths(3).AddDays(-1), venda.DataPrevistaBonus);   // venda de janeiro = bônus previsto para o fim de março
        Assert.Equal(FinanceiroStatus.Pendente, venda.StatusBonus);                 // o mês do pagamento ainda não chegou
        Assert.Equal(150m, venda.BonusPrevisto);
        Assert.Null(venda.BonusRecebido);
    }

    [Fact]
    public async Task Pj_BonusEPrevisao_ConsultorInformaQuantoRealmenteRecebeu_MesmoZeroSeOAssociadoNaoPagou()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var (ana, admin) = await CenarioAsync(factory, db);
        await VendaGanhaAsync(factory, db, ana.Id, 300m);
        var gestao = Contrato(db, Como(admin.Id, Roles.Admin));
        await gestao.SalvarPerfilAsync(ana.Id, new FinanceiroPerfilRequest("Pj", null, MesAtual, "BonusPorPlaca"), CancellationToken.None);
        await gestao.DefinirValorAsync(ana.Id, new FinanceiroValorRequest("BonusPorPlaca", MesAtual, 100m, null), CancellationToken.None);
        var eu = Financeiro(db, Como(ana.Id, Roles.Comercial));
        var venda = (await eu.ObterAnoAsync(null, null, CancellationToken.None)).Vendas.Single();

        // Recebeu só R$ 60 (o associado pagou o 1º boleto menor): vale o informado, e a venda passa a recebida.
        await eu.SalvarVendaAsync(null, venda with { BonusRecebido = 60m }, CancellationToken.None);
        var parcial = (await eu.ObterAnoAsync(null, null, CancellationToken.None)).Vendas.Single();
        Assert.Equal(60m, parcial.ValorBonus);
        Assert.Equal(100m, parcial.BonusPrevisto);
        Assert.Equal(FinanceiroStatus.Recebido, parcial.StatusBonus);

        // O associado não pagou o 1º boleto: recebeu zero, e isso é um valor informado (não "ainda não informado").
        await eu.SalvarVendaAsync(null, parcial with { BonusRecebido = 0m }, CancellationToken.None);
        var zero = (await eu.ObterAnoAsync(null, null, CancellationToken.None)).Vendas.Single();
        Assert.Equal(0m, zero.ValorBonus);
        Assert.Equal(0m, zero.BonusRecebido);
        Assert.Equal(FinanceiroStatus.Recebido, zero.StatusBonus);

        // Limpar volta a ser só previsão.
        await eu.SalvarVendaAsync(null, zero with { BonusRecebido = null }, CancellationToken.None);
        var previsao = (await eu.ObterAnoAsync(null, null, CancellationToken.None)).Vendas.Single();
        Assert.Equal(100m, previsao.ValorBonus);
        Assert.Equal(FinanceiroStatus.Pendente, previsao.StatusBonus);

        await Assert.ThrowsAsync<CrmBusinessException>(() => eu.SalvarVendaAsync(null, previsao with { BonusRecebido = -5m }, CancellationToken.None));
    }

    [Fact]
    public async Task Pj_BonusDeVendaAntiga_ComOMesDoPagamentoChegando_FicaPrevistoAteInformar()
    {
        if (Hoje.Month < 3) return;   // precisa de uma venda de 2 meses atrás no mesmo ano
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var (ana, admin) = await CenarioAsync(factory, db);
        var venda2MesesAtras = new DateTimeOffset(MesAtual.AddMonths(-2).Year, MesAtual.AddMonths(-2).Month, 10, 15, 0, 0, TimeSpan.Zero);
        await VendaGanhaAsync(factory, db, ana.Id, 300m, venda2MesesAtras);
        var gestao = Contrato(db, Como(admin.Id, Roles.Admin));
        await gestao.SalvarPerfilAsync(ana.Id, new FinanceiroPerfilRequest("Pj", null, MesAtual.AddMonths(-2), "BonusPorPlaca"), CancellationToken.None);
        await gestao.DefinirValorAsync(ana.Id, new FinanceiroValorRequest("BonusPorPlaca", MesAtual.AddMonths(-2), 150m, null), CancellationToken.None);

        var d = await Financeiro(db, Como(ana.Id, Roles.Comercial)).ObterAnoAsync(null, null, CancellationToken.None);

        var venda = Assert.Single(d.Vendas);
        Assert.Equal(MesAtual.AddMonths(1).AddDays(-1), venda.DataPrevistaBonus);   // previsto para o fim deste mês
        Assert.Equal(FinanceiroStatus.Pendente, venda.StatusBonus);                  // até o consultor informar o que recebeu
        Assert.Equal(150m, venda.ValorBonus);
    }

    [Fact]
    public async Task Clt_NoControle_GanhaSoAPorcentagemDaAdesao_MaisSalario_ESemCustosFixos()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var (ana, admin) = await CenarioAsync(factory, db);
        await VendaGanhaAsync(factory, db, ana.Id, 1000m);
        var gestao = Contrato(db, Como(admin.Id, Roles.Admin));
        await gestao.SalvarPerfilAsync(ana.Id, new FinanceiroPerfilRequest("Clt", null, MesAtual), CancellationToken.None);
        await gestao.DefinirValorAsync(ana.Id, new FinanceiroValorRequest("Salario", MesAtual, 2200m, null), CancellationToken.None);
        await gestao.DefinirValorAsync(ana.Id, new FinanceiroValorRequest("PercentualAdesao", MesAtual, 25m, null), CancellationToken.None);

        var d = await Financeiro(db, Como(ana.Id, Roles.Comercial)).ObterAnoAsync(null, null, CancellationToken.None);

        var venda = Assert.Single(d.Vendas);
        Assert.Equal(250m, venda.ValorAdesao);          // 25% da adesão, não 100%
        Assert.Equal(1000m, venda.AdesaoOriginal);
        Assert.Equal(0m, venda.ValorBonus);
        Assert.Equal(2200m, d.Rendas!.Single(r => r.Tipo == "Salário").Valor);
        Assert.DoesNotContain(d.Custos, c => c.Fixo);   // CLT não tem custos fixos
    }

    [Fact]
    public async Task Clt_ContratoMostraAComissaoCalculadaSozinha_PorcentagemVezesAAdesaoDasVendasDoMes()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var (ana, admin) = await CenarioAsync(factory, db);
        await VendaGanhaAsync(factory, db, ana.Id, 1000m);
        await VendaGanhaAsync(factory, db, ana.Id, 333m);
        var gestao = Contrato(db, Como(admin.Id, Roles.Admin));
        await gestao.SalvarPerfilAsync(ana.Id, new FinanceiroPerfilRequest("Clt", null, MesAtual), CancellationToken.None);
        await gestao.DefinirValorAsync(ana.Id, new FinanceiroValorRequest("Salario", MesAtual, 2200m, null), CancellationToken.None);
        await gestao.DefinirValorAsync(ana.Id, new FinanceiroValorRequest("PercentualAdesao", MesAtual, 30m, null), CancellationToken.None);

        var c = await gestao.ObterAsync(ana.Id, null, CancellationToken.None);

        var mes = c.Meses[MesAtual.Month - 1];
        Assert.Equal(2, mes.VendasDoMes);
        Assert.Equal(1333m, mes.AdesoesDoMes);
        Assert.Equal(399.90m, mes.Comissao);          // 300,00 + 99,90, cada venda arredondada
        Assert.Equal(2200m, mes.Salario);
        // O mesmo valor que o Controle financeiro do consultor mostra venda a venda.
        var d = await Financeiro(db, Como(ana.Id, Roles.Comercial)).ObterAnoAsync(null, null, CancellationToken.None);
        Assert.Equal(mes.Comissao, d.Vendas.Sum(v => v.ValorAdesao));
    }

    [Fact]
    public async Task SemContrato_ControleSeguePedindoOBonusManual()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var (ana, _) = await CenarioAsync(factory, db);
        await VendaGanhaAsync(factory, db, ana.Id, 300m);

        var d = await Financeiro(db, Como(ana.Id, Roles.Comercial)).ObterAnoAsync(null, null, CancellationToken.None);

        Assert.Null(d.TipoContrato);
        Assert.False(Assert.Single(d.Vendas).BonusAutomatico);
        Assert.Empty(d.Rendas!);
    }
}
