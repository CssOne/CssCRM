using System.Text.Json;
using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Authorization;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Contrato por grupo: o que vale em cada mês vem do consultor ou do grupo em que ele estava, valor por valor.</summary>
public class ContratoPorGrupoTests
{
    private static readonly Guid Ana = Guid.NewGuid();
    private static readonly Guid Internos = Guid.NewGuid();
    private static readonly Guid Externos = Guid.NewGuid();
    private static readonly DateOnly Hoje = new(2026, 10, 21);

    private static DateOnly M(int mes) => new(2026, mes, 1);

    private static CrmGrupoMembro Entrou(Guid? grupo, int mes) => new() { UsuarioId = Ana, GrupoId = grupo, DesdeMes = M(mes) };

    private static CrmGrupoContratoPerfil PerfilGrupo(Guid grupo, TipoContrato tipo, int desde = 1,
        ModeloRemuneracaoPj? modelo = ModeloRemuneracaoPj.BonusPorPlaca, ModalidadeTransporte transporte = ModalidadeTransporte.Nenhum) =>
        new() { GrupoId = grupo, Tipo = tipo, ModeloPj = tipo == TipoContrato.Pj ? modelo : null, Transporte = transporte, VigenteDesde = M(desde) };

    private static CrmGrupoContratoValor ValorGrupo(Guid grupo, ParametroFinanceiro p, decimal valor, int desde = 1) =>
        new() { GrupoId = grupo, Parametro = p, Valor = valor, VigenteDesde = M(desde) };

    private static CrmFinanceiroValor Proprio(ParametroFinanceiro p, decimal valor, int desde = 1) =>
        new() { UsuarioId = Ana, Parametro = p, Valor = valor, VigenteDesde = M(desde) };

    private static CrmFinanceiroPerfil PerfilProprio(TipoContrato tipo, int inicio = 1) =>
        new() { UsuarioId = Ana, Tipo = tipo, ModeloPj = tipo == TipoContrato.Pj ? ModeloRemuneracaoPj.BonusPorPlaca : null, InicioContrato = M(inicio) };

    private static IReadOnlyList<Web.Api.Contracts.Crm.FinanceiroMesContratoDto> Meses(ContratoEfetivo e, ContratoFinanceiroCalculo.Regras? regras = null) =>
        ContratoFinanceiroCalculo.Meses(e, 2026, Hoje, regras);

    [Fact]
    public void SemGrupo_ResultadoIdenticoAoContratoProprio()
    {
        var perfil = PerfilProprio(TipoContrato.Pj, 3);
        CrmFinanceiroValor[] valores = [Proprio(ParametroFinanceiro.BonusPorPlaca, 100m), Proprio(ParametroFinanceiro.Coworking, 150m, 4)];

        var antes = ContratoFinanceiroCalculo.Meses(perfil, valores, 2026, Hoje);
        var depois = Meses(new ContratoEfetivo(perfil, valores));

        Assert.Equal(JsonSerializer.Serialize(antes), JsonSerializer.Serialize(depois));
        Assert.False(new ContratoEfetivo(perfil, valores).TemGrupo);
    }

    [Fact]
    public void SemGrupoENemContrato_NaoGeraNada() => Assert.Empty(Meses(new ContratoEfetivo(null, [])));

    [Fact]
    public void GrupoInterno_CobraCoworkingDesdeAEntradaNoGrupo()
    {
        var e = new ContratoEfetivo(null, [], [Entrou(Internos, 3)],
            [PerfilGrupo(Internos, TipoContrato.Pj)],
            [ValorGrupo(Internos, ParametroFinanceiro.Coworking, 100m), ValorGrupo(Internos, ParametroFinanceiro.ImpostoMei, 0m)]);

        var meses = Meses(e);

        Assert.Empty(meses[1].Custos);                                                     // fev: ainda não estava no grupo
        Assert.Equal(100m, meses[2].Custos.Single(c => c.Categoria == "Coworking").Valor);  // mar: entrou
        Assert.Equal(100m, meses[9].Custos.Single(c => c.Categoria == "Coworking").Valor);  // out
        Assert.Empty(meses[10].Custos);                                                    // nov: ainda não aconteceu
    }

