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

/// <summary>Custos fixos do PJ com padrão para todos, valor e prazo por consultor, e o alerta de que a cobrança vai começar.</summary>
public class ContratoCobrancaCalculoTests
{
    private static readonly Guid Ana = Guid.NewGuid();
    private static readonly DateOnly Hoje = new(2026, 10, 21);

    private static CrmFinanceiroPerfil Pj(int inicio = 1) =>
        new() { UsuarioId = Ana, Tipo = TipoContrato.Pj, ModeloPj = ModeloRemuneracaoPj.BonusPorPlaca, InicioContrato = new DateOnly(2026, inicio, 1) };

    /// <summary>10 leads por mês em 2026; <c>custoLead</c> é o valor POR LEAD.</summary>
    private static ContratoFinanceiroCalculo.Regras Padroes(decimal trafego = 300m, decimal custoLead = 50m, params CrmFinanceiroPrazo[] prazos) =>
        new(
            new Dictionary<ParametroFinanceiro, CrmFinanceiroPadrao>
            {
                [ParametroFinanceiro.TrafegoPago] = new() { Parametro = ParametroFinanceiro.TrafegoPago, Valor = trafego },
                [ParametroFinanceiro.CustoLead] = new() { Parametro = ParametroFinanceiro.CustoLead, Valor = custoLead },
            },
            prazos.ToDictionary(p => p.Parametro),
            Enumerable.Range(1, 12).ToDictionary(m => new DateOnly(2026, m, 1), _ => 10));

    private static string[] Categorias(IReadOnlyList<FinanceiroMesContratoDto> meses, int mes) => meses[mes - 1].Custos.Select(c => c.Categoria).ToArray();

    [Fact]
    public void TrafegoPagoECustoLead_SoComecamDepoisDe4Meses_CoworkingEMeiDesdeOInicio()
    {
        var meses = ContratoFinanceiroCalculo.Meses(Pj(), [], 2026, Hoje, Padroes());

        Assert.Equal(["Coworking", "Impostos / MEI"], Categorias(meses, 4));
        Assert.Equal(["Coworking", "Impostos / MEI", "Gestão de Tráfego", "Custo de Leads"], Categorias(meses, 5));
    }

    [Fact]
    public void CustoLead_EOValorPorLeadVezesOsLeadsDeTrafegoENotionDoMes()
    {
        // R$ 13,00 por lead x 120 leads no mês = R$ 1.560,00; em outro mês, com outro valor e outra quantidade, o custo muda.
        var valores = new[]
        {
            new CrmFinanceiroValor { UsuarioId = Ana, Parametro = ParametroFinanceiro.CustoLead, VigenteDesde = new DateOnly(2026, 5, 1), Valor = 13m },
            new CrmFinanceiroValor { UsuarioId = Ana, Parametro = ParametroFinanceiro.CustoLead, VigenteDesde = new DateOnly(2026, 7, 1), Valor = 15.5m },
        };
        var leads = new Dictionary<DateOnly, int> { [new DateOnly(2026, 5, 1)] = 120, [new DateOnly(2026, 6, 1)] = 80, [new DateOnly(2026, 7, 1)] = 100 };
        var regras = new ContratoFinanceiroCalculo.Regras(null, null, leads);

        var meses = ContratoFinanceiroCalculo.Meses(Pj(), valores, 2026, Hoje, regras);

        decimal Lead(int mes) => meses[mes - 1].Custos.Where(c => c.Chave == "CustoLead").Sum(c => c.Valor);
        Assert.Equal(1560m, Lead(5));
        Assert.Equal(1040m, Lead(6));                       // o valor de maio continua valendo, com 80 leads
        Assert.Equal(1550m, Lead(7));                       // novo valor por lead a partir de julho
        Assert.Equal(0m, Lead(8));                          // mês sem lead = sem custo
        Assert.Equal("Custo de leads — 120 lead(s) × R$ 13,00 (fixo)", meses[4].Custos.Single(c => c.Chave == "CustoLead").Descricao);
    }

