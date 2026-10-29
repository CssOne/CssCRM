using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Authorization;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using CssVision.Web.Services.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace CssVision.Web.Tests.Services;

public class CobrancasCalculoTests
{
    private static readonly Guid Ana = Guid.NewGuid();
    private static readonly DateOnly Hoje = new(2026, 10, 21);

    private static CrmFinanceiroValor V(ParametroFinanceiro p, int mes, decimal valor) =>
        new() { UsuarioId = Ana, Parametro = p, VigenteDesde = new DateOnly(2026, mes, 1), Valor = valor };

    private static CrmFinanceiroPerfil Pj(int inicio = 1, int? parcelas = null, DateOnly? primeira = null) =>
        new() { UsuarioId = Ana, Tipo = TipoContrato.Pj, ModeloPj = ModeloRemuneracaoPj.BonusPorPlaca, InicioContrato = new DateOnly(2026, inicio, 1) };

    private static CrmCobrancaPagamento Pago(ItemCobranca item, int mes, DateOnly? em = null) =>
        new() { UsuarioId = Ana, Item = item, Competencia = new DateOnly(2026, mes, 1), Pago = true, PagoEm = em ?? new DateOnly(2026, mes, 10) };

    private static CrmCustoConsultor Notebook(decimal total, int parcelas, DateOnly primeira, string? descricao = null) =>
        new() { Id = Guid.NewGuid(), UsuarioId = Ana, Tipo = TipoCustoConsultor.Notebook, Descricao = descricao, ValorTotal = total, Parcelas = parcelas, PrimeiraParcela = primeira };

    [Fact]
    public void CustoAdicionalEmParcelas_UmaCobrancaPorParcelaAteOMesAtual_ComNumeroDaParcelaEValorDividido()
    {
        var custo = Notebook(1000m, 4, new DateOnly(2026, 8, 25));   // vence dia 25: ago, set, out, nov; hoje é 21/10, então a de outubro ainda vai vencer

        var lista = CobrancasCalculo.Listar(null, [], ContratoFinanceiroCalculo.Regras.Nenhuma, [], [custo], Hoje);

        var parcelas = lista.OrderBy(c => c.Competencia).ToList();
        Assert.Equal([8, 9, 10], parcelas.Select(c => c.Competencia.Month).ToArray());
        Assert.All(parcelas, c => { Assert.Equal("Adicional", c.Item); Assert.Equal("Notebook", c.Rotulo); Assert.Equal(custo.Id, c.CustoId); Assert.Equal(250m, c.Valor); });
        Assert.Equal("Notebook — parcela 1/4", parcelas[0].Descricao);
        Assert.True(parcelas[0].Atrasada);       // agosto já acabou e não foi pago
        Assert.False(parcelas[2].Atrasada);      // vence dia 25 de outubro: ainda no prazo
        Assert.Equal(new DateOnly(2026, 10, 25), parcelas[2].Vencimento);
    }

    [Fact]
    public void CustoAdicional_ADiferencaDeArredondamentoFicaNaUltimaParcela_EDescricaoSubstituiOTipo()
    {
        var custo = Notebook(100m, 3, new DateOnly(2026, 8, 1), "Notebook Dell");
        var tres = CobrancasCalculo.Listar(null, [], ContratoFinanceiroCalculo.Regras.Nenhuma, [], [custo], new DateOnly(2026, 12, 1)).OrderBy(c => c.Competencia).ToList();

        Assert.Equal([33.33m, 33.33m, 33.34m], tres.Select(c => c.Valor).ToArray());
        Assert.Equal(100m, tres.Sum(c => c.Valor));
        Assert.StartsWith("Notebook Dell — parcela", tres[0].Descricao);
    }

    [Fact]
    public void CustoAdicional_ParcelaPagaFicaPagaEPagamentoDeOutroCustoNaoInterfere()
    {
        var custo = Notebook(300m, 3, new DateOnly(2026, 8, 1));
        var outro = Notebook(300m, 3, new DateOnly(2026, 8, 1));
        var pagamento = new CrmCobrancaPagamento { UsuarioId = Ana, Item = ItemCobranca.Adicional, CustoId = custo.Id, Competencia = new DateOnly(2026, 8, 1), Pago = true, PagoEm = new DateOnly(2026, 8, 12) };

        var lista = CobrancasCalculo.Listar(null, [], ContratoFinanceiroCalculo.Regras.Nenhuma, [pagamento], [custo, outro], Hoje);

        Assert.Single(lista, c => c.Pago);
        Assert.Equal(custo.Id, lista.Single(c => c.Pago).CustoId);
        Assert.Equal(5, lista.Count(c => !c.Pago));
    }

    [Fact]
    public void PagoOuEmAberto_EAtrasadaSoQuandoOMesJaAcabou()
    {
        var valores = new[] { V(ParametroFinanceiro.CustoLead, 1, 50m) };
        var prazo = new CrmFinanceiroPrazo { UsuarioId = Ana, Parametro = ParametroFinanceiro.CustoLead, CarenciaMeses = 8 };
        var leads = new Dictionary<DateOnly, int> { [new DateOnly(2026, 9, 1)] = 10, [new DateOnly(2026, 10, 1)] = 10 };
        var regras = new ContratoFinanceiroCalculo.Regras(null, new Dictionary<ParametroFinanceiro, CrmFinanceiroPrazo> { [ParametroFinanceiro.CustoLead] = prazo }, leads);

        var lista = CobrancasCalculo.Listar(Pj(), valores, regras, [Pago(ItemCobranca.CustoLead, 9)], [], Hoje).Where(c => c.Item != "Coworking").ToList();

        Assert.Equal([10, 9], lista.Select(c => c.Competencia.Month).ToArray());   // meses 9 e 10 (carência de 8 a partir de janeiro = setembro)
        Assert.True(lista[1].Pago);
        Assert.Equal(new DateOnly(2026, 9, 10), lista[1].PagoEm);
        Assert.False(lista[1].Atrasada);
        Assert.False(lista[0].Pago);
        Assert.False(lista[0].Atrasada);   // outubro é o mês corrente
    }

