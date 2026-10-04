using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Authorization;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Dias e horário de recebimento de leads por consultor e o Relatório comercial.</summary>
public class JanelaRecebimentoELRelatorioTests
{
    private sealed class RelogioFixo(DateTimeOffset agora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => agora;
    }

    // 2026-10-05 é segunda-feira. 15:00 UTC = 12:00 em Brasília.
    private static readonly DateTimeOffset SegundaMeioDia = new(2026, 10, 5, 15, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("09:00", "18:00", null, true)]            // dentro do horário
    [InlineData("13:00", "18:00", null, false)]           // ainda não começou
    [InlineData("08:00", "12:00", null, false)]           // o fim é exclusivo
    [InlineData("22:00", "13:00", null, true)]            // atravessa a meia-noite
    [InlineData(null, null, 0b0111110, true)]             // segunda a sexta
    [InlineData(null, null, 0b1000001, false)]            // só fim de semana
    [InlineData("09:00", "18:00", 0b0111110, true)]
    [InlineData("09:00", "18:00", 0b1000001, false)]
    public void Permite_RespeitaHorarioEDiasDeBrasilia(string? inicio, string? fim, int? dias, bool esperado)
    {
        var i = inicio is null ? (TimeOnly?)null : TimeOnly.Parse(inicio);
        var f = fim is null ? (TimeOnly?)null : TimeOnly.Parse(fim);
        Assert.Equal(esperado, JanelaRecebimentoLeads.Permite(i, f, dias, SegundaMeioDia));
    }

    [Fact]
    public void Permite_UsaODiaDeBrasilia_NaoODoUtc()
    {
        // 01:00 UTC de terça = 22:00 de segunda em Brasília.
        var segundaNoite = new DateTimeOffset(2026, 10, 6, 1, 0, 0, TimeSpan.Zero);
        Assert.True(JanelaRecebimentoLeads.Permite(null, null, 1 << (int)DayOfWeek.Monday, segundaNoite));
        Assert.False(JanelaRecebimentoLeads.Permite(null, null, 1 << (int)DayOfWeek.Tuesday, segundaNoite));
    }

    [Fact]
    public void Dias_TodosOuNenhumViramSemRestricao()
    {
        Assert.Null(JanelaRecebimentoLeads.DiasParaMascara([0, 1, 2, 3, 4, 5, 6]));
        Assert.Null(JanelaRecebimentoLeads.DiasParaMascara([]));
        var util = JanelaRecebimentoLeads.DiasParaMascara([1, 2, 3, 4, 5]);
        Assert.Equal([1, 2, 3, 4, 5], JanelaRecebimentoLeads.MascaraParaDias(util)!);
    }

    [Fact]
    public async Task Rodizio_PulaConsultorForaDoHorarioEDoDia()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var ana = await factory.CriarUsuarioAsync(db, "Ana Vendedora");
        var bruna = await factory.CriarUsuarioAsync(db, "Bruna Vendedora");
        await factory.AtribuirPapelAsync(db, ana, Roles.Comercial);
        await factory.AtribuirPapelAsync(db, bruna, Roles.Comercial);
        var service = new LeadAssignmentService(db, relogio: new RelogioFixo(SegundaMeioDia));

        // Ana (primeira por nome) só trabalha à tarde: ao meio-dia a vez é da Bruna.
        ana.HorarioInicioLeads = new TimeOnly(13, 0);
        ana.HorarioFimLeads = new TimeOnly(18, 0);
        await db.SaveChangesAsync();
        Assert.False(await service.PodeReceberAsync(ana.Id, CancellationToken.None));
        Assert.Equal(bruna.Id, await service.ProximoResponsavelAsync(null, CancellationToken.None));

        // Sem restrição de horário, mas só terça a sexta: segunda também fica de fora.
        ana.HorarioInicioLeads = null;
        ana.HorarioFimLeads = null;
        ana.DiasSemanaLeads = JanelaRecebimentoLeads.DiasParaMascara([2, 3, 4, 5]);
        await db.SaveChangesAsync();
        Assert.Equal(bruna.Id, await service.ProximoResponsavelAsync(null, CancellationToken.None));

