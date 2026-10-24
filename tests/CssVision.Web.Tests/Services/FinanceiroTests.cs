using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Authorization;
using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace CssVision.Web.Tests.Services;

public class FinanceiroValidadorTests
{
    private static FinanceiroCustoDto Custo(string categoria = "Coworking", decimal valor = 100m, string status = "APagar", DateOnly? data = null) =>
        new(null, false, data ?? new DateOnly(2026, 10, 10), categoria, "descrição", valor, null, status, null, false);

    [Fact]
    public void Custo_Valido_NaoLanca() => FinanceiroValidador.Validar(Custo());

    [Theory]
    [InlineData("Categoria inventada")]
    [InlineData("")]
    public void Custo_CategoriaForaDaLista_Rejeita(string categoria) =>
        Assert.Throws<CrmBusinessException>(() => FinanceiroValidador.Validar(Custo(categoria: categoria)));

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(10_000_001)]
    public void Custo_ValorForaDoLimite_Rejeita(double valor) =>
        Assert.Throws<CrmBusinessException>(() => FinanceiroValidador.Validar(Custo(valor: (decimal)valor)));

    [Fact]
    public void Custo_StatusInvalido_Rejeita() =>
        Assert.Throws<CrmBusinessException>(() => FinanceiroValidador.Validar(Custo(status: "Hackeado")));

    [Fact]
    public void Custo_DataAbsurda_Rejeita() =>
        Assert.Throws<CrmBusinessException>(() => FinanceiroValidador.Validar(Custo(data: new DateOnly(1900, 1, 1))));

    [Fact]
    public void Venda_DoCard_IgnoraCamposDoCard_MasValidaOComplemento()
    {
        // Produto fora da lista e cliente vazio não importam para venda do card: esses campos vêm do card.
        var ok = new FinanceiroVendaDto(null, Guid.NewGuid(), true, default, "", "Produto livre do card", 0, 100, 0, null, "Recebido", null, "Pendente", null);
        FinanceiroValidador.Validar(ok, doCard: true);

        var bonusNegativo = ok with { ValorBonus = -1 };
        Assert.Throws<CrmBusinessException>(() => FinanceiroValidador.Validar(bonusNegativo, doCard: true));
    }

    [Fact]
    public void Venda_Manual_ExigeCliente() =>
        Assert.Throws<CrmBusinessException>(() => FinanceiroValidador.Validar(
            new FinanceiroVendaDto(null, null, false, new DateOnly(2026, 10, 1), "  ", "Adesão", 10, 0, 0, null, "Pendente", null, "Pendente", null)));

    [Fact]
    public void Limpar_RemoveControleEEspacos_EVazioViraNulo()
    {
        Assert.Equal("Olá mundo", FinanceiroValidador.Limpar("  Olá\u0000 mundo\u0007  "));
        Assert.Null(FinanceiroValidador.Limpar("   \u0000  "));
        Assert.Null(FinanceiroValidador.Limpar(null));
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void Config_ReservaForaDe0a100_Rejeita(double reserva) =>
        Assert.Throws<CrmBusinessException>(() => FinanceiroValidador.Validar(new FinanceiroConfigDto((decimal)reserva, 0)));

    [Fact]
    public void Meta_MesInvalido_Rejeita() =>
        Assert.Throws<CrmBusinessException>(() => FinanceiroValidador.Validar(new FinanceiroMetaDto(13, 0, 0, 0, 0, null)));
}

public class FinanceiroServiceTests
{
    private readonly Mock<IAuditSink> _auditoria = new();

