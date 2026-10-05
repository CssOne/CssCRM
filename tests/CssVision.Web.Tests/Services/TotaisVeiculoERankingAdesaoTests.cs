using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Rodapé da lista de leads, veículo não atendido, tráfego do mês anterior e adesão no ranking.</summary>
public class TotaisVeiculoERankingAdesaoTests
{
    private static LeadService Leads(ApplicationDbContext db, Guid usuarioId)
    {
        var usuario = TestDbContextFactory.MockCurrentUser(usuarioId, visaoTotal: true, podeGerir: true).Object;
        return new LeadService(db, usuario, new EquipeComercialService(db, usuario), new NoOpLeadAssignmentService(), new NoOpMetaConversionService(), new NoOpAuditSink());
    }

    private static ManagementService Gestao(ApplicationDbContext db, Guid usuarioId)
    {
        var usuario = TestDbContextFactory.MockCurrentUser(usuarioId, visaoTotal: true, podeGerir: true).Object;
        return new ManagementService(db, usuario, new EquipeComercialService(db, usuario), TestDbContextFactory.CreateUserManager(db));
    }

    [Fact]
    public async Task Totais_SomamTodasAsVendasDosLeadsFiltrados_ERespeitamOsFiltros()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var admin = await factory.CriarUsuarioAsync(db, "Admin");
        var ana = await factory.CriarUsuarioAsync(db, "Ana");
        var bruna = await factory.CriarUsuarioAsync(db, "Bruna");
        var ganho = await factory.CriarEtapaAsync(db, "Ganho", 9, TipoEtapaPipeline.Ganho);
        CrmLead L(string nome, Guid resp) => new() { NomeOuRazaoSocial = nome, TipoPessoa = TipoPessoa.Fisica, ResponsavelId = resp };
        var l1 = L("Da Ana 1", ana.Id);
        var l2 = L("Da Ana 2", ana.Id);
        var l3 = L("Da Bruna", bruna.Id);
        var semVenda = L("Da Ana sem venda", ana.Id);
        db.CrmLeads.AddRange(l1, l2, l3, semVenda);
        await db.SaveChangesAsync();
        CrmOpportunity Venda(CrmLead lead, Guid resp, decimal adesao, decimal mensalidade, decimal fipe, decimal total) => new()
        {
            LeadId = lead.Id, Titulo = "V", ResponsavelId = resp, EtapaId = ganho.Id, PagamentoAdesao = adesao, Mensalidade = mensalidade,
            MensalidadeComDesconto = mensalidade - 10, ValorIndicacao = 5, Total = total,
            Veiculo = new CrmVeiculo { Fipe = fipe, Rastreador = 20, ValorVistoria = 30 },
        };
        db.CrmOpportunities.AddRange(Venda(l1, ana.Id, 100, 150, 40_000, 250), Venda(l1, ana.Id, 50, 100, 20_000, 150), Venda(l2, ana.Id, 10, 90, 10_000, 100), Venda(l3, bruna.Id, 999, 999, 99_999, 999));
        await db.SaveChangesAsync();
        var servico = Leads(db, admin.Id);

        var daAna = await servico.ObterTotaisAsync(new LeadFilterRequest { ResponsavelIds = [ana.Id] }, CancellationToken.None);
        Assert.Equal(3, daAna.Contagem); // 3 leads da Ana (um sem venda), contados todos
        Assert.Equal(160m, daAna.Adesao); // 100 + 50 + 10: todas as vendas, não só a mais recente de cada lead
        Assert.Equal(70_000m, daAna.Fipe);
        Assert.Equal(340m, daAna.Mensalidade);
        Assert.Equal(310m, daAna.MensalidadeComDesconto);
        Assert.Equal(60m, daAna.Rastreador);
        Assert.Equal(15m, daAna.Indicacao);
        Assert.Equal(90m, daAna.Vistoria);
        Assert.Equal(500m, daAna.Total);

        var todos = await servico.ObterTotaisAsync(new LeadFilterRequest(), CancellationToken.None);
        Assert.Equal(4, todos.Contagem);
        Assert.Equal(1159m, todos.Adesao);