    [Fact]
    public void GrupoSemCoworking_NaoCobra()
    {
        var e = new ContratoEfetivo(null, [], [Entrou(Externos, 1)], [PerfilGrupo(Externos, TipoContrato.Pj)],
            [ValorGrupo(Externos, ParametroFinanceiro.Coworking, 0m), ValorGrupo(Externos, ParametroFinanceiro.ImpostoMei, 0m)]);

        Assert.DoesNotContain(Meses(e)[5].Custos, c => c.Categoria == "Coworking");
    }

    [Fact]
    public void GrupoClt_TodosTemOMesmoContrato_MasOSalarioEIndividual()
    {
        var perfis = new[] { PerfilGrupo(Internos, TipoContrato.Clt) };
        var valoresDoGrupo = new[]
        {
            ValorGrupo(Internos, ParametroFinanceiro.Alimentacao, 600m), ValorGrupo(Internos, ParametroFinanceiro.Passagem, 220m),
            ValorGrupo(Internos, ParametroFinanceiro.Salario, 9999m), // o grupo nunca define salário
        };
        var ana = Meses(new ContratoEfetivo(null, [Proprio(ParametroFinanceiro.Salario, 2200m)], [Entrou(Internos, 1)], perfis, valoresDoGrupo));
        var bia = Meses(new ContratoEfetivo(null, [Proprio(ParametroFinanceiro.Salario, 3100m)], [Entrou(Internos, 1)], perfis, valoresDoGrupo));

        Assert.Equal(2200m, ana[5].Salario);
        Assert.Equal(3100m, bia[5].Salario);
        Assert.All(new[] { ana[5], bia[5] }, m =>
        {
            Assert.Equal(600m, m.Alimentacao);
            Assert.Equal(220m, m.Passagem);
            Assert.Empty(m.Custos);
        });
    }

    [Fact]
    public void ClteSemSalarioProprio_NaoRecebeSalarioDoGrupo()
    {
        var e = new ContratoEfetivo(null, [], [Entrou(Internos, 1)], [PerfilGrupo(Internos, TipoContrato.Clt)],
            [ValorGrupo(Internos, ParametroFinanceiro.Salario, 5000m)]);

        Assert.Equal(0m, Meses(e)[5].Salario);
    }

    [Fact]
    public void ExcecaoIndividual_VenceOValorDoGrupo()
    {
        var e = new ContratoEfetivo(null, [Proprio(ParametroFinanceiro.Coworking, 150m)], [Entrou(Internos, 1)],
            [PerfilGrupo(Internos, TipoContrato.Pj)], [ValorGrupo(Internos, ParametroFinanceiro.Coworking, 100m)]);

        Assert.Equal(150m, Meses(e)[5].Custos.Single(c => c.Categoria == "Coworking").Valor);
    }

    [Fact]
    public void ValorDoGrupoMudaAPartirDoMes_SemReescreverOPassado()
    {
        var e = new ContratoEfetivo(null, [], [Entrou(Internos, 1)], [PerfilGrupo(Internos, TipoContrato.Pj)],
            [ValorGrupo(Internos, ParametroFinanceiro.Coworking, 100m), ValorGrupo(Internos, ParametroFinanceiro.Coworking, 120m, desde: 7)]);

        var meses = Meses(e);

        Assert.Equal(100m, meses[5].Custos.Single(c => c.Categoria == "Coworking").Valor); // jun
        Assert.Equal(120m, meses[6].Custos.Single(c => c.Categoria == "Coworking").Valor); // jul em diante
    }

    [Fact]
    public void TrocaDeGrupo_CadaMesUsaOGrupoDaquelePeriodo()
    {
        var e = new ContratoEfetivo(null, [Proprio(ParametroFinanceiro.Salario, 2200m)],
            [Entrou(Internos, 4), Entrou(Externos, 7)],
            [PerfilGrupo(Internos, TipoContrato.Pj), PerfilGrupo(Externos, TipoContrato.Clt)],
            [ValorGrupo(Internos, ParametroFinanceiro.Coworking, 100m), ValorGrupo(Externos, ParametroFinanceiro.Alimentacao, 600m)]);

        var meses = Meses(e);

        Assert.Empty(meses[2].Custos);                                                      // mar: antes de qualquer grupo
        Assert.Equal(100m, meses[3].Custos.Single(c => c.Categoria == "Coworking").Valor);  // abr: grupo interno (PJ)
        Assert.Equal(100m, meses[5].Custos.Single(c => c.Categoria == "Coworking").Valor);  // jun: ainda interno
        Assert.Equal(0m, meses[5].Alimentacao);
        Assert.Empty(meses[6].Custos);                                                      // jul: grupo externo (CLT), sem custos fixos
        Assert.Equal(600m, meses[6].Alimentacao);
        Assert.Equal(2200m, meses[6].Salario);
    }