    private FinanceiroService Servico(ApplicationDbContext db, Guid usuario)
    {
        _auditoria.Setup(a => a.RegistrarAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return new FinanceiroService(db, TestDbContextFactory.MockCurrentUser(usuario).Object, _auditoria.Object);
    }

    private static FinanceiroCustoDto NovoCusto(string categoria = "Coworking", decimal valor = 250m, string status = "APagar") =>
        new(null, false, HorarioBrasilia.Hoje, categoria, "Aluguel", valor, null, status, null, true);

    [Fact]
    public async Task Custo_Criado_GravaAuditoria_ELimpaComprovanteSeNaoPago()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var u = await factory.CriarUsuarioAsync(db, "Ana");
        var s = Servico(db, u.Id);

        var criado = await s.SalvarCustoAsync(null, NovoCusto(), CancellationToken.None);

        Assert.NotNull(criado.Id);
        Assert.False(criado.ComprovanteSalvo); // "a pagar" não tem comprovante
        Assert.Null(criado.DataPagamento);
        _auditoria.Verify(a => a.RegistrarAsync("financeiro.custo.criado", nameof(CrmFinanceiroCusto), criado.Id, It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Custo_Pago_SemData_FicaComADataDeHoje()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var u = await factory.CriarUsuarioAsync(db, "Ana");
        var s = Servico(db, u.Id);

        var criado = await s.SalvarCustoAsync(null, NovoCusto(status: "Pago"), CancellationToken.None);

        Assert.Equal(HorarioBrasilia.Hoje, criado.DataPagamento);
        Assert.True(criado.ComprovanteSalvo);
    }

    [Fact]
    public async Task Custo_DeOutraPessoa_NaoEncontrado_NemEditaNemExclui()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var dona = await factory.CriarUsuarioAsync(db, "Dona");
        var intrusa = await factory.CriarUsuarioAsync(db, "Intrusa");
        var criado = await Servico(db, dona.Id).SalvarCustoAsync(null, NovoCusto(), CancellationToken.None);

        var contraIntrusa = Servico(db, intrusa.Id);
        await Assert.ThrowsAsync<CrmNotFoundException>(() => contraIntrusa.SalvarCustoAsync(criado.Id, NovoCusto(valor: 1m), CancellationToken.None));
        await Assert.ThrowsAsync<CrmNotFoundException>(() => contraIntrusa.ExcluirCustoAsync(criado.Id!.Value, CancellationToken.None));

        var vistoPelaIntrusa = await contraIntrusa.ObterAnoAsync(HorarioBrasilia.Hoje.Year, CancellationToken.None);
        Assert.Empty(vistoPelaIntrusa.Custos);
    }

    [Fact]
    public async Task Custo_ReenvioEmSegundos_DevolveOMesmo_SemDuplicar()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var u = await factory.CriarUsuarioAsync(db, "Ana");
        var s = Servico(db, u.Id);

        var a = await s.SalvarCustoAsync(null, NovoCusto(), CancellationToken.None);
        var b = await s.SalvarCustoAsync(null, NovoCusto(), CancellationToken.None);

        Assert.Equal(a.Id, b.Id);
        Assert.Single((await s.ObterAnoAsync(null, CancellationToken.None)).Custos);
    }

    [Fact]
    public async Task Custo_VersaoDesatualizada_LancaConflito()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var u = await factory.CriarUsuarioAsync(db, "Ana");
        var s = Servico(db, u.Id);
        var criado = await s.SalvarCustoAsync(null, NovoCusto(), CancellationToken.None);