    [Fact]
    public void MesAnteriorSemPagamentoFicaAtrasado()
    {
        var valores = new[] { V(ParametroFinanceiro.TrafegoPago, 1, 300m) };
        var prazo = new CrmFinanceiroPrazo { UsuarioId = Ana, Parametro = ParametroFinanceiro.TrafegoPago, CarenciaMeses = 8 };
        var regras = new ContratoFinanceiroCalculo.Regras(null, new Dictionary<ParametroFinanceiro, CrmFinanceiroPrazo> { [ParametroFinanceiro.TrafegoPago] = prazo });

        var lista = CobrancasCalculo.Listar(Pj(), valores, regras, [], [], Hoje).Where(c => c.Item != "Coworking").ToList();

        Assert.Equal(2, lista.Count);
        Assert.Single(lista, c => c.Atrasada && c.Competencia.Month == 9);
    }

    [Fact]
    public void CltEContratoNaoDefinidoNaoTemCobrancas()
    {
        var clt = new CrmFinanceiroPerfil { UsuarioId = Ana, Tipo = TipoContrato.Clt, InicioContrato = new DateOnly(2026, 1, 1) };

        Assert.Empty(CobrancasCalculo.Listar(clt, [], ContratoFinanceiroCalculo.Regras.Nenhuma, [], [], Hoje));
        Assert.Empty(CobrancasCalculo.Listar(null, [], ContratoFinanceiroCalculo.Regras.Nenhuma, [], [], Hoje));
    }

    [Fact]
    public void CoworkingECobrancaDaCss_ImpostoMeiEDoConsultorENaoApareceNoSetorFinanceiro()
    {
        var lista = CobrancasCalculo.Listar(Pj(), [], ContratoFinanceiroCalculo.Regras.Nenhuma, [], [], Hoje);

        Assert.Contains(lista, c => c.Item == "Coworking" && c.Valor == 100m);
        Assert.DoesNotContain(lista, c => c.Item == "ImpostoMei");
    }
}

