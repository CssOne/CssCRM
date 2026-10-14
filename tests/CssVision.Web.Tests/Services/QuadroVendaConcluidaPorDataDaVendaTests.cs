using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>
/// No quadro de leads, filtrar o mês pela data de CHEGADA esconde as vendas feitas no mês de leads que chegaram antes. Nas colunas "Venda concluída" o período de
/// chegada passa a valer para a data da VENDA; as demais colunas continuam filtrando pela chegada.
/// </summary>
public class QuadroVendaConcluidaPorDataDaVendaTests
{
    [Fact]
    public async Task ColunaDeVendaConcluida_FiltraPelaDataDaVenda_QuandoOFiltroEDeChegada()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var admin = await factory.CriarUsuarioAsync(db, "Admin");
        var ganho = await factory.CriarEtapaAsync(db, "Ganho", 9, TipoEtapaPipeline.Ganho);
        var vendaConcluida = await factory.ObterOuCriarEtapaLeadAsync(db, "Venda concluída (Leads)", 5);
        var atendimento = await factory.ObterOuCriarEtapaLeadAsync(db, "Em atendimento (Leads)", 2);

        CrmLead Lead(string nome, DateTimeOffset chegou, Guid etapa) => new()
        {
            NomeOuRazaoSocial = nome, TipoPessoa = TipoPessoa.Fisica, ResponsavelId = admin.Id, EtapaId = etapa, CriadoEm = chegou,
        };
        var chegouAntesVendeuNoMes = Lead("chegou em setembro, vendeu em outubro", new DateTimeOffset(2026, 9, 20, 15, 0, 0, TimeSpan.Zero), vendaConcluida.Id);
        var chegouEVendeuNoMes = Lead("chegou e vendeu em outubro", new DateTimeOffset(2026, 10, 3, 15, 0, 0, TimeSpan.Zero), vendaConcluida.Id);
        var vendeuAntes = Lead("vendeu em setembro", new DateTimeOffset(2026, 9, 1, 15, 0, 0, TimeSpan.Zero), vendaConcluida.Id);
        var emAtendimentoNoMes = Lead("em atendimento, chegou em outubro", new DateTimeOffset(2026, 10, 5, 15, 0, 0, TimeSpan.Zero), atendimento.Id);
        var emAtendimentoAntes = Lead("em atendimento, chegou em setembro", new DateTimeOffset(2026, 9, 5, 15, 0, 0, TimeSpan.Zero), atendimento.Id);
        db.CrmLeads.AddRange(chegouAntesVendeuNoMes, chegouEVendeuNoMes, vendeuAntes, emAtendimentoNoMes, emAtendimentoAntes);
        await db.SaveChangesAsync();
        // O contexto preenche CriadoEm ao inserir; aqui volta à data de chegada do cenário.
        chegouAntesVendeuNoMes.CriadoEm = new DateTimeOffset(2026, 9, 20, 15, 0, 0, TimeSpan.Zero);
        chegouEVendeuNoMes.CriadoEm = new DateTimeOffset(2026, 10, 3, 15, 0, 0, TimeSpan.Zero);
        vendeuAntes.CriadoEm = new DateTimeOffset(2026, 9, 1, 15, 0, 0, TimeSpan.Zero);
        emAtendimentoNoMes.CriadoEm = new DateTimeOffset(2026, 10, 5, 15, 0, 0, TimeSpan.Zero);
        emAtendimentoAntes.CriadoEm = new DateTimeOffset(2026, 9, 5, 15, 0, 0, TimeSpan.Zero);
        await db.SaveChangesAsync();
        CrmOpportunity Venda(CrmLead l, DateTimeOffset data) => new()
        {
            LeadId = l.Id, Titulo = "AGV", ResponsavelId = admin.Id, EtapaId = ganho.Id, DataEfetivaFechamento = data,
        };
        db.CrmOpportunities.AddRange(
            Venda(chegouAntesVendeuNoMes, new DateTimeOffset(2026, 10, 8, 15, 0, 0, TimeSpan.Zero)),
            Venda(chegouEVendeuNoMes, new DateTimeOffset(2026, 10, 9, 15, 0, 0, TimeSpan.Zero)),
            Venda(vendeuAntes, new DateTimeOffset(2026, 9, 2, 15, 0, 0, TimeSpan.Zero)));
        await db.SaveChangesAsync();

        var usuario = TestDbContextFactory.MockCurrentUser(admin.Id, visaoTotal: true);
        var kanban = new LeadKanbanService(db, new EquipeComercialService(db, usuario.Object));

        var board = await kanban.ObterBoardAsync(
            new LeadKanbanFilterRequest { DataChegadaInicio = new DateOnly(2026, 10, 1), DataChegadaFim = new DateOnly(2026, 10, 14) }, CancellationToken.None);

        var vendas = board.Colunas.Single(c => c.Etapa.Nome == "Venda concluída (Leads)");
        Assert.Equal(2, vendas.Total);
        Assert.Equal(["chegou e vendeu em outubro", "chegou em setembro, vendeu em outubro"], vendas.Cartoes.Select(c => c.NomeOuRazaoSocial).Order());
        var atendimentoCol = board.Colunas.Single(c => c.Etapa.Nome == "Em atendimento (Leads)");
        Assert.Equal(["em atendimento, chegou em outubro"], atendimentoCol.Cartoes.Select(c => c.NomeOuRazaoSocial)); // as demais colunas seguem a chegada

        // "Ver mais" da coluna de venda usa a mesma regra.
        var maisVendas = await kanban.ObterCartoesAsync(
            new LeadKanbanColunaRequest { EtapaId = vendaConcluida.Id, DataChegadaInicio = new DateOnly(2026, 10, 1), DataChegadaFim = new DateOnly(2026, 10, 14), Quantidade = 30 },
            CancellationToken.None);
        Assert.Equal(2, maisVendas.Count);
    }
}