        // No SQLite o xmin não muda sozinho; aqui o carimbo enviado é diferente do gravado, o que o serviço trata como conflito.
        var desatualizado = criado with { Versao = (criado.Versao ?? 0) + 1 };
        await Assert.ThrowsAsync<CrmConcurrencyException>(() => s.SalvarCustoAsync(criado.Id, desatualizado, CancellationToken.None));
    }

    [Fact]
    public async Task Custo_Invalido_NaoChegaAoBanco()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var u = await factory.CriarUsuarioAsync(db, "Ana");
        var s = Servico(db, u.Id);

        await Assert.ThrowsAsync<CrmBusinessException>(() => s.SalvarCustoAsync(null, NovoCusto(categoria: "<script>"), CancellationToken.None));
        Assert.Empty(db.CrmFinanceiroCustos);
    }

    [Fact]
    public async Task Venda_Manual_EBloqueada_SoSeComplementaVendaDoCard()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var u = await factory.CriarUsuarioAsync(db, "Ana");
        var s = Servico(db, u.Id);

        var manual = new FinanceiroVendaDto(null, null, false, HorarioBrasilia.Hoje, "Cliente", "Adesão", 100, 0, 0, null, "Pendente", null, "Pendente", null);
        var erro = await Assert.ThrowsAsync<CrmBusinessException>(() => s.SalvarVendaAsync(null, manual, CancellationToken.None));
        Assert.Equal("financeiro_venda_sem_card", erro.Codigo);
    }

    private static async Task<CrmOpportunity> VendaGanhaAsync(TestDbContextFactory factory, ApplicationDbContext db, Guid vendedor, decimal adesao, decimal? rastreador = null, decimal? indicacao = null)
    {
        var ganho = await factory.CriarEtapaAsync(db, $"Ganho {Guid.NewGuid():N}", 9, TipoEtapaPipeline.Ganho);
        var lead = new CrmLead { NomeOuRazaoSocial = "Cliente do Card", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = vendedor };
        db.CrmLeads.Add(lead);
        await db.SaveChangesAsync();
        var op = new CrmOpportunity
        {
            LeadId = lead.Id, Titulo = "V", ResponsavelId = vendedor, EtapaId = ganho.Id, PagamentoAdesao = adesao,
            DataEfetivaFechamento = DateTimeOffset.UtcNow, Indicacao = indicacao is not null, ValorIndicacao = indicacao,
        };
        if (rastreador is not null) op.Veiculo = new CrmVeiculo { Rastreador = rastreador };
        db.CrmOpportunities.Add(op);
        await db.SaveChangesAsync();
        return op;
    }

    [Fact]
    public async Task Ano_TrazVendaECustosDoCard_SoDoProprioConsultor()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var eu = await factory.CriarUsuarioAsync(db, "Eu");
        var outro = await factory.CriarUsuarioAsync(db, "Outro");
        await VendaGanhaAsync(factory, db, eu.Id, 200m, rastreador: 100m, indicacao: 50m);
        await VendaGanhaAsync(factory, db, outro.Id, 999m, rastreador: 77m);

        var ano = await Servico(db, eu.Id).ObterAnoAsync(null, CancellationToken.None);

        var venda = Assert.Single(ano.Vendas);
        Assert.True(venda.DoCard);
        Assert.Equal(200m, venda.ValorAdesao);
        Assert.Equal("Cliente do Card", venda.Cliente);
        Assert.Equal(2, ano.Custos.Count);
        Assert.Contains(ano.Custos, c => c.Categoria == "Rastreador" && c.Valor == 100m && c.DoCard);
        Assert.Contains(ano.Custos, c => c.Categoria == "Indicação" && c.Valor == 50m && c.DoCard);
    }

    /// <summary>Venda fechada no quadro de leads com a adesão agendada para outro dia (sem comprovante): entra no controle como pendente, com a data combinada.</summary>
    private static async Task<CrmOpportunity> VendaComAdesaoAgendadaAsync(TestDbContextFactory factory, ApplicationDbContext db, Guid vendedor, decimal valorPagoNoFechamento, decimal? adesaoDaCotacao, DateOnly prevista, bool comComprovante = false)
    {
        var op = await VendaGanhaAsync(factory, db, vendedor, valorPagoNoFechamento);
        op.DataPagamentoAdesaoPrevista = prevista;
        if (comComprovante) op.PagamentoAdesaoArquivoUrl = "/uploads/comprovante.pdf";
        var lead = await db.CrmLeads.FirstAsync(l => l.Id == op.LeadId);
        lead.ValorAdesao = adesaoDaCotacao;
        await db.SaveChangesAsync();
        return op;
    }

    [Fact]
    public async Task Ano_AdesaoAgendadaParaOutroDia_VemPendenteComADataCombinada()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var eu = await factory.CriarUsuarioAsync(db, "Eu");
        var prevista = HorarioBrasilia.Hoje.AddDays(7);
        await VendaComAdesaoAgendadaAsync(factory, db, eu.Id, 1500m, 1500m, prevista);

        var venda = Assert.Single((await Servico(db, eu.Id).ObterAnoAsync(null, CancellationToken.None)).Vendas);

        Assert.Equal(1500m, venda.ValorAdesao);
        Assert.Equal(FinanceiroStatus.Pendente, venda.StatusAdesao);
        Assert.Equal(prevista, venda.DataPrevistaAdesao);
    }

    [Fact]
    public async Task Ano_AdesaoAgendada_ComValorZeradoNoFechamento_UsaAAdesaoDaCotacao_ParaNaoSumirDePendencias()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var eu = await factory.CriarUsuarioAsync(db, "Eu");
        await VendaComAdesaoAgendadaAsync(factory, db, eu.Id, 0m, 1800m, HorarioBrasilia.Hoje.AddDays(-3));

        var venda = Assert.Single((await Servico(db, eu.Id).ObterAnoAsync(null, CancellationToken.None)).Vendas);

        Assert.Equal(1800m, venda.ValorAdesao);
        Assert.Equal(FinanceiroStatus.Pendente, venda.StatusAdesao);
        Assert.Equal(HorarioBrasilia.Hoje.AddDays(-3), venda.DataPrevistaAdesao); // a tela marca "Em atraso" porque a data já passou
    }

    [Fact]
    public async Task Ano_AdesaoPaga_NaoUsaAAdesaoDaCotacao_NemFicaPendente()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var eu = await factory.CriarUsuarioAsync(db, "Eu");
        // Paga no dia, sem data combinada: vale o valor pago, mesmo que a cotação tivesse outro número.
        await VendaComAdesaoAgendadaAsync(factory, db, eu.Id, 900m, 1800m, default);
        var op = await db.CrmOpportunities.SingleAsync();
        op.DataPagamentoAdesaoPrevista = null;
        await db.SaveChangesAsync();

        var venda = Assert.Single((await Servico(db, eu.Id).ObterAnoAsync(null, CancellationToken.None)).Vendas);

        Assert.Equal(900m, venda.ValorAdesao);
        Assert.Equal(FinanceiroStatus.Recebido, venda.StatusAdesao);
    }

    [Fact]
    public async Task Ano_AdesaoAgendadaComComprovante_JaFoiPaga_ComValorZeradoNaoPuxaAdesaoDaCotacao()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var eu = await factory.CriarUsuarioAsync(db, "Eu");
        await VendaComAdesaoAgendadaAsync(factory, db, eu.Id, 0m, 1800m, HorarioBrasilia.Hoje.AddDays(2), comComprovante: true);

        var venda = Assert.Single((await Servico(db, eu.Id).ObterAnoAsync(null, CancellationToken.None)).Vendas);

        Assert.Equal(0m, venda.ValorAdesao);
        Assert.Equal(FinanceiroStatus.Recebido, venda.StatusAdesao);
    }

    [Fact]
    public async Task Venda_DoCard_ComplementoNaoAlteraAdesaoDoCard_NemDuplica()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var eu = await factory.CriarUsuarioAsync(db, "Eu");
        var op = await VendaGanhaAsync(factory, db, eu.Id, 200m);
        var s = Servico(db, eu.Id);

        var doCard = (await s.ObterAnoAsync(null, CancellationToken.None)).Vendas.Single();
        // A tela tenta "mudar" a adesão e o cliente: só o complemento (bônus) vale.
        var adulterada = doCard with { ValorAdesao = 999_999m, Cliente = "Outro nome", ValorBonus = 150m };
        await s.SalvarVendaAsync(null, adulterada, CancellationToken.None);
        await s.SalvarVendaAsync(null, adulterada with { ValorBonus = 120m }, CancellationToken.None);

        var depois = (await s.ObterAnoAsync(null, CancellationToken.None)).Vendas.Single();
        Assert.Equal(200m, depois.ValorAdesao);
        Assert.Equal("Cliente do Card", depois.Cliente);
        Assert.Equal(120m, depois.ValorBonus);
        Assert.Equal(op.Id, depois.OportunidadeId);
        Assert.Equal(1, db.CrmFinanceiroVendas.Count());
    }

    [Fact]
    public async Task Venda_DoCard_DeOutroConsultor_NaoPodeSerComplementada()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var dono = await factory.CriarUsuarioAsync(db, "Dono");
        var intruso = await factory.CriarUsuarioAsync(db, "Intruso");
        var op = await VendaGanhaAsync(factory, db, dono.Id, 300m);

        var ataque = new FinanceiroVendaDto(null, op.Id, true, HorarioBrasilia.Hoje, "x", "Adesão", 0, 500, 0, null, "Recebido", null, "Recebido", null);
        await Assert.ThrowsAsync<CrmNotFoundException>(() => Servico(db, intruso.Id).SalvarVendaAsync(null, ataque, CancellationToken.None));
    }

    [Fact]
    public async Task Venda_DoCard_NaoPodeSerExcluida()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var eu = await factory.CriarUsuarioAsync(db, "Eu");
        await VendaGanhaAsync(factory, db, eu.Id, 200m);
        var s = Servico(db, eu.Id);
        var doCard = (await s.ObterAnoAsync(null, CancellationToken.None)).Vendas.Single();
        var complementada = await s.SalvarVendaAsync(null, doCard with { ValorBonus = 10m }, CancellationToken.None);

        await Assert.ThrowsAsync<CrmBusinessException>(() => s.ExcluirVendaAsync(complementada.Id!.Value, CancellationToken.None));
    }

    [Fact]
    public async Task Metas_VindasDoCrm_ValemSobreAsDoFinanceiro()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var eu = await factory.CriarUsuarioAsync(db, "Eu");
        var mes = HorarioBrasilia.PrimeiroDiaDoMes(HorarioBrasilia.Hoje);
        db.CrmSalesGoals.Add(new CrmSalesGoal { VendedorId = eu.Id, MesReferencia = mes, MetaValor = 5000m, MetaQuantidadeVendas = 12 });
        await db.SaveChangesAsync();
        var s = Servico(db, eu.Id);

        await s.SalvarMetaAsync(mes.Year, new FinanceiroMetaDto(mes.Month, 1m, 800m, 300m, 1, "obs"), CancellationToken.None);
        var meta = (await s.ObterAnoAsync(mes.Year, CancellationToken.None)).Metas.Single();

        Assert.True(meta.DoCrm);
        Assert.Equal(5000m, meta.MetaAdesao);   // do CRM, não o 1 digitado
        Assert.Equal(12, meta.MetaVendas);      // do CRM
        Assert.Equal(800m, meta.MetaBonus);     // do consultor
        Assert.Equal(300m, meta.MetaRecorrencia);
    }
}