    [Fact]
    public void PrazoProprioDoConsultor_AntecipaUmCustoSemMexerNoOutro()
    {
        var prazo = new CrmFinanceiroPrazo { UsuarioId = Ana, Parametro = ParametroFinanceiro.CustoLead, CarenciaMeses = 1 };

        var meses = ContratoFinanceiroCalculo.Meses(Pj(), [], 2026, Hoje, Padroes(prazos: prazo));

        Assert.Contains("Custo de Leads", Categorias(meses, 2));
        Assert.DoesNotContain("Gestão de Tráfego", Categorias(meses, 2));
        Assert.Contains("Gestão de Tráfego", Categorias(meses, 5));
    }

    [Fact]
    public void PadraoDaGestaoMudaParaTodos_EOValorDoConsultorPrevalece()
    {
        var regras = new ContratoFinanceiroCalculo.Regras(new Dictionary<ParametroFinanceiro, CrmFinanceiroPadrao>
        {
            [ParametroFinanceiro.Coworking] = new() { Parametro = ParametroFinanceiro.Coworking, Valor = 150m },
        });

        var padrao = ContratoFinanceiroCalculo.Meses(Pj(), [], 2026, Hoje, regras)[1].Custos.Single(c => c.Categoria == "Coworking");
        var proprio = ContratoFinanceiroCalculo.Meses(Pj(), [new CrmFinanceiroValor { UsuarioId = Ana, Parametro = ParametroFinanceiro.Coworking, VigenteDesde = new DateOnly(2026, 1, 1), Valor = 80m }], 2026, Hoje, regras)[1]
            .Custos.Single(c => c.Categoria == "Coworking");

        Assert.Equal(150m, padrao.Valor);
        Assert.Equal(80m, proprio.Valor);
    }

    [Fact]
    public void CltNaoTemCustosFixosNemAlerta()
    {
        var clt = new CrmFinanceiroPerfil { UsuarioId = Ana, Tipo = TipoContrato.Clt, InicioContrato = new DateOnly(2026, 1, 1) };

        Assert.All(ContratoFinanceiroCalculo.Meses(clt, [], 2026, Hoje, Padroes()), m => Assert.Empty(m.Custos));
        Assert.Empty(ContratoFinanceiroCalculo.AlertasDeInicio(clt, [], Padroes(), new DateOnly(2026, 4, 20)));
    }

    [Fact]
    public void Alerta_AparecePertoDoInicio_ForaDaJanelaOuConfirmadoNaoAparece()
    {
        // Início em janeiro + 4 meses = cobrança a partir de 01/05/2026.
        var perto = ContratoFinanceiroCalculo.AlertasDeInicio(Pj(), [], Padroes(), new DateOnly(2026, 4, 20));
        Assert.Equal([ParametroFinanceiro.TrafegoPago, ParametroFinanceiro.CustoLead], perto.Select(a => a.Parametro).ToArray());
        Assert.All(perto, a => Assert.Equal(11, a.Dias));

        Assert.Empty(ContratoFinanceiroCalculo.AlertasDeInicio(Pj(), [], Padroes(), new DateOnly(2026, 3, 1)));   // longe demais
        Assert.Empty(ContratoFinanceiroCalculo.AlertasDeInicio(Pj(), [], Padroes(), new DateOnly(2026, 5, 1)));   // já começou

        var confirmado = new CrmFinanceiroPrazo { UsuarioId = Ana, Parametro = ParametroFinanceiro.TrafegoPago, AlertaConfirmadoPara = new DateOnly(2026, 5, 1) };
        var restante = ContratoFinanceiroCalculo.AlertasDeInicio(Pj(), [], Padroes(prazos: confirmado), new DateOnly(2026, 4, 20));
        Assert.Equal(ParametroFinanceiro.CustoLead, Assert.Single(restante).Parametro);
    }

    [Fact]
    public void Alerta_SoParaCustoComValorMaiorQueZero()
    {
        var alertas = ContratoFinanceiroCalculo.AlertasDeInicio(Pj(), [], Padroes(trafego: 0m, custoLead: 500m), new DateOnly(2026, 4, 20));

        Assert.Equal(ParametroFinanceiro.CustoLead, Assert.Single(alertas).Parametro);
    }
}

public class FinanceiroCobrancaServiceTests
{
    private static readonly DateOnly Hoje = HorarioBrasilia.Hoje;
    private static readonly DateOnly MesAtual = new(Hoje.Year, Hoje.Month, 1);