    [Fact]
    public void SairDoGrupo_PassaASerCobradoSemOContratoDele()
    {
        var e = new ContratoEfetivo(PerfilProprio(TipoContrato.Pj), [], [Entrou(Internos, 1), Entrou(null, 6)],
            [PerfilGrupo(Internos, TipoContrato.Pj)], [ValorGrupo(Internos, ParametroFinanceiro.Coworking, 300m)]);

        var meses = Meses(e);

        Assert.Equal(300m, meses[4].Custos.Single(c => c.Categoria == "Coworking").Valor); // mai, no grupo
        Assert.Equal(100m, meses[5].Custos.Single(c => c.Categoria == "Coworking").Valor); // jun: saiu, volta ao padrão do sistema
    }

    [Fact]
    public void ContratoProprioDoConsultor_VenceOTipoDoGrupo()
    {
        var e = new ContratoEfetivo(PerfilProprio(TipoContrato.Clt), [Proprio(ParametroFinanceiro.Salario, 2000m)], [Entrou(Internos, 1)],
            [PerfilGrupo(Internos, TipoContrato.Pj)], [ValorGrupo(Internos, ParametroFinanceiro.Coworking, 100m)]);

        var mes = Meses(e)[5];

        Assert.Equal(2000m, mes.Salario);
        Assert.Empty(mes.Custos);
    }

    [Fact]
    public void CarenciaDoGrupo_ContaDaEntradaNoGrupo()
    {
        var e = new ContratoEfetivo(null, [], [Entrou(Internos, 3)], [PerfilGrupo(Internos, TipoContrato.Pj)],
            [ValorGrupo(Internos, ParametroFinanceiro.TrafegoPago, 300m)],
            [new CrmGrupoContratoPrazo { GrupoId = Internos, Parametro = ParametroFinanceiro.TrafegoPago, CarenciaMeses = 2 }]);

        var meses = Meses(e);

        Assert.DoesNotContain(meses[3].Custos, c => c.Chave == "TrafegoPago"); // abr (1 mês depois)
        Assert.Equal(300m, meses[4].Custos.Single(c => c.Chave == "TrafegoPago").Valor); // mai (2 meses depois da entrada)
    }

    [Fact]
    public void CarenciaPropriaDoConsultor_VenceACarenciaDoGrupo()
    {
        var prazoProprio = new Dictionary<ParametroFinanceiro, CrmFinanceiroPrazo>
        {
            [ParametroFinanceiro.TrafegoPago] = new() { UsuarioId = Ana, Parametro = ParametroFinanceiro.TrafegoPago, CarenciaMeses = 0 },
        };
        var e = new ContratoEfetivo(null, [], [Entrou(Internos, 3)], [PerfilGrupo(Internos, TipoContrato.Pj)],
            [ValorGrupo(Internos, ParametroFinanceiro.TrafegoPago, 300m)],
            [new CrmGrupoContratoPrazo { GrupoId = Internos, Parametro = ParametroFinanceiro.TrafegoPago, CarenciaMeses = 2 }], prazoProprio);

        Assert.Equal(300m, Meses(e)[2].Custos.Single(c => c.Chave == "TrafegoPago").Valor); // mar, sem carência
    }

    [Fact]
    public void GrupoSemTipoDefinido_NaoCriaContrato()
    {
        var e = new ContratoEfetivo(null, [], [Entrou(Internos, 1)], [], [ValorGrupo(Internos, ParametroFinanceiro.Coworking, 100m)]);

        Assert.Empty(Meses(e));
        Assert.Null(e.PerfilDoAno(2026, Hoje));
    }

    [Fact]
    public void PerfilNoMes_InicioDoContratoHerdadoContaDaEntradaNoGrupo()
    {
        var e = new ContratoEfetivo(null, [], [Entrou(Internos, 5)], [PerfilGrupo(Internos, TipoContrato.Pj)]);

        Assert.Equal(M(5), e.PerfilNoMes(M(8))!.InicioContrato);
        Assert.Null(e.PerfilNoMes(M(3)));
    }
}