/// <summary>Visão da gestão no Controle financeiro: um consultor escolhido ou a equipe toda, sempre só leitura e dentro do escopo de quem consulta.</summary>
public class FinanceiroVisaoGestaoTests
{
    private static FinanceiroService Servico(ApplicationDbContext db, Mock<ICurrentUserService> eu) =>
        new(db, eu.Object, new NoOpAuditSink(), new EquipeComercialService(db, eu.Object));

    private static async Task<CrmOpportunity> VendaGanhaAsync(TestDbContextFactory factory, ApplicationDbContext db, Guid vendedor, string cliente, decimal adesao)
    {
        var ganho = await factory.CriarEtapaAsync(db, $"Ganho {Guid.NewGuid():N}", 9, TipoEtapaPipeline.Ganho);
        var lead = new CrmLead { NomeOuRazaoSocial = cliente, TipoPessoa = TipoPessoa.Fisica, ResponsavelId = vendedor };
        db.CrmLeads.Add(lead);
        await db.SaveChangesAsync();
        var op = new CrmOpportunity { LeadId = lead.Id, Titulo = "V", ResponsavelId = vendedor, EtapaId = ganho.Id, PagamentoAdesao = adesao, DataEfetivaFechamento = DateTimeOffset.UtcNow };
        db.CrmOpportunities.Add(op);
        await db.SaveChangesAsync();
        return op;
    }