public class SetorFinanceiroServiceTests
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

    private static FinanceiroService Financeiro(ApplicationDbContext db, Mock<ICurrentUserService> eu) =>
        new(db, eu.Object, new NoOpAuditSink(), new EquipeComercialService(db, eu.Object));

    private static SetorFinanceiroService Setor(ApplicationDbContext db, Mock<ICurrentUserService> eu) =>
        new(db, eu.Object, new NoOpAuditSink(), new EquipeComercialService(db, eu.Object));

    /// <summary>Ana é PJ desde há 4 meses e o custo de lead (R$ 500) começa 1 mês depois da entrada: 4 cobranças, 3 delas de meses que já acabaram.</summary>
    private static async Task<(Domain.Identity.ApplicationUser Ana, Domain.Identity.ApplicationUser Admin)> CenarioAsync(TestDbContextFactory factory, ApplicationDbContext db)
    {
        await factory.SeedRolesAsync(db);
        var ana = await factory.CriarUsuarioAsync(db, "Ana");
        var admin = await factory.CriarUsuarioAsync(db, "Admin");
        await factory.AtribuirPapelAsync(db, ana, Roles.Comercial);
        await factory.AtribuirPapelAsync(db, admin, Roles.Admin);
        var gestao = Contrato(db, Como(admin.Id, Roles.Admin));
        await gestao.SalvarPerfilAsync(ana.Id, new FinanceiroPerfilRequest("Pj", null, MesAtual.AddMonths(-4), "BonusPorPlaca"), CancellationToken.None);
        // Custo de lead: R$ 250,00 por lead x 2 leads de tráfego pago por mês = R$ 500,00 por mês.
        await gestao.DefinirPadraoAsync(new FinanceiroPadraoRequest("CustoLead", 250m, 1), CancellationToken.None);
        await gestao.DefinirPadraoAsync(new FinanceiroPadraoRequest("Coworking", 0m, null), CancellationToken.None);   // coworking é cobrança da CSS; zerado aqui para os totais dos testes
        var etapa = await factory.ObterOuCriarEtapaLeadAsync(db);
        var leads = new List<(CrmLead Lead, DateTimeOffset Chegada)>();
        for (var k = -4; k <= 0; k++)
        {
            var mes = MesAtual.AddMonths(k);
            for (var i = 0; i < 2; i++)
                leads.Add((new CrmLead { EtapaId = etapa.Id, NomeOuRazaoSocial = $"Lead {k}/{i}", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = ana.Id, MetaLeadId = $"meta-{k}-{i}" },
                    new DateTimeOffset(mes.Year, mes.Month, 2, 12, 0, 0, TimeSpan.Zero)));
        }
        db.CrmLeads.AddRange(leads.Select(l => l.Lead));
        await db.SaveChangesAsync();
        // O sistema carimba a chegada no momento do cadastro; aqui se volta cada lead para o mês em que "chegou".
        foreach (var (lead, chegada) in leads) lead.ResponsavelAtribuidoEm = chegada;
        await db.SaveChangesAsync();
        return (ana, admin);
    }

    [Fact]
    public async Task GestaoRegistraPagamento_ETotaisAtualizam()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var (ana, admin) = await CenarioAsync(factory, db);
        var setor = Setor(db, Como(admin.Id, Roles.Admin));

        var antes = await setor.ObterAsync(ana.Id, CancellationToken.None);
        Assert.Equal(4, antes.CobrancasEmAberto);
        Assert.Equal(3, antes.CobrancasAtrasadas);
        Assert.Equal(2000m, antes.EmAberto);
        Assert.Equal(500m, antes.CustoLeadMes);      // 2 leads do mês x R$ 250,00
        Assert.Equal(2, antes.LeadsMes);
        Assert.Equal(2000m, antes.CustoLeadTotal);   // 4 meses de cobrança

        await setor.DefinirPagamentoAsync(ana.Id, new CobrancaPagamentoRequest("CustoLead", MesAtual.AddMonths(-3), true, null, "pix"), CancellationToken.None);
        await setor.DefinirPagamentoAsync(ana.Id, new CobrancaPagamentoRequest("CustoLead", MesAtual.AddMonths(-2), true, Hoje, null), CancellationToken.None);

        var depois = await setor.ObterAsync(ana.Id, CancellationToken.None);
        Assert.Equal(2, depois.CobrancasEmAberto);
        Assert.Equal(1, depois.CobrancasAtrasadas);
        Assert.Equal(1000m, depois.EmAberto);
        Assert.Equal(1000m, depois.Pago);
        Assert.Equal("pix", depois.Cobrancas.Single(c => c.Competencia == MesAtual.AddMonths(-3)).Observacao);

        // Desfazer volta a ficar em aberto.
        await setor.DefinirPagamentoAsync(ana.Id, new CobrancaPagamentoRequest("CustoLead", MesAtual.AddMonths(-3), false, null, null), CancellationToken.None);
        Assert.Equal(3, (await setor.ObterAsync(ana.Id, CancellationToken.None)).CobrancasEmAberto);
    }

    [Fact]
    public async Task MarcarComoPagoNasCobrancas_MarcaComoPagoNoControleFinanceiroDoConsultor()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var (ana, admin) = await CenarioAsync(factory, db);
        var setor = Setor(db, Como(admin.Id, Roles.Admin));
        var meuControle = Financeiro(db, Como(ana.Id, Roles.Comercial));

        // Custo de lead do mês (2 leads x R$ 250): enquanto não pago, fica "A pagar" no controle dele.
        var antes = (await meuControle.ObterAnoAsync(null, null, CancellationToken.None)).Custos.Where(c => c.Fixo && c.Categoria == "Custo de Leads" && c.Data == MesAtual).ToList();
        Assert.Equal(FinanceiroStatus.APagar, Assert.Single(antes).Status);
        Assert.Null(antes[0].DataPagamento);

        var dia = new DateOnly(Hoje.Year, Hoje.Month, Hoje.Day);
        await setor.DefinirPagamentoAsync(ana.Id, new CobrancaPagamentoRequest("CustoLead", MesAtual, true, dia, null), CancellationToken.None);

        var pago = (await meuControle.ObterAnoAsync(null, null, CancellationToken.None)).Custos.Single(c => c.Fixo && c.Categoria == "Custo de Leads" && c.Data == MesAtual);
        Assert.Equal(FinanceiroStatus.Pago, pago.Status);
        Assert.Equal(dia, pago.DataPagamento);

        // Desfazer volta a "A pagar".
        await setor.DefinirPagamentoAsync(ana.Id, new CobrancaPagamentoRequest("CustoLead", MesAtual, false, null, null), CancellationToken.None);
        var custos = (await meuControle.ObterAnoAsync(null, null, CancellationToken.None)).Custos;
        Assert.Equal(FinanceiroStatus.APagar, custos.Single(c => c.Fixo && c.Categoria == "Custo de Leads" && c.Data == MesAtual).Status);
        // O imposto MEI é do consultor: aparece pré-preenchido e não depende do Setor Financeiro.
        Assert.All(custos.Where(c => c.Fixo && c.Categoria == "Impostos / MEI"), c => Assert.Equal(FinanceiroStatus.Pago, c.Status));
    }

    [Fact]
    public async Task ConsultorSemContrato_RecebeSozinhoAsCobrancasPadraoDoMes_SoDepoisQueAGestaoDefinirOPadrao()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        await factory.SeedRolesAsync(db);
        var ana = await factory.CriarUsuarioAsync(db, "Ana");
        var admin = await factory.CriarUsuarioAsync(db, "Admin");
        await factory.AtribuirPapelAsync(db, ana, Roles.Comercial);
        await factory.AtribuirPapelAsync(db, admin, Roles.Admin);
        var setor = Setor(db, Como(admin.Id, Roles.Admin));

        // A gestão ainda não definiu nenhum valor padrão: nada é cobrado.
        Assert.Empty((await setor.ObterAsync(ana.Id, CancellationToken.None)).Cobrancas);

        // Define o coworking (R$ 100): a cobrança do mês chega sozinha, sem contrato nem lançamento nenhum.
        await Contrato(db, Como(admin.Id, Roles.Admin)).DefinirPadraoAsync(new FinanceiroPadraoRequest("Coworking", 100m, null), CancellationToken.None);
        var d = await setor.ObterAsync(ana.Id, CancellationToken.None);
        Assert.Null(d.TipoContrato);                                    // continua sem contrato
        var cobranca = Assert.Single(d.Cobrancas);
        Assert.Equal("Coworking", cobranca.Item);
        Assert.Equal(MesAtual, cobranca.Competencia);
        Assert.Equal(100m, cobranca.Valor);

        var resumo = Assert.Single(await setor.ResumoAsync(CancellationToken.None), r => r.ConsultorId == ana.Id);
        Assert.True(resumo.TemCobrancas);
        Assert.Equal(100m, resumo.EmAberto);

        // Aparece como custo a pagar no Controle financeiro dele e vira pago quando o Setor Financeiro dá baixa.
        var meuControle = Financeiro(db, Como(ana.Id, Roles.Comercial));
        Assert.Equal(FinanceiroStatus.APagar, (await meuControle.ObterAnoAsync(null, null, CancellationToken.None)).Custos.Single(c => c.Categoria == "Coworking").Status);
        await setor.DefinirPagamentoAsync(ana.Id, new CobrancaPagamentoRequest("Coworking", MesAtual, true, null, null), CancellationToken.None);
        Assert.Equal(FinanceiroStatus.Pago, (await meuControle.ObterAnoAsync(null, null, CancellationToken.None)).Custos.Single(c => c.Categoria == "Coworking").Status);
    }

    [Fact]
    public async Task ConsultorClt_NaoRecebeCobrancasAutomaticas()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        await factory.SeedRolesAsync(db);
        var ana = await factory.CriarUsuarioAsync(db, "Ana");
        var admin = await factory.CriarUsuarioAsync(db, "Admin");
        await factory.AtribuirPapelAsync(db, ana, Roles.Comercial);
        await factory.AtribuirPapelAsync(db, admin, Roles.Admin);
        var gestao = Contrato(db, Como(admin.Id, Roles.Admin));
        await gestao.DefinirPadraoAsync(new FinanceiroPadraoRequest("Coworking", 100m, null), CancellationToken.None);
        await gestao.SalvarPerfilAsync(ana.Id, new FinanceiroPerfilRequest("Clt", null, MesAtual), CancellationToken.None);

        Assert.Empty((await Setor(db, Como(admin.Id, Roles.Admin)).ObterAsync(ana.Id, CancellationToken.None)).Cobrancas);
    }

    private static (SetorFinanceiroService Servico, Mock<IFileStorageService> Armazenamento, Mock<IPushService> Push) SetorCompleto(ApplicationDbContext db, Mock<ICurrentUserService> eu)
    {
        var armazenamento = new Mock<IFileStorageService>();
        armazenamento.Setup(a => a.SalvarAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync("/uploads/cobrancas/comprovante.png");
        var push = new Mock<IPushService>();
        push.Setup(p => p.EnviarAsync(It.IsAny<Guid>(), It.IsAny<PushMensagem>(), It.IsAny<CancellationToken>())).ReturnsAsync(1);
        return (new SetorFinanceiroService(db, eu.Object, new NoOpAuditSink(), new EquipeComercialService(db, eu.Object), armazenamento.Object, push.Object), armazenamento, push);
    }

    private static IFormFile Png()
    {
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0, 0, 0 };
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "arquivo", "comprovante.png") { Headers = new HeaderDictionary(), ContentType = "image/png" };
    }

    [Fact]
    public async Task ConsultorInformaQuePagou_ComComprovante_AguardaConferencia_FinanceiroConfirma()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var (ana, admin) = await CenarioAsync(factory, db);
        var (consultor, armazenamento, _) = SetorCompleto(db, Como(ana.Id, Roles.Comercial));
        var (financeiro, _, _) = SetorCompleto(db, Como(admin.Id, Roles.Admin));
        var dia = new DateOnly(Hoje.Year, Hoje.Month, Hoje.Day);

        await consultor.InformarPagamentoAsync(new InformarPagamentoRequest("CustoLead", MesAtual, null, dia, "paguei por pix"), Png(), CancellationToken.None);

        var cobranca = (await consultor.ObterAsync(ana.Id, CancellationToken.None)).Cobrancas.Single(c => c.Item == "CustoLead" && c.Competencia == MesAtual);
        Assert.True(cobranca.InformadoPeloConsultor);
        Assert.False(cobranca.Pago);                         // só o financeiro confirma
        Assert.False(cobranca.Atrasada);
        Assert.Equal(dia, cobranca.DataInformada);
        Assert.Equal("/uploads/cobrancas/comprovante.png", cobranca.ComprovanteUrl);
        Assert.Equal("paguei por pix", cobranca.ObservacaoConsultor);
        armazenamento.Verify(a => a.SalvarAsync($"cobrancas/{ana.Id}", It.Is<string>(n => n.EndsWith(".png")), It.IsAny<Stream>(), "image/png", It.IsAny<CancellationToken>()), Times.Once);

        // Volta para o financeiro: aparece aguardando conferência na lista dele.
        Assert.Equal(1, Assert.Single(await financeiro.ResumoAsync(CancellationToken.None), r => r.ConsultorId == ana.Id).AguardandoConferencia);

        // O financeiro confere e confirma: vira paga, com a data que o consultor informou e o comprovante guardado.
        await financeiro.DefinirPagamentoAsync(ana.Id, new CobrancaPagamentoRequest("CustoLead", MesAtual, true, null, null), CancellationToken.None);
        var confirmada = (await financeiro.ObterAsync(ana.Id, CancellationToken.None)).Cobrancas.Single(c => c.Item == "CustoLead" && c.Competencia == MesAtual);
        Assert.True(confirmada.Pago);
        Assert.Equal(dia, confirmada.PagoEm);
        Assert.False(confirmada.InformadoPeloConsultor);
        Assert.Equal("/uploads/cobrancas/comprovante.png", confirmada.ComprovanteUrl);
        Assert.Equal(0, Assert.Single(await financeiro.ResumoAsync(CancellationToken.None), r => r.ConsultorId == ana.Id).AguardandoConferencia);
    }

    [Fact]
    public async Task ConsultorInformaPagamento_SemComprovante_EValidaOsDados()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var (ana, _) = await CenarioAsync(factory, db);
        var (consultor, _, _) = SetorCompleto(db, Como(ana.Id, Roles.Comercial));

        await consultor.InformarPagamentoAsync(new InformarPagamentoRequest("CustoLead", MesAtual, null, Hoje, null), null, CancellationToken.None);   // comprovante é opcional
        Assert.True((await consultor.ObterAsync(ana.Id, CancellationToken.None)).Cobrancas.Single(c => c.Item == "CustoLead" && c.Competencia == MesAtual).InformadoPeloConsultor);

        await Assert.ThrowsAsync<CrmBusinessException>(() => consultor.InformarPagamentoAsync(new InformarPagamentoRequest("CustoLead", MesAtual, null, Hoje.AddDays(2), null), null, CancellationToken.None));   // futuro
        await Assert.ThrowsAsync<CrmBusinessException>(() => consultor.InformarPagamentoAsync(new InformarPagamentoRequest("Pizza", MesAtual, null, Hoje, null), null, CancellationToken.None));
        // Arquivo renomeado (não é PNG de verdade) é recusado.
        var falso = new FormFile(new MemoryStream(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 }), 0, 12, "arquivo", "x.png");
        await Assert.ThrowsAsync<CrmBusinessException>(() => consultor.InformarPagamentoAsync(new InformarPagamentoRequest("CustoLead", MesAtual, null, Hoje, null), falso, CancellationToken.None));
    }

    [Fact]
    public async Task OutroConsultorNaoInformaPagamentoDaCobrancaDeAna()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        await CenarioAsync(factory, db);
        var bia = await factory.CriarUsuarioAsync(db, "Bia");
        await factory.AtribuirPapelAsync(db, bia, Roles.Comercial);
        var (daBia, _, _) = SetorCompleto(db, Como(bia.Id, Roles.Comercial));

        // A cobrança é procurada sempre entre as do próprio usuário: Bia não tem essa cobrança.
        await Assert.ThrowsAsync<CrmBusinessException>(() => daBia.InformarPagamentoAsync(new InformarPagamentoRequest("CustoLead", MesAtual, null, Hoje, null), null, CancellationToken.None));
    }

    [Fact]
    public async Task FinanceiroNaoConfirma_DevolveAoConsultorComMotivo_ECriaAviso()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var (ana, admin) = await CenarioAsync(factory, db);
        var (consultor, _, _) = SetorCompleto(db, Como(ana.Id, Roles.Comercial));
        var (financeiro, _, push) = SetorCompleto(db, Como(admin.Id, Roles.Admin));
        await consultor.InformarPagamentoAsync(new InformarPagamentoRequest("CustoLead", MesAtual, null, Hoje, null), Png(), CancellationToken.None);

        await Assert.ThrowsAsync<CrmBusinessException>(() => financeiro.RecusarPagamentoAsync(ana.Id, new RecusarPagamentoRequest("CustoLead", MesAtual, null, " "), CancellationToken.None));
        await financeiro.RecusarPagamentoAsync(ana.Id, new RecusarPagamentoRequest("CustoLead", MesAtual, null, "O comprovante é de outro valor."), CancellationToken.None);

        var cobranca = (await consultor.ObterAsync(ana.Id, CancellationToken.None)).Cobrancas.Single(c => c.Item == "CustoLead" && c.Competencia == MesAtual);
        Assert.False(cobranca.InformadoPeloConsultor);
        Assert.False(cobranca.Pago);
        Assert.Equal("O comprovante é de outro valor.", cobranca.MotivoRecusa);
        var aviso = await db.CrmAvisosConsultor.SingleAsync(a => a.ConsultorId == ana.Id);
        Assert.Equal("Pagamento não confirmado", aviso.Titulo);
        Assert.Contains("O comprovante é de outro valor.", aviso.Mensagem);
        push.Verify(p => p.EnviarAsync(ana.Id, It.IsAny<PushMensagem>(), It.IsAny<CancellationToken>()), Times.Once);
        await Assert.ThrowsAsync<CrmBusinessException>(() => financeiro.RecusarPagamentoAsync(ana.Id, new RecusarPagamentoRequest("CustoLead", MesAtual, null, "de novo"), CancellationToken.None));   // já devolvida
    }

    [Fact]
    public async Task PagarDepois_ConsultorEscolheODia_RecebeUmLembreteNoDia_ESoSePaga()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var (ana, admin) = await CenarioAsync(factory, db);
        var (consultor, _, _) = SetorCompleto(db, Como(ana.Id, Roles.Comercial));
        var depois = Hoje.AddDays(3);

        await Assert.ThrowsAsync<CrmBusinessException>(() => consultor.AgendarLembreteAsync(new LembreteCobrancaRequest("CustoLead", MesAtual, null, Hoje.AddDays(-1)), CancellationToken.None));   // passado
        await consultor.AgendarLembreteAsync(new LembreteCobrancaRequest("CustoLead", MesAtual, null, depois), CancellationToken.None);
        Assert.Equal(depois, (await consultor.ObterAsync(ana.Id, CancellationToken.None)).Cobrancas.Single(c => c.Item == "CustoLead" && c.Competencia == MesAtual).LembreteEm);

        var push = new Mock<IPushService>();
        push.Setup(p => p.EnviarAsync(It.IsAny<Guid>(), It.IsAny<PushMensagem>(), It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var notificador = new LembreteCobrancaNotificador(db, push.Object, NullLogger<LembreteCobrancaNotificador>.Instance);

        Assert.Equal(0, await notificador.NotificarAsync(Hoje.AddDays(2), CancellationToken.None));   // ainda não é o dia
        Assert.Equal(1, await notificador.NotificarAsync(depois, CancellationToken.None));
        Assert.Equal(0, await notificador.NotificarAsync(depois, CancellationToken.None));            // um lembrete por pedido
        var aviso = await db.CrmAvisosConsultor.SingleAsync(a => a.ConsultorId == ana.Id && a.Titulo == "Lembrete de pagamento");
        Assert.Contains("Custo de lead", aviso.Mensagem);

        // Cancelar o lembrete: nada sai.
        await consultor.AgendarLembreteAsync(new LembreteCobrancaRequest("CustoLead", MesAtual, null, depois.AddDays(1)), CancellationToken.None);
        await consultor.AgendarLembreteAsync(new LembreteCobrancaRequest("CustoLead", MesAtual, null, null), CancellationToken.None);
        Assert.Equal(0, await notificador.NotificarAsync(depois.AddDays(5), CancellationToken.None));
    }

    [Fact]
    public async Task LembretePendente_NaoSai_SeACobrancaJaFoiPaga()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var (ana, admin) = await CenarioAsync(factory, db);
        var (consultor, _, _) = SetorCompleto(db, Como(ana.Id, Roles.Comercial));
        var (financeiro, _, _) = SetorCompleto(db, Como(admin.Id, Roles.Admin));
        await consultor.AgendarLembreteAsync(new LembreteCobrancaRequest("CustoLead", MesAtual, null, Hoje.AddDays(1)), CancellationToken.None);
        await financeiro.DefinirPagamentoAsync(ana.Id, new CobrancaPagamentoRequest("CustoLead", MesAtual, true, null, null), CancellationToken.None);

        var push = new Mock<IPushService>();
        var notificador = new LembreteCobrancaNotificador(db, push.Object, NullLogger<LembreteCobrancaNotificador>.Instance);

        Assert.Equal(0, await notificador.NotificarAsync(Hoje.AddDays(1), CancellationToken.None));
        push.Verify(p => p.EnviarAsync(It.IsAny<Guid>(), It.IsAny<PushMensagem>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Administradores_NaoAparecemNaEquipeNemNaEscolhaDeConsultor_NemNosAvisosDeCobranca()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var (ana, admin) = await CenarioAsync(factory, db);
        admin.AtuaNasVendas = true;   // administrador que também atua nas vendas
        await db.SaveChangesAsync();
        var gestao = Contrato(db, Como(admin.Id, Roles.Admin));
        await gestao.SalvarPerfilAsync(admin.Id, new FinanceiroPerfilRequest("Pj", null, MesAtual.AddMonths(-4), "BonusPorPlaca"), CancellationToken.None);
        var meu = Financeiro(db, Como(admin.Id, Roles.Admin));

        var consultores = await meu.ListarConsultoresAsync(CancellationToken.None);
        Assert.Contains(consultores, c => c.Id == ana.Id);
        Assert.DoesNotContain(consultores, c => c.Id == admin.Id);

        var equipe = await meu.ObterAnoAsync(null, "equipe", CancellationToken.None);
        Assert.Equal(1, equipe.Visao!.Consultores);   // só a Ana

        Assert.DoesNotContain(await gestao.ListarAlertasAsync(CancellationToken.None), a => a.ConsultorId == admin.Id);
        Assert.DoesNotContain(await Setor(db, Como(admin.Id, Roles.Admin)).ResumoAsync(CancellationToken.None), r => r.ConsultorId == admin.Id);
    }

    [Fact]
    public async Task NaoSeRegistraPagamentoDoQueNaoEDevido()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var (ana, admin) = await CenarioAsync(factory, db);
        var setor = Setor(db, Como(admin.Id, Roles.Admin));

        // Tráfego pago sem valor definido, mês futuro e item inexistente.
        await Assert.ThrowsAsync<CrmBusinessException>(() => setor.DefinirPagamentoAsync(ana.Id, new CobrancaPagamentoRequest("TrafegoPago", MesAtual, true, null, null), CancellationToken.None));
        await Assert.ThrowsAsync<CrmBusinessException>(() => setor.DefinirPagamentoAsync(ana.Id, new CobrancaPagamentoRequest("CustoLead", MesAtual.AddMonths(1), true, null, null), CancellationToken.None));
        await Assert.ThrowsAsync<CrmBusinessException>(() => setor.DefinirPagamentoAsync(ana.Id, new CobrancaPagamentoRequest("Cafe", MesAtual, true, null, null), CancellationToken.None));
    }

    [Fact]
    public async Task ConsultorVeAsProprias_NaoEditaENaoVeAsDeOutro()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var (ana, _) = await CenarioAsync(factory, db);
        var bia = await factory.CriarUsuarioAsync(db, "Bia");
        await factory.AtribuirPapelAsync(db, bia, Roles.Comercial);

        var propria = await Setor(db, Como(ana.Id, Roles.Comercial)).ObterAsync(ana.Id, CancellationToken.None);
        Assert.False(propria.PodeEditar);
        Assert.Equal(4, propria.Cobrancas.Count);

        await Assert.ThrowsAsync<CrmForbiddenException>(() =>
            Setor(db, Como(ana.Id, Roles.Comercial)).DefinirPagamentoAsync(ana.Id, new CobrancaPagamentoRequest("CustoLead", MesAtual, true, null, null), CancellationToken.None));
        await Assert.ThrowsAsync<CrmForbiddenException>(() => Setor(db, Como(bia.Id, Roles.Comercial)).ObterAsync(ana.Id, CancellationToken.None));
        await Assert.ThrowsAsync<CrmForbiddenException>(() => Setor(db, Como(ana.Id, Roles.Comercial)).ResumoAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Financeiro_AdicionaCustoParaOConsultor_ParcelasViramCobrancasEPodemSerPagas()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var (ana, admin) = await CenarioAsync(factory, db);
        var setor = Setor(db, Como(admin.Id, Roles.Admin));

        // Notebook de R$ 1.200,00 em 12 parcelas, começando 1 mês atrás: já venceram 2 parcelas (mês passado e este mês).
        var criado = await setor.AdicionarCustoAsync(ana.Id, new CustoConsultorRequest("Notebook", "Notebook Dell", 1200m, 12, MesAtual.AddMonths(-1)), CancellationToken.None);
        Assert.Equal(100m, criado.ValorParcela);

        var d = await setor.ObterAsync(ana.Id, CancellationToken.None);
        var custo = Assert.Single(d.Custos!);
        Assert.Equal(12, custo.Parcelas);
        Assert.Equal(2, custo.ParcelaAtual);
        var parcelas = d.Cobrancas.Where(c => c.Item == "Adicional").OrderBy(c => c.Competencia).ToList();
        Assert.Equal(2, parcelas.Count);
        Assert.Equal("Notebook Dell — parcela 1/12", parcelas[0].Descricao);

        await setor.DefinirPagamentoAsync(ana.Id, new CobrancaPagamentoRequest("Adicional", parcelas[0].Competencia, true, null, null, criado.Id), CancellationToken.None);
        var depois = await setor.ObterAsync(ana.Id, CancellationToken.None);
        Assert.Equal(1, depois.Custos!.Single().ParcelasPagas);
        Assert.Single(depois.Cobrancas, c => c.Item == "Adicional" && c.Pago);

        // As parcelas também são custos do consultor no Controle financeiro dele (a paga como "Pago", a deste mês como "A pagar").
        var meuControle = await Financeiro(db, Como(ana.Id, Roles.Comercial)).ObterAnoAsync(null, null, CancellationToken.None);
        var custosDoNotebook = meuControle.Custos.Where(c => c.Descricao != null && c.Descricao.StartsWith("Notebook Dell")).ToList();
        Assert.True(custosDoNotebook.Count >= 1);
        Assert.All(custosDoNotebook, c => Assert.Equal("Material de Trabalho", c.Categoria));
        Assert.Contains(custosDoNotebook, c => c.Status == FinanceiroStatus.APagar);

        // Remover o custo leva junto as parcelas e os pagamentos.
        await setor.RemoverCustoAsync(ana.Id, criado.Id, CancellationToken.None);
        var sem = await setor.ObterAsync(ana.Id, CancellationToken.None);
        Assert.Empty(sem.Custos!);
        Assert.DoesNotContain(sem.Cobrancas, c => c.Item == "Adicional");
    }

    [Fact]
    public async Task Financeiro_EditaCustoJaLancado_ValorParcelasEVencimento_SemPerderParcelaPaga()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var (ana, admin) = await CenarioAsync(factory, db);
        var setor = Setor(db, Como(admin.Id, Roles.Admin));
        var primeira = new DateOnly(MesAtual.AddMonths(-1).Year, MesAtual.AddMonths(-1).Month, 10);
        var criado = await setor.AdicionarCustoAsync(ana.Id, new CustoConsultorRequest("Notebook", "Notebook Dell", 1200m, 12, primeira), CancellationToken.None);

        // Corrige o valor, as parcelas, o dia do vencimento e a descrição.
        var editado = await setor.AtualizarCustoAsync(ana.Id, criado.Id, new CustoConsultorRequest("Celular", "Celular Samsung", 900m, 9, primeira.AddDays(5)), CancellationToken.None);
        Assert.Equal("Celular", editado.Tipo);
        Assert.Equal(100m, editado.ValorParcela);
        Assert.Equal(9, editado.Parcelas);
        var d = await setor.ObterAsync(ana.Id, CancellationToken.None);
        var parcelas = d.Cobrancas.Where(c => c.CustoId == criado.Id).OrderBy(c => c.Competencia).ToList();
        Assert.Equal(2, parcelas.Count);
        Assert.Equal("Celular Samsung — parcela 1/9", parcelas[0].Descricao);
        Assert.Equal(15, parcelas[0].Vencimento!.Value.Day);

        // Com a 1ª parcela já paga, não dá para mudar o calendário e deixá-la de fora; dá para mudar valor e número de parcelas mantendo o mês dela.
        await setor.DefinirPagamentoAsync(ana.Id, new CobrancaPagamentoRequest("Adicional", parcelas[0].Competencia, true, null, null, criado.Id), CancellationToken.None);
        await Assert.ThrowsAsync<CrmBusinessException>(() => setor.AtualizarCustoAsync(ana.Id, criado.Id, new CustoConsultorRequest("Celular", "Celular Samsung", 900m, 9, primeira.AddMonths(2)), CancellationToken.None));
        var maisParcelas = await setor.AtualizarCustoAsync(ana.Id, criado.Id, new CustoConsultorRequest("Celular", "Celular Samsung", 900m, 18, primeira.AddDays(5)), CancellationToken.None);
        Assert.Equal(50m, maisParcelas.ValorParcela);
        Assert.Equal(1, maisParcelas.ParcelasPagas);

        // Validações e permissão.
        await Assert.ThrowsAsync<CrmBusinessException>(() => setor.AtualizarCustoAsync(ana.Id, criado.Id, new CustoConsultorRequest("Celular", null, 0m, 9, primeira), CancellationToken.None));
        await Assert.ThrowsAsync<CrmNotFoundException>(() => setor.AtualizarCustoAsync(ana.Id, Guid.NewGuid(), new CustoConsultorRequest("Celular", null, 100m, 9, primeira), CancellationToken.None));
        await Assert.ThrowsAsync<CrmForbiddenException>(() => Setor(db, Como(ana.Id, Roles.Comercial)).AtualizarCustoAsync(ana.Id, criado.Id, new CustoConsultorRequest("Celular", null, 100m, 9, primeira), CancellationToken.None));
    }

    [Fact]
    public async Task CustoIgualAOutroJaLancado_ERecusado_ATeQueOFinanceiroConfirme()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var (ana, admin) = await CenarioAsync(factory, db);
        var setor = Setor(db, Como(admin.Id, Roles.Admin));
        var pedido = new CustoConsultorRequest("Notebook", "Notebook Dell", 1200m, 12, MesAtual);
        await setor.AdicionarCustoAsync(ana.Id, pedido, CancellationToken.None);

        // Clique duplo ou lançamento repetido: recusado.
        var erro = await Assert.ThrowsAsync<CrmBusinessException>(() => setor.AdicionarCustoAsync(ana.Id, pedido, CancellationToken.None));
        Assert.Equal("custo_duplicado", erro.Codigo);
        Assert.Single((await setor.ObterAsync(ana.Id, CancellationToken.None)).Custos!);

        // Dois notebooks de verdade: o financeiro confirma e os dois seguem, cada um com as próprias parcelas.
        await setor.AdicionarCustoAsync(ana.Id, pedido with { Forcar = true }, CancellationToken.None);
        var d = await setor.ObterAsync(ana.Id, CancellationToken.None);
        Assert.Equal(2, d.Custos!.Count);
        Assert.Equal(2, d.Cobrancas.Count(c => c.Item == "Adicional" && c.Competencia == MesAtual));

        // Um custo diferente (outro valor) não é duplicado.
        await setor.AdicionarCustoAsync(ana.Id, pedido with { ValorTotal = 1300m }, CancellationToken.None);
        Assert.Equal(3, (await setor.ObterAsync(ana.Id, CancellationToken.None)).Custos!.Count);

        // Editar um custo para ficar igual a outro também é barrado.
        var ultimo = (await setor.ObterAsync(ana.Id, CancellationToken.None)).Custos!.Last();
        await Assert.ThrowsAsync<CrmBusinessException>(() => setor.AtualizarCustoAsync(ana.Id, ultimo.Id, pedido, CancellationToken.None));
    }

    [Fact]
    public async Task AdicionarCusto_ValidaOsDados_ESoOFinanceiroLanca()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var (ana, admin) = await CenarioAsync(factory, db);
        var setor = Setor(db, Como(admin.Id, Roles.Admin));

        await Assert.ThrowsAsync<CrmBusinessException>(() => setor.AdicionarCustoAsync(ana.Id, new CustoConsultorRequest("Notebook", null, 0m, 3, MesAtual), CancellationToken.None));
        await Assert.ThrowsAsync<CrmBusinessException>(() => setor.AdicionarCustoAsync(ana.Id, new CustoConsultorRequest("Notebook", null, 100m, 0, MesAtual), CancellationToken.None));
        await Assert.ThrowsAsync<CrmBusinessException>(() => setor.AdicionarCustoAsync(ana.Id, new CustoConsultorRequest("Notebook", null, 100m, 121, MesAtual), CancellationToken.None));
        await Assert.ThrowsAsync<CrmBusinessException>(() => setor.AdicionarCustoAsync(ana.Id, new CustoConsultorRequest("Outro", null, 100m, 3, MesAtual), CancellationToken.None));   // "outro" precisa de descrição
        await Assert.ThrowsAsync<CrmBusinessException>(() => setor.AdicionarCustoAsync(ana.Id, new CustoConsultorRequest("Pizza", null, 100m, 3, MesAtual), CancellationToken.None));

        // O próprio consultor não lança custo para si.
        await Assert.ThrowsAsync<CrmForbiddenException>(() => Setor(db, Como(ana.Id, Roles.Comercial)).AdicionarCustoAsync(ana.Id, new CustoConsultorRequest("Notebook", null, 100m, 3, MesAtual), CancellationToken.None));
    }

    [Fact]
    public async Task Resumo_MostraEmAbertoPorConsultorDentroDoEscopo()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var (ana, admin) = await CenarioAsync(factory, db);

        var resumo = await Setor(db, Como(admin.Id, Roles.Admin)).ResumoAsync(CancellationToken.None);

        var linha = Assert.Single(resumo, r => r.ConsultorId == ana.Id);
        Assert.Equal(2000m, linha.EmAberto);
        Assert.Equal(500m, linha.CustoLeadMes);
        Assert.Equal(2, linha.LeadsMes);
        Assert.Equal(4, linha.CobrancasEmAberto);
        Assert.Equal(3, linha.CobrancasAtrasadas);
        Assert.Equal("Pj", linha.TipoContrato);
    }

    [Fact]
    public async Task Financeiro_AjustaOValorDeUmaParcela_RemoveERestaura_EOEmAbertoAcompanha()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var (ana, admin) = await CenarioAsync(factory, db);
        var setor = Setor(db, Como(admin.Id, Roles.Admin));
        var criado = await setor.AdicionarCustoAsync(ana.Id, new CustoConsultorRequest("Notebook", "Notebook Dell", 1200m, 12, MesAtual.AddMonths(-1)), CancellationToken.None);
        var antes = await setor.ObterAsync(ana.Id, CancellationToken.None);
        var primeira = antes.Cobrancas.Where(c => c.Item == "Adicional").OrderBy(c => c.Competencia).First();
        var emAbertoAntes = antes.EmAberto;

        // Valor combinado: vale no lugar do calculado, e o do contrato fica guardado para mostrar a diferença.
        await setor.AjustarCobrancaAsync(ana.Id, new CobrancaAjusteRequest("Adicional", primeira.Competencia, criado.Id, 60m), CancellationToken.None);
        var ajustada = await setor.ObterAsync(ana.Id, CancellationToken.None);
        var parcela = ajustada.Cobrancas.Single(c => c.Item == "Adicional" && c.Competencia == primeira.Competencia);
        Assert.Equal(60m, parcela.Valor);
        Assert.Equal(100m, parcela.ValorOriginal);
        Assert.Equal(emAbertoAntes - 40m, ajustada.EmAberto);

        // Remover tira a cobrança do mês (não conta em aberto nem atrasada) e a deixa na lista das removidas.
        await setor.AjustarCobrancaAsync(ana.Id, new CobrancaAjusteRequest("Adicional", primeira.Competencia, criado.Id, null, Remover: true), CancellationToken.None);
        var removida = await setor.ObterAsync(ana.Id, CancellationToken.None);
        Assert.DoesNotContain(removida.Cobrancas, c => c.Item == "Adicional" && c.Competencia == primeira.Competencia);
        Assert.Contains(removida.Removidas!, c => c.Item == "Adicional" && c.Competencia == primeira.Competencia);
        Assert.Equal(emAbertoAntes - 100m, removida.EmAberto);

        // Restaurar (sem valor e sem remover) volta ao que o contrato calcula.
        await setor.AjustarCobrancaAsync(ana.Id, new CobrancaAjusteRequest("Adicional", primeira.Competencia, criado.Id, null), CancellationToken.None);
        var restaurada = await setor.ObterAsync(ana.Id, CancellationToken.None);
        Assert.Equal(100m, restaurada.Cobrancas.Single(c => c.Item == "Adicional" && c.Competencia == primeira.Competencia).Valor);
        Assert.Empty(restaurada.Removidas!);
        Assert.Equal(emAbertoAntes, restaurada.EmAberto);
    }

    [Fact]
    public async Task AjustarCobranca_NaoRemoveCobrancaPaga_NemDeCobrancaInexistente_NemDeQuemNaoEGestao()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var (ana, admin) = await CenarioAsync(factory, db);
        var setor = Setor(db, Como(admin.Id, Roles.Admin));
        var criado = await setor.AdicionarCustoAsync(ana.Id, new CustoConsultorRequest("Notebook", "Notebook Dell", 1200m, 12, MesAtual.AddMonths(-1)), CancellationToken.None);
        var primeira = (await setor.ObterAsync(ana.Id, CancellationToken.None)).Cobrancas.Where(c => c.Item == "Adicional").OrderBy(c => c.Competencia).First();
        await setor.DefinirPagamentoAsync(ana.Id, new CobrancaPagamentoRequest("Adicional", primeira.Competencia, true, null, null, criado.Id), CancellationToken.None);

        var paga = await Assert.ThrowsAsync<CrmBusinessException>(() =>
            setor.AjustarCobrancaAsync(ana.Id, new CobrancaAjusteRequest("Adicional", primeira.Competencia, criado.Id, null, Remover: true), CancellationToken.None));
        Assert.Contains("paga", paga.Message);
        await Assert.ThrowsAsync<CrmBusinessException>(() =>
            setor.AjustarCobrancaAsync(ana.Id, new CobrancaAjusteRequest("Adicional", MesAtual.AddMonths(-9), criado.Id, 10m), CancellationToken.None));
        await Assert.ThrowsAsync<CrmBusinessException>(() =>
            setor.AjustarCobrancaAsync(ana.Id, new CobrancaAjusteRequest("Adicional", primeira.Competencia, criado.Id, -5m), CancellationToken.None));

        // O próprio consultor não ajusta cobrança.
        var eleMesmo = Setor(db, Como(ana.Id, Roles.Comercial));
        await Assert.ThrowsAnyAsync<Exception>(() => eleMesmo.AjustarCobrancaAsync(ana.Id, new CobrancaAjusteRequest("Adicional", primeira.Competencia, criado.Id, 1m), CancellationToken.None));
    }
}