        var nenhum = await servico.ObterTotaisAsync(new LeadFilterRequest { ResponsavelIds = [Guid.NewGuid()] }, CancellationToken.None);
        Assert.Equal(0, nenhum.Contagem);
        Assert.Equal(0m, nenhum.Adesao);
    }

    [Fact]
    public async Task VeiculoNaoAtendido_SalvaLimpaEMostraNoCartaoDoQuadro()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var admin = await factory.CriarUsuarioAsync(db, "Admin");
        var lead = new CrmLead { NomeOuRazaoSocial = "Cliente", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = admin.Id };
        db.CrmLeads.Add(lead);
        await db.SaveChangesAsync();
        var servico = Leads(db, admin.Id);

        LeadUpdateRequest Editar(string? veiculo, uint rowVersion) => new(
            "Cliente", TipoPessoa.Fisica, null, null, null, null, null, null, null, null, null,
            null, null, null, null, null, null, null, null, null, null, null, null, null, null, null,
            null, null, false, null, rowVersion, VeiculoNaoAtendido: veiculo);

        var salvo = await servico.AtualizarAsync(lead.Id, Editar("  Honda CG 160  ", lead.RowVersion), CancellationToken.None);
        Assert.Equal("Honda CG 160", salvo.VeiculoNaoAtendido);

        var kanban = new LeadKanbanService(db, new EquipeComercialService(db, TestDbContextFactory.MockCurrentUser(admin.Id, visaoTotal: true, podeGerir: true).Object));
        var board = await kanban.ObterBoardAsync(new LeadKanbanFilterRequest(), CancellationToken.None);
        Assert.Equal("Honda CG 160", board.Colunas.SelectMany(c => c.Cartoes).Single().VeiculoNaoAtendido);

        // Nulo = o chamador não manda o campo: não mexe.
        db.ChangeTracker.Clear();
        var atual = await db.CrmLeads.AsNoTracking().SingleAsync(l => l.Id == lead.Id);
        var igual = await servico.AtualizarAsync(lead.Id, Editar(null, atual.RowVersion), CancellationToken.None);
        Assert.Equal("Honda CG 160", igual.VeiculoNaoAtendido);

        // Vazio limpa.
        db.ChangeTracker.Clear();
        atual = await db.CrmLeads.AsNoTracking().SingleAsync(l => l.Id == lead.Id);
        var limpo = await servico.AtualizarAsync(lead.Id, Editar("", atual.RowVersion), CancellationToken.None);
        Assert.Null(limpo.VeiculoNaoAtendido);
    }

    [Fact]
    public async Task Gestao_RankingMostraAdesaoOrdenadoPorElaETrafegoDoMesAnterior()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var admin = await factory.CriarUsuarioAsync(db, "Admin");
        var ana = await factory.CriarUsuarioAsync(db, "Ana");
        var bruna = await factory.CriarUsuarioAsync(db, "Bruna");
        await factory.AtribuirPapelAsync(db, ana, CssVision.Web.Authorization.Roles.Comercial);
        await factory.AtribuirPapelAsync(db, bruna, CssVision.Web.Authorization.Roles.Comercial);
        var ganho = await factory.CriarEtapaAsync(db, "Ganho", 9, TipoEtapaPipeline.Ganho);
        CrmLead Trafego(string nome, Guid resp) => new()
        {
            NomeOuRazaoSocial = nome, TipoPessoa = TipoPessoa.Fisica, ResponsavelId = resp, MetaLeadId = Guid.NewGuid().ToString(),
        };
        var deAna = Trafego("Trafego Ana", ana.Id);
        var deBruna = Trafego("Trafego Bruna", bruna.Id);
        var anteriorAna = Trafego("Mes anterior 1", ana.Id);
        var anteriorAna2 = Trafego("Mes anterior 2", ana.Id);
        db.CrmLeads.AddRange(deAna, deBruna, anteriorAna, anteriorAna2);
        await db.SaveChangesAsync();
        var primeiroDoMesAnterior = HorarioBrasilia.PrimeiroDiaDoMes(HorarioBrasilia.Hoje).AddMonths(-1);
        var meioDoMesAnterior = HorarioBrasilia.Inicio(primeiroDoMesAnterior.AddDays(10));
        await db.CrmLeads.Where(l => l.Id == anteriorAna.Id || l.Id == anteriorAna2.Id).ExecuteUpdateAsync(s => s.SetProperty(l => l.CriadoEm, meioDoMesAnterior));
        // Ana vendeu mais em valor total, Bruna mais em adesão: o ranking (coluna "Valor") é pela adesão.
        db.CrmOpportunities.AddRange(
            new CrmOpportunity { LeadId = deAna.Id, Titulo = "V", ResponsavelId = ana.Id, EtapaId = ganho.Id, ValorFinal = 900m, PagamentoAdesao = 100m, DataEfetivaFechamento = DateTimeOffset.UtcNow },
            new CrmOpportunity { LeadId = deBruna.Id, Titulo = "V", ResponsavelId = bruna.Id, EtapaId = ganho.Id, ValorFinal = 200m, PagamentoAdesao = 400m, DataEfetivaFechamento = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        var gestao = Gestao(db, admin.Id);

        var resumo = await gestao.ObterResumoAsync(null, null, CancellationToken.None);
        Assert.Equal(bruna.Id, resumo.Ranking.First().VendedorId);
        Assert.Equal(400m, resumo.Ranking.First().ValorAdesao);
        Assert.Equal(100m, resumo.Ranking.Single(r => r.VendedorId == ana.Id).ValorAdesao);

        var vendedores = await gestao.ObterVendedoresAsync(CancellationToken.None);
        var daAna = vendedores.Single(v => v.Id == ana.Id);
        Assert.Equal(1, daAna.LeadsTrafegoNoMes);
        Assert.Equal(2, daAna.LeadsTrafegoMesAnterior);
        Assert.Equal(0, vendedores.Single(v => v.Id == bruna.Id).LeadsTrafegoMesAnterior);
    }
}