    private static async Task<(Domain.Identity.ApplicationUser Ana, Domain.Identity.ApplicationUser Bia, Domain.Identity.ApplicationUser Admin)> CenarioAsync(TestDbContextFactory factory, ApplicationDbContext db)
    {
        await factory.SeedRolesAsync(db);
        var ana = await factory.CriarUsuarioAsync(db, "Ana");
        var bia = await factory.CriarUsuarioAsync(db, "Bia");
        var admin = await factory.CriarUsuarioAsync(db, "Admin");
        await factory.AtribuirPapelAsync(db, ana, Roles.Comercial);
        await factory.AtribuirPapelAsync(db, bia, Roles.Comercial);
        await factory.AtribuirPapelAsync(db, admin, Roles.Admin);
        await VendaGanhaAsync(factory, db, ana.Id, "Cliente da Ana", 200m);
        await VendaGanhaAsync(factory, db, bia.Id, "Cliente da Bia", 300m);
        return (ana, bia, admin);
    }

    [Fact]
    public async Task Consultor_NaoVeOsNumerosDeOutroConsultor_NemDaEquipe()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var (ana, bia, _) = await CenarioAsync(factory, db);
        var s = Servico(db, TestDbContextFactory.MockCurrentUser(ana.Id));

        await Assert.ThrowsAsync<CrmForbiddenException>(() => s.ObterAnoAsync(null, bia.Id.ToString(), CancellationToken.None));
        await Assert.ThrowsAsync<CrmForbiddenException>(() => s.ObterAnoAsync(null, "equipe", CancellationToken.None));