/// <summary>O histórico de grupo é gravado sozinho sempre que o grupo do usuário muda, por qualquer caminho.</summary>
public class HistoricoDeGrupoTests
{
    private static readonly DateOnly MesAtual = new(HorarioBrasilia.Hoje.Year, HorarioBrasilia.Hoje.Month, 1);

    private static DateOnly M(int mes) => new(2026, mes, 1);

    [Fact]
    public async Task UsuarioCriadoNoGrupo_GanhaLinhaNoHistorico_ETrocaCriaOutra()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        await factory.SeedRolesAsync(db);
        var regional = await factory.CriarRegionalAsync(db, "MG");
        var a = await factory.CriarGrupoAsync(db, regional.Id, "Internos");
        var b = await factory.CriarGrupoAsync(db, regional.Id, "Externos");
        var ana = await factory.CriarUsuarioAsync(db, "Ana");

        ana.GrupoId = a.Id;
        await db.SaveChangesAsync();
        var linhas = await db.CrmGrupoMembros.Where(m => m.UsuarioId == ana.Id).ToListAsync();
        Assert.Equal(a.Id, Assert.Single(linhas).GrupoId);
        Assert.Equal(MesAtual, linhas[0].DesdeMes);

        // Troca no mesmo mês: a linha do mês é ajustada, sem criar outra (índice único usuário + mês).
        ana.GrupoId = b.Id;
        await db.SaveChangesAsync();
        Assert.Equal(b.Id, Assert.Single(await db.CrmGrupoMembros.Where(m => m.UsuarioId == ana.Id).ToListAsync()).GrupoId);

        // Troca depois de uma linha mais antiga: o histórico guarda as duas.
        var antiga = await db.CrmGrupoMembros.SingleAsync(m => m.UsuarioId == ana.Id);
        antiga.DesdeMes = MesAtual.AddMonths(-3);
        await db.SaveChangesAsync();
        ana.GrupoId = a.Id;
        await db.SaveChangesAsync();
        var todas = await db.CrmGrupoMembros.Where(m => m.UsuarioId == ana.Id).OrderBy(m => m.DesdeMes).ToListAsync();
        Assert.Equal([b.Id, a.Id], todas.Select(m => m.GrupoId!.Value));

