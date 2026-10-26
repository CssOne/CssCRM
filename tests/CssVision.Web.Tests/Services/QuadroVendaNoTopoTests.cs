using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>
/// Cliente cadastrado há tempo que fecha a venda hoje precisa aparecer no topo da coluna "Venda concluída" do quadro (e não no fundo, atrás de milhares de
/// cartões), nenhum lead pode ficar sem coluna, e o diagnóstico explica por que um cliente não aparece.
/// </summary>
public class QuadroVendaNoTopoTests : IDisposable
{
    private readonly TestDbContextFactory _factory = new();
    private readonly CssVision.Web.Data.ApplicationDbContext _db;

    public QuadroVendaNoTopoTests() => _db = _factory.CreateContext();

    public void Dispose()
    {
        _db.Dispose();
        _factory.Dispose();
    }

    private LeadKanbanService Kanban(Guid adminId) =>
        new(_db, new EquipeComercialService(_db, TestDbContextFactory.MockCurrentUser(adminId, visaoTotal: true).Object));

    private async Task<(Guid AdminId, CrmLeadStage Venda, CrmPipelineStage Ganho)> CenarioAsync(bool vendaAtiva = true)
    {
        var admin = await _factory.CriarUsuarioAsync(_db, "Admin");
        var venda = new CrmLeadStage { Nome = "Venda concluída (Indicação)", Ordem = 6, Ativa = vendaAtiva, Fechada = true };
        _db.CrmLeadStages.Add(venda);
        await _db.SaveChangesAsync();
        var ganho = await _factory.CriarEtapaAsync(_db, "Ganho", 8, TipoEtapaPipeline.Ganho);
        return (admin.Id, venda, ganho);
    }

    private async Task<CrmLead> VendaAsync(Guid adminId, CrmLeadStage etapa, CrmPipelineStage ganho, string nome, DateTimeOffset criadoEm, DateTimeOffset venda)
    {
        var lead = new CrmLead { NomeOuRazaoSocial = nome, TipoPessoa = TipoPessoa.Fisica, ResponsavelId = adminId, EtapaId = etapa.Id, CriadoEm = criadoEm };
        _db.CrmLeads.Add(lead);
        await _db.SaveChangesAsync();
        _db.CrmOpportunities.Add(new CrmOpportunity { LeadId = lead.Id, Titulo = "AGV", ResponsavelId = adminId, EtapaId = ganho.Id, DataEfetivaFechamento = venda });
        await _db.SaveChangesAsync();
        return lead;
    }

    [Fact]
    public async Task VendaDeHoje_DeClienteAntigo_FicaNoTopoDaColunaDeVenda_NoQuadroENoVerMais()
    {
        var (admin, venda, ganho) = await CenarioAsync();
        var hoje = new DateTimeOffset(2026, 10, 26, 15, 0, 0, TimeSpan.Zero);
        // 45 clientes recentes com vendas antigas, e um cadastrado em 2020 que fechou a venda hoje.
        for (var i = 0; i < 45; i++)
            await VendaAsync(admin, venda, ganho, $"Cliente {i:00}", hoje.AddDays(-10 - i), hoje.AddDays(-60 - i));
        await VendaAsync(admin, venda, ganho, "Cliente antigo com venda de hoje", new DateTimeOffset(2020, 3, 1, 12, 0, 0, TimeSpan.Zero), hoje);

        var board = await Kanban(admin).ObterBoardAsync(new LeadKanbanFilterRequest { CartoesPorColuna = 30 }, CancellationToken.None);
        var coluna = board.Colunas.Single(c => c.Etapa.Id == venda.Id);

        Assert.Equal(46, coluna.Total);
        Assert.Equal(30, coluna.Cartoes.Count);
        Assert.Equal("Cliente antigo com venda de hoje", coluna.Cartoes[0].NomeOuRazaoSocial); // antes ficava na última posição (criado em 2020)

        var proximos = await Kanban(admin).ObterCartoesAsync(new LeadKanbanColunaRequest { EtapaId = venda.Id, Pular = 30, Quantidade = 30 }, CancellationToken.None);
        Assert.Equal(16, proximos.Count);
        Assert.DoesNotContain(proximos, c => c.NomeOuRazaoSocial == "Cliente antigo com venda de hoje");
    }

    [Fact]
    public async Task LeadEmEtapaInativa_GanhaColunaPropria_EmVezDeSumirDoQuadro()
    {
        var (admin, venda, ganho) = await CenarioAsync(vendaAtiva: false);
        await VendaAsync(admin, venda, ganho, "Cliente na etapa inativa", DateTimeOffset.UtcNow.AddDays(-3), DateTimeOffset.UtcNow.AddDays(-1));

        var board = await Kanban(admin).ObterBoardAsync(new LeadKanbanFilterRequest(), CancellationToken.None);

        var coluna = Assert.Single(board.Colunas, c => c.Etapa.Nome.EndsWith("(etapa inativa)"));
        Assert.Equal("Cliente na etapa inativa", Assert.Single(coluna.Cartoes).NomeOuRazaoSocial);
        Assert.False(coluna.Etapa.Ativa);
    }