        var proprio = await s.ObterAnoAsync(null, null, CancellationToken.None);
        Assert.Equal("propria", proprio.Visao!.Tipo);
        Assert.False(proprio.Visao.SomenteLeitura);
        Assert.Equal("Cliente da Ana", Assert.Single(proprio.Vendas).Cliente);
    }

    [Fact]
    public async Task Admin_VeUmConsultorEscolhido_SoLeitura_ComONome()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var (_, bia, admin) = await CenarioAsync(factory, db);
        var s = Servico(db, TestDbContextFactory.MockCurrentUser(admin.Id, visaoTotal: true));

        var d = await s.ObterAnoAsync(null, bia.Id.ToString(), CancellationToken.None);

        Assert.Equal("consultor", d.Visao!.Tipo);
        Assert.True(d.Visao.SomenteLeitura);
        Assert.Equal("Bia", d.Visao.ConsultorNome);
        var venda = Assert.Single(d.Vendas);
        Assert.Equal("Cliente da Bia", venda.Cliente);
        Assert.Equal("Bia", venda.Consultor);
        Assert.Equal(bia.Id, venda.ConsultorId);
        Assert.Equal(300m, venda.ValorAdesao);
    }

    [Fact]
    public async Task Admin_VeAEquipeToda_Somada_ComONomeDeCadaConsultor()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var (ana, bia, admin) = await CenarioAsync(factory, db);
        admin.AtuaNasVendas = false; // administrador que não vende não entra na lista de consultores
        var mes = HorarioBrasilia.PrimeiroDiaDoMes(HorarioBrasilia.Hoje);
        db.CrmSalesGoals.AddRange(
            new CrmSalesGoal { VendedorId = ana.Id, MesReferencia = mes, MetaValor = 1000m, MetaQuantidadeVendas = 4 },
            new CrmSalesGoal { VendedorId = bia.Id, MesReferencia = mes, MetaValor = 2000m, MetaQuantidadeVendas = 6 });
        await db.SaveChangesAsync();
        var s = Servico(db, TestDbContextFactory.MockCurrentUser(admin.Id, visaoTotal: true));

        var d = await s.ObterAnoAsync(null, "equipe", CancellationToken.None);

        Assert.Equal("equipe", d.Visao!.Tipo);
        Assert.True(d.Visao.SomenteLeitura);
        Assert.Equal(2, d.Visao.Consultores); // Ana e Bia; o admin não atua nas vendas
        Assert.Equal(["Ana", "Bia"], d.Vendas.Select(v => v.Consultor).Order());
        Assert.Equal(new[] { ana.Id, bia.Id }.Order(), d.Vendas.Select(v => v.ConsultorId!.Value).Order()); // o id permite filtrar e abrir o relatório de cada um
        Assert.Equal(500m, d.Vendas.Sum(v => v.ValorAdesao));
        var meta = Assert.Single(d.Metas);
        Assert.Equal(3000m, meta.MetaAdesao); // metas somadas mês a mês
        Assert.Equal(10, meta.MetaVendas);
    }

    [Fact]
    public async Task ConsultorInvalidoOuInexistente_NaoVaza()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var (_, _, admin) = await CenarioAsync(factory, db);
        var s = Servico(db, TestDbContextFactory.MockCurrentUser(admin.Id, visaoTotal: true));

        await Assert.ThrowsAsync<CrmBusinessException>(() => s.ObterAnoAsync(null, "nao-e-um-id", CancellationToken.None));
        await Assert.ThrowsAsync<CrmNotFoundException>(() => s.ObterAnoAsync(null, Guid.NewGuid().ToString(), CancellationToken.None));
    }

    [Fact]
    public async Task GestorComercial_SoVeAPropriaEquipe()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        await factory.SeedRolesAsync(db);
        var gestor = await factory.CriarUsuarioAsync(db, "Gestor");
        var daEquipe = await factory.CriarUsuarioAsync(db, "DaEquipe", gestorId: gestor.Id);
        var deFora = await factory.CriarUsuarioAsync(db, "DeFora");
        await factory.AtribuirPapelAsync(db, gestor, Roles.GestorComercial);
        await factory.AtribuirPapelAsync(db, daEquipe, Roles.Comercial);
        await factory.AtribuirPapelAsync(db, deFora, Roles.Comercial);
        await VendaGanhaAsync(factory, db, daEquipe.Id, "Da equipe", 100m);
        await VendaGanhaAsync(factory, db, deFora.Id, "De fora", 900m);
        var s = Servico(db, TestDbContextFactory.MockCurrentUser(gestor.Id, gestorComercial: true, podeGerir: true));

        var equipe = await s.ObterAnoAsync(null, "equipe", CancellationToken.None);
        Assert.Equal("Da equipe", Assert.Single(equipe.Vendas).Cliente);
        await Assert.ThrowsAsync<CrmForbiddenException>(() => s.ObterAnoAsync(null, deFora.Id.ToString(), CancellationToken.None));
    }
}