    private static Mock<ICurrentUserService> Como(Guid id, params string[] papeis)
    {
        var mock = TestDbContextFactory.MockCurrentUser(id, visaoTotal: papeis.Contains(Roles.Admin));
        foreach (var p in papeis) mock.Setup(m => m.IsInRole(p)).Returns(true);
        return mock;
    }

    private static FinanceiroContratoService Contrato(ApplicationDbContext db, Mock<ICurrentUserService> eu) =>
        new(db, eu.Object, new NoOpAuditSink(), new EquipeComercialService(db, eu.Object));

    /// <summary>Ana é PJ e a cobrança do tráfego pago e do custo de lead começa no mês que vem (entrou há 3 meses, carência de 4).</summary>
    private static async Task<(Domain.Identity.ApplicationUser Ana, Domain.Identity.ApplicationUser Admin)> CenarioAsync(TestDbContextFactory factory, ApplicationDbContext db)
    {
        await factory.SeedRolesAsync(db);
        var ana = await factory.CriarUsuarioAsync(db, "Ana");
        var admin = await factory.CriarUsuarioAsync(db, "Admin");
        await factory.AtribuirPapelAsync(db, ana, Roles.Comercial);
        await factory.AtribuirPapelAsync(db, admin, Roles.Admin);

        var gestao = Contrato(db, Como(admin.Id, Roles.Admin));
        await gestao.SalvarPerfilAsync(ana.Id, new FinanceiroPerfilRequest("Pj", null, MesAtual.AddMonths(-3), "BonusPorPlaca"), CancellationToken.None);
        await gestao.DefinirPadraoAsync(new FinanceiroPadraoRequest("TrafegoPago", 300m, null), CancellationToken.None);
        await gestao.DefinirPadraoAsync(new FinanceiroPadraoRequest("CustoLead", 500m, null), CancellationToken.None);
        return (ana, admin);
    }

    [Fact]
    public async Task ConsultorEGestaoVeemOAlerta_ConsultorSoLeitura()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var (ana, admin) = await CenarioAsync(factory, db);

        var doConsultor = await Contrato(db, Como(ana.Id, Roles.Comercial)).ListarAlertasAsync(CancellationToken.None);
        var daGestao = await Contrato(db, Como(admin.Id, Roles.Admin)).ListarAlertasAsync(CancellationToken.None);