    [Fact]
    public async Task Diagnostico_DizOndeOClienteEstaEPorqueNaoApareceDeInicio()
    {
        var (admin, venda, ganho) = await CenarioAsync();
        var hoje = new DateTimeOffset(2026, 10, 26, 15, 0, 0, TimeSpan.Zero);
        for (var i = 0; i < 40; i++)
            await VendaAsync(admin, venda, ganho, $"Recente {i:00}", hoje.AddDays(-5 - i), hoje.AddDays(-5 - i));
        await VendaAsync(admin, venda, ganho, "Fabiano Antigo", new DateTimeOffset(2020, 1, 1, 12, 0, 0, TimeSpan.Zero), hoje.AddDays(-300));

        var achado = Assert.Single(await Kanban(admin).DiagnosticarAsync("fabiano", CancellationToken.None));

        Assert.Equal("Venda concluída (Indicação)", achado.Etapa);
        Assert.Equal(41, achado.PosicaoNaColuna);
        Assert.Equal(41, achado.TotalNaColuna);
        Assert.Contains("posição 41", achado.Veredito);
        Assert.Contains("Ver mais", achado.Veredito);
        Assert.Equal(1, achado.VendasGanhas);
    }

    [Fact]
    public async Task Diagnostico_ReconheceArquivado_Lixeira_EtapaInativa_EVendaSemVendaGanha()
    {
        var (admin, venda, ganho) = await CenarioAsync();
        var inativa = new CrmLeadStage { Nome = "Etapa velha", Ordem = 99, Ativa = false };
        _db.CrmLeadStages.Add(inativa);
        await _db.SaveChangesAsync();
        _db.CrmLeads.AddRange(
            new CrmLead { NomeOuRazaoSocial = "Zeta arquivado", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = admin, EtapaId = venda.Id, Arquivado = true },
            new CrmLead { NomeOuRazaoSocial = "Zeta excluido", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = admin, EtapaId = venda.Id, Arquivado = true, ArquivadoPorId = admin },
            new CrmLead { NomeOuRazaoSocial = "Zeta inativa", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = admin, EtapaId = inativa.Id },
            new CrmLead { NomeOuRazaoSocial = "Zeta sem venda", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = admin, EtapaId = venda.Id });
        await _db.SaveChangesAsync();

        var porNome = (await Kanban(admin).DiagnosticarAsync("zeta", CancellationToken.None)).ToDictionary(d => d.Nome);

        Assert.Contains("arquivado", porNome["Zeta arquivado"].Veredito);
        Assert.True(porNome["Zeta excluido"].NaLixeira);
        Assert.Contains("lixeira", porNome["Zeta excluido"].Veredito);
        Assert.Contains("inativa", porNome["Zeta inativa"].Veredito);
        Assert.Contains("nenhuma venda ganha", porNome["Zeta sem venda"].Veredito);

        var curta = await Assert.ThrowsAsync<CssVision.Web.Api.Contracts.Common.CrmBusinessException>(() => Kanban(admin).DiagnosticarAsync("ab", CancellationToken.None));
        Assert.Equal("busca_curta", curta.Codigo);
    }
    [Fact]
    public async Task Auditoria_ContaOsGruposQueOQuadroPodeEsconder()
    {
        var (admin, venda, ganho) = await CenarioAsync();
        var inativa = new CrmLeadStage { Nome = "Etapa velha", Ordem = 99, Ativa = false };
        var atendimento = new CrmLeadStage { Nome = "Em atendimento (Leads)", Ordem = 1 };
        _db.CrmLeadStages.AddRange(inativa, atendimento);
        await _db.SaveChangesAsync();
        await VendaAsync(admin, venda, ganho, "Venda normal", DateTimeOffset.UtcNow.AddDays(-5), DateTimeOffset.UtcNow.AddDays(-1));
        _db.CrmLeads.AddRange(
            new CrmLead { NomeOuRazaoSocial = "Na etapa inativa", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = admin, EtapaId = inativa.Id },
            new CrmLead { NomeOuRazaoSocial = "Venda sem ganho", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = admin, EtapaId = venda.Id });
        var fora = new CrmLead { NomeOuRazaoSocial = "Ganho fora da coluna", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = admin, EtapaId = atendimento.Id };
        _db.CrmLeads.Add(fora);
        await _db.SaveChangesAsync();
        _db.CrmOpportunities.Add(new CrmOpportunity { LeadId = fora.Id, Titulo = "AGV", ResponsavelId = admin, EtapaId = ganho.Id, DataEfetivaFechamento = DateTimeOffset.UtcNow });
        await _db.SaveChangesAsync();

        var grupos = (await Kanban(admin).AuditarAsync(CancellationToken.None)).ToDictionary(g => g.Codigo);

        Assert.Equal(1, grupos["etapa-inativa"].Total);
        Assert.Equal("Na etapa inativa", Assert.Single(grupos["etapa-inativa"].Exemplos).Nome);
        Assert.Equal(1, grupos["venda-sem-venda-ganha"].Total);
        Assert.Equal("Venda sem ganho", Assert.Single(grupos["venda-sem-venda-ganha"].Exemplos).Nome);
        Assert.Equal(1, grupos["venda-fora-da-coluna"].Total);
        Assert.Equal("Ganho fora da coluna", Assert.Single(grupos["venda-fora-da-coluna"].Exemplos).Nome);
    }

}