        ana.DiasSemanaLeads = null;
        await db.SaveChangesAsync();
        Assert.Equal(ana.Id, await service.ProximoResponsavelAsync(null, CancellationToken.None));
    }

    [Fact]
    public async Task Gestao_SalvaEDevolveAJanela_ERejeitaFaixaIncompleta()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var admin = await factory.CriarUsuarioAsync(db, "Admin");
        var ana = await factory.CriarUsuarioAsync(db, "Ana");
        await factory.AtribuirPapelAsync(db, ana, Roles.Comercial);
        var usuario = TestDbContextFactory.MockCurrentUser(admin.Id, visaoTotal: true, podeGerir: true).Object;
        var gestao = new ManagementService(db, usuario, new EquipeComercialService(db, usuario), TestDbContextFactory.CreateUserManager(db));

        await Assert.ThrowsAsync<CrmBusinessException>(() => gestao.AtualizarJanelaRecebimentoAsync(
            ana.Id, new AtualizarJanelaRecebimentoRequest("09:00", null, null), CancellationToken.None));

        await gestao.AtualizarJanelaRecebimentoAsync(ana.Id, new AtualizarJanelaRecebimentoRequest("09:00", "18:30", [1, 2, 3, 4, 5]), CancellationToken.None);
        var vendedor = Assert.Single(await gestao.ObterVendedoresAsync(CancellationToken.None), v => v.Id == ana.Id);
        Assert.Equal("09:00", vendedor.HorarioInicioLeads);
        Assert.Equal("18:30", vendedor.HorarioFimLeads);
        Assert.Equal([1, 2, 3, 4, 5], vendedor.DiasSemanaLeads!);

        await gestao.AtualizarJanelaRecebimentoAsync(ana.Id, new AtualizarJanelaRecebimentoRequest(null, null, null), CancellationToken.None);
        vendedor = Assert.Single(await gestao.ObterVendedoresAsync(CancellationToken.None), v => v.Id == ana.Id);
        Assert.Null(vendedor.HorarioInicioLeads);
        Assert.Null(vendedor.DiasSemanaLeads);
    }

    [Fact]
    public async Task Relatorio_SomaVendasLeadsEOrigemDoPeriodo()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var admin = await factory.CriarUsuarioAsync(db, "Admin");
        var ana = await factory.CriarUsuarioAsync(db, "Ana");
        await factory.AtribuirPapelAsync(db, ana, Roles.Comercial);
        var ganho = await factory.CriarEtapaAsync(db, "Ganho", 9, TipoEtapaPipeline.Ganho);

        CrmLead Lead(string nome, string origem, string estado) => new()
        {
            NomeOuRazaoSocial = nome, TipoPessoa = TipoPessoa.Fisica, ResponsavelId = ana.Id, Origem = origem, Estado = estado,
        };
        var venda = Lead("Cliente Venda", "Lookalike", "MG");
        var semVenda = Lead("Cliente Sem Venda", "Lookalike", "SP");
        var arquivado = Lead("Arquivado", "Pmax", "MG");
        arquivado.Arquivado = true;
        db.CrmLeads.AddRange(venda, semVenda, arquivado);
        await db.SaveChangesAsync();
        foreach (var l in new[] { venda, semVenda, arquivado }) l.CriadoEm = new DateTimeOffset(2025, 3, 10, 15, 0, 0, TimeSpan.Zero);
        db.CrmOpportunities.Add(new CrmOpportunity
        {
            LeadId = venda.Id, Titulo = "AGV", ResponsavelId = ana.Id, EtapaId = ganho.Id, ProdutoOuServico = "AGV",
            DataEfetivaFechamento = new DateTimeOffset(2025, 3, 12, 15, 0, 0, TimeSpan.Zero),
            PagamentoAdesao = 300m, Mensalidade = 150m, Estado = "MG",
            Veiculo = new CrmVeiculo { Fipe = 40_000m, Rastreador = 50m, ValorVistoria = 20m },
        });
        await db.SaveChangesAsync();

        var usuario = TestDbContextFactory.MockCurrentUser(admin.Id, visaoTotal: true, podeGerir: true).Object;
        var relatorio = new RelatorioComercialService(db, usuario, new EquipeComercialService(db, usuario));

        var r = await relatorio.ObterAsync(new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31), CancellationToken.None);

        Assert.Equal(2, r.Totais.Leads); // o arquivado não conta
        Assert.Equal(1, r.Totais.Vendas);
        Assert.Equal(50m, r.Totais.TaxaConversao);
        Assert.Equal(300m, r.Totais.Adesao);
        Assert.Equal(150m, r.Totais.Mensalidade);
        Assert.Equal(50m, r.Totais.Rastreador);
        Assert.Equal(20m, r.Totais.Vistoria);
        var mes = Assert.Single(r.PorMes);
        Assert.Equal("2025-03", mes.Mes);
        Assert.Equal(2, Assert.Single(r.PorOrigem).Leads);
        Assert.Equal("Ana", Assert.Single(r.PorVendedor).Vendedor);
        Assert.Equal(1, r.PorEstado.Single(e => e.Estado == "MG").Vendas);
        Assert.Equal(1, r.FaixasFipe.Single(f => f.Faixa == "R$ 30 a 50 mil").Vendas);

        // Fora do período: nada.
        var vazio = await relatorio.ObterAsync(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), CancellationToken.None);
        Assert.Equal(0, vazio.Totais.Leads);
        Assert.Equal(0, vazio.Totais.Vendas);

        // Sem permissão de gestão: negado.
        var semPermissao = TestDbContextFactory.MockCurrentUser(ana.Id, visaoTotal: false, podeGerir: false).Object;
        var negado = new RelatorioComercialService(db, semPermissao, new EquipeComercialService(db, semPermissao));
        await Assert.ThrowsAsync<CrmForbiddenException>(() => negado.ObterAsync(null, null, CancellationToken.None));
    }
}