        Assert.Equal(2, doConsultor.Count);
        Assert.All(doConsultor, a => Assert.False(a.PodeEditar));
        Assert.Equal(2, daGestao.Count);
        Assert.All(daGestao, a => { Assert.True(a.PodeEditar); Assert.Equal(ana.Id, a.ConsultorId); Assert.Equal(MesAtual.AddMonths(1), a.InicioCobranca); });
    }

    [Fact]
    public async Task OutroConsultorNaoVeAlertaDeAna()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        await CenarioAsync(factory, db);
        var bia = await factory.CriarUsuarioAsync(db, "Bia");
        await factory.AtribuirPapelAsync(db, bia, Roles.Comercial);

        Assert.Empty(await Contrato(db, Como(bia.Id, Roles.Comercial)).ListarAlertasAsync(CancellationToken.None));
    }

    [Fact]
    public async Task DeixarComecar_TiraOAlerta_EEstenderOPrazoAdiaOInicio()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var (ana, admin) = await CenarioAsync(factory, db);
        var gestao = Contrato(db, Como(admin.Id, Roles.Admin));

        await gestao.ConfirmarInicioAsync(ana.Id, "TrafegoPago", CancellationToken.None);   // deixa começar
        await gestao.DefinirPrazoAsync(ana.Id, new FinanceiroPrazoRequest("CustoLead", 6), CancellationToken.None); // estende: 4 -> 6 meses

        Assert.Empty(await gestao.ListarAlertasAsync(CancellationToken.None));

        var contrato = await gestao.ObterAsync(ana.Id, null, CancellationToken.None);
        var lead = contrato.Atuais.Single(a => a.Parametro == "CustoLead");
        Assert.Equal(6, lead.CarenciaMeses);
        Assert.True(lead.CarenciaPropria);
        Assert.Equal(4, lead.CarenciaPadrao);
        Assert.Equal(MesAtual.AddMonths(3), lead.InicioCobranca);
    }

    [Fact]
    public async Task EstenderAteQueOPrazoChegue_VoltaAAvisarQuandoPertoDoNovoInicio()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var (ana, admin) = await CenarioAsync(factory, db);
        var gestao = Contrato(db, Como(admin.Id, Roles.Admin));

        await gestao.ConfirmarInicioAsync(ana.Id, "CustoLead", CancellationToken.None);
        // Estender 1 mês só: o novo início (daqui a 2 meses) ainda está longe, então não há alerta; e a confirmação antiga não vale para a nova data.
        await gestao.DefinirPrazoAsync(ana.Id, new FinanceiroPrazoRequest("CustoLead", 5), CancellationToken.None);
        Assert.DoesNotContain(await gestao.ListarAlertasAsync(CancellationToken.None), a => a.Parametro == "CustoLead");

        // Voltar a seguir o padrão (4 meses): início no mês que vem outra vez e a confirmação de antes foi zerada ao mexer no prazo.
        await gestao.DefinirPrazoAsync(ana.Id, new FinanceiroPrazoRequest("CustoLead", null), CancellationToken.None);
        Assert.Contains(await gestao.ListarAlertasAsync(CancellationToken.None), a => a.Parametro == "CustoLead");
    }

    [Fact]
    public async Task ConsultorNaoEditaPrazoNemConfirma()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var (ana, _) = await CenarioAsync(factory, db);
        var eu = Contrato(db, Como(ana.Id, Roles.Comercial));

        await Assert.ThrowsAsync<CrmForbiddenException>(() => eu.DefinirPrazoAsync(ana.Id, new FinanceiroPrazoRequest("CustoLead", 0), CancellationToken.None));
        await Assert.ThrowsAsync<CrmForbiddenException>(() => eu.ConfirmarInicioAsync(ana.Id, "CustoLead", CancellationToken.None));
    }

    [Fact]
    public async Task PadraoParaTodosOsPj_SoAdminGestorMasterESupervisorDefinem_GestorRegionalSoVe()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        await CenarioAsync(factory, db);
        var regional = await factory.CriarUsuarioAsync(db, "Regional");
        await factory.AtribuirPapelAsync(db, regional, Roles.GestorComercial);
        var s = Contrato(db, Como(regional.Id, Roles.GestorComercial));

        await Assert.ThrowsAsync<CrmForbiddenException>(() => s.DefinirPadraoAsync(new FinanceiroPadraoRequest("Coworking", 1m, null), CancellationToken.None));
        var padroes = await s.ObterPadroesAsync(CancellationToken.None);
        Assert.False(padroes.PodeEditar);
        Assert.Equal(300m, padroes.Padroes.Single(p => p.Parametro == "TrafegoPago").Valor);
        Assert.Equal(100m, padroes.Padroes.Single(p => p.Parametro == "Coworking").Valor);   // padrão do sistema
        Assert.Equal(4, padroes.Padroes.Single(p => p.Parametro == "CustoLead").CarenciaMeses);
    }

    [Fact]
    public async Task ValoresInvalidosSaoRecusados()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var (ana, admin) = await CenarioAsync(factory, db);
        var gestao = Contrato(db, Como(admin.Id, Roles.Admin));

        await Assert.ThrowsAsync<CrmBusinessException>(() => gestao.DefinirPrazoAsync(ana.Id, new FinanceiroPrazoRequest("BonusPorPlaca", 1), CancellationToken.None));
        await Assert.ThrowsAsync<CrmBusinessException>(() => gestao.DefinirPrazoAsync(ana.Id, new FinanceiroPrazoRequest("CustoLead", -1), CancellationToken.None));
        await Assert.ThrowsAsync<CrmBusinessException>(() => gestao.DefinirPadraoAsync(new FinanceiroPadraoRequest("Coworking", -5m, null), CancellationToken.None));
        await Assert.ThrowsAsync<CrmBusinessException>(() => gestao.DefinirPadraoAsync(new FinanceiroPadraoRequest("Coworking", null, 999), CancellationToken.None));
    }
}