        // Sair do grupo também fica registrado.
        ana.GrupoId = null;
        await db.SaveChangesAsync();
        var ultima = await db.CrmGrupoMembros.Where(m => m.UsuarioId == ana.Id).OrderByDescending(m => m.DesdeMes).FirstAsync();
        Assert.Null(ultima.GrupoId);
    }

    [Fact]
    public async Task SalvarSemMudarOGrupo_NaoCriaHistorico()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var ana = await factory.CriarUsuarioAsync(db, "Ana");

        ana.NomeCompleto = "Ana Souza";
        await db.SaveChangesAsync();

        Assert.Empty(await db.CrmGrupoMembros.ToListAsync());
    }

    [Fact]
    public async Task ConsultorEmGrupoClt_HerdaOContratoDoGrupo_ESalarioEIndividual()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        await factory.SeedRolesAsync(db);
        var regional = await factory.CriarRegionalAsync(db, "MG");
        var grupo = await factory.CriarGrupoAsync(db, regional.Id, "CLT Interno");
        var ana = await factory.CriarUsuarioAsync(db, "Ana");
        var admin = await factory.CriarUsuarioAsync(db, "Admin");
        await factory.AtribuirPapelAsync(db, ana, Roles.Comercial);
        await factory.AtribuirPapelAsync(db, admin, Roles.Admin);
        db.CrmGrupoContratoPerfis.Add(new CrmGrupoContratoPerfil { GrupoId = grupo.Id, Tipo = TipoContrato.Clt, VigenteDesde = new DateOnly(2020, 1, 1) });
        db.CrmGrupoContratoValores.Add(new CrmGrupoContratoValor { GrupoId = grupo.Id, Parametro = ParametroFinanceiro.Alimentacao, Valor = 600m, VigenteDesde = new DateOnly(2020, 1, 1) });
        await db.SaveChangesAsync();

        ana.GrupoId = grupo.Id; // o histórico é gravado sozinho, desde o mês atual
        await db.SaveChangesAsync();

        var mock = TestDbContextFactory.MockCurrentUser(admin.Id, visaoTotal: true);
        mock.Setup(m => m.IsInRole(Roles.Admin)).Returns(true);
        var contrato = new FinanceiroContratoService(db, mock.Object, new NoOpAuditSink(), new EquipeComercialService(db, mock.Object));

        // Sem nenhum perfil próprio, o salário individual já pode ser definido: o tipo (CLT) vem do grupo.
        await contrato.DefinirValorAsync(ana.Id, new FinanceiroValorRequest("Salario", MesAtual, 2400m, null), CancellationToken.None);
        var dados = await contrato.ObterAsync(ana.Id, null, CancellationToken.None);

        Assert.Equal("Clt", dados.Perfil.Tipo);
        var mes = dados.Meses[MesAtual.Month - 1];
        Assert.Equal(2400m, mes.Salario);
        Assert.Equal(600m, mes.Alimentacao);
        Assert.Equal(600m, dados.Atuais.Single(a => a.Parametro == "Alimentacao").Valor);
        Assert.Empty(mes.Custos);
    }

    private static Moq.Mock<ICurrentUserService> Como(Guid id, params string[] papeis)
    {
        var mock = TestDbContextFactory.MockCurrentUser(id, visaoTotal: papeis.Contains(Roles.Admin));
        foreach (var p in papeis) mock.Setup(m => m.IsInRole(p)).Returns(true);
        return mock;
    }

    [Fact]
    public async Task ContratoDoGrupo_AdminDefine_FinanceiroSoConsulta_SalarioNaoEDoGrupo()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        await factory.SeedRolesAsync(db);
        var regional = await factory.CriarRegionalAsync(db, "MG");
        var grupo = await factory.CriarGrupoAsync(db, regional.Id, "Internos");
        var admin = await factory.CriarUsuarioAsync(db, "Admin");
        var fin = await factory.CriarUsuarioAsync(db, "Fin");
        await factory.AtribuirPapelAsync(db, admin, Roles.Admin);
        await factory.AtribuirPapelAsync(db, fin, Roles.Financeiro);
        fin.RegionalId = regional.Id; // quem não tem visão total só enxerga os grupos da própria regional
        await db.SaveChangesAsync();
        var comoAdmin = new ContratoGrupoService(db, Como(admin.Id, Roles.Admin).Object, new NoOpAuditSink());
        var comoFin = new ContratoGrupoService(db, Como(fin.Id, Roles.Financeiro).Object, new NoOpAuditSink());
        var ct = CancellationToken.None;

        await comoAdmin.SalvarPerfilAsync(grupo.Id, new GrupoPerfilRequest(M(1), "Pj", "BonusPorPlaca", null), ct);
        await comoAdmin.DefinirValorAsync(grupo.Id, new GrupoValorRequest("Coworking", M(1), 100m, null), ct);
        await comoAdmin.DefinirPrazoAsync(grupo.Id, new GrupoPrazoRequest("TrafegoPago", 2), ct);

        await Assert.ThrowsAsync<CrmBusinessException>(() => comoAdmin.DefinirValorAsync(grupo.Id, new GrupoValorRequest("Salario", M(1), 2000m, null), ct));
        await Assert.ThrowsAsync<CrmBusinessException>(() => comoAdmin.DefinirValorAsync(grupo.Id, new GrupoValorRequest("Alimentacao", M(1), 600m, null), ct)); // alimentação é do CLT
        await Assert.ThrowsAsync<CrmForbiddenException>(() => comoFin.DefinirValorAsync(grupo.Id, new GrupoValorRequest("Coworking", M(1), 1m, null), ct));

        var dados = await comoFin.ObterAsync(grupo.Id, ct); // quem só consulta enxerga
        Assert.False(dados.PodeEditar);
        Assert.Equal("Pj", dados.Perfil!.Tipo);
        Assert.Equal(100m, dados.Atuais.Single(a => a.Parametro == "Coworking").Valor);
        Assert.Equal(2, dados.Prazos.Single(p => p.Parametro == "TrafegoPago").CarenciaMeses);
        Assert.Single(await comoFin.ListarAsync(ct));

        // Grupo passa a CLT a partir de outro mês: o histórico guarda os dois contratos.
        await comoAdmin.SalvarPerfilAsync(grupo.Id, new GrupoPerfilRequest(M(7), "Clt", null, null), ct);
        Assert.Equal(2, (await comoAdmin.ObterAsync(grupo.Id, ct)).HistoricoPerfil.Count);
    }

    [Fact]
    public async Task ContratoDoConsultor_MostraDeOndeVemCadaValor_EOHistoricoDeGrupos()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        await factory.SeedRolesAsync(db);
        var regional = await factory.CriarRegionalAsync(db, "MG");
        var grupo = await factory.CriarGrupoAsync(db, regional.Id, "CLT Interno");
        var ana = await factory.CriarUsuarioAsync(db, "Ana");
        var admin = await factory.CriarUsuarioAsync(db, "Admin");
        await factory.AtribuirPapelAsync(db, ana, Roles.Comercial);
        await factory.AtribuirPapelAsync(db, admin, Roles.Admin);
        var comoAdmin = new ContratoGrupoService(db, Como(admin.Id, Roles.Admin).Object, new NoOpAuditSink());
        await comoAdmin.SalvarPerfilAsync(grupo.Id, new GrupoPerfilRequest(new DateOnly(2020, 1, 1), "Clt", null, null), CancellationToken.None);
        await comoAdmin.DefinirValorAsync(grupo.Id, new GrupoValorRequest("Alimentacao", new DateOnly(2020, 1, 1), 600m, null), CancellationToken.None);
        await comoAdmin.DefinirValorAsync(grupo.Id, new GrupoValorRequest("Passagem", new DateOnly(2020, 1, 1), 220m, null), CancellationToken.None);
        ana.GrupoId = grupo.Id;
        await db.SaveChangesAsync();

        var mock = Como(admin.Id, Roles.Admin);
        var contrato = new FinanceiroContratoService(db, mock.Object, new NoOpAuditSink(), new EquipeComercialService(db, mock.Object));
        var mesAtual = new DateOnly(HorarioBrasilia.Hoje.Year, HorarioBrasilia.Hoje.Month, 1);
        await contrato.DefinirValorAsync(ana.Id, new FinanceiroValorRequest("Salario", mesAtual, 2400m, null), CancellationToken.None);
        await contrato.DefinirValorAsync(ana.Id, new FinanceiroValorRequest("Passagem", mesAtual, 300m, null), CancellationToken.None); // exceção individual

        var d = await contrato.ObterAsync(ana.Id, null, CancellationToken.None);

        Assert.Equal("Grupo", d.Perfil.Origem);
        Assert.Equal("CLT Interno", d.Perfil.Grupo);
        var atuais = d.Atuais.ToDictionary(a => a.Parametro);
        Assert.Equal(("Individual", null), (atuais["Salario"].Origem, atuais["Salario"].Grupo));
        Assert.Equal(("Grupo", "CLT Interno"), (atuais["Alimentacao"].Origem, atuais["Alimentacao"].Grupo));
        Assert.Equal(300m, atuais["Passagem"].Valor);
        Assert.Equal("Individual", atuais["Passagem"].Origem);
        var periodo = Assert.Single(d.Grupos!);
        Assert.Equal("CLT Interno", periodo.Grupo);
        Assert.Null(periodo.Ate);
    }

    [Fact]
    public async Task ConsultorSemGrupo_ContratoProprioContinuaIgual()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        await factory.SeedRolesAsync(db);
        var ana = await factory.CriarUsuarioAsync(db, "Ana");
        await factory.AtribuirPapelAsync(db, ana, Roles.Comercial);
        db.CrmFinanceiroPerfis.Add(new CrmFinanceiroPerfil { UsuarioId = ana.Id, Tipo = TipoContrato.Clt, InicioContrato = new DateOnly(2026, 1, 1) });
        db.CrmFinanceiroValores.Add(new CrmFinanceiroValor { UsuarioId = ana.Id, Parametro = ParametroFinanceiro.Salario, VigenteDesde = new DateOnly(2026, 1, 1), Valor = 2200m });
        await db.SaveChangesAsync();

        var efetivo = await ContratoEfetivoCarregador.CarregarAsync(db, ana.Id, CancellationToken.None);

        Assert.False(efetivo.TemGrupo);
        Assert.Equal(TipoContrato.Clt, efetivo.PerfilDoAno(2026, new DateOnly(2026, 10, 21))!.Tipo);
        Assert.Equal(2200m, ContratoFinanceiroCalculo.Meses(efetivo, 2026, new DateOnly(2026, 10, 21))[5].Salario);
    }
}
