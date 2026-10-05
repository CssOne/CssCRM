using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Visão geral (conversão, leads parados, funil de leads, resumo mensal) e filtros/colunas da lista de leads.</summary>
public class VisaoGeralEFiltrosDeLeadsTests
{
    private static DashboardService Painel(ApplicationDbContext db, Guid usuarioId)
    {
        var usuario = TestDbContextFactory.MockCurrentUser(usuarioId, visaoTotal: true, podeGerir: true).Object;
        var equipe = new EquipeComercialService(db, usuario);
        return new DashboardService(db, equipe, new ActivityService(db, usuario, equipe, new NoOpAuditSink()), usuario);
    }

    private static LeadService Leads(ApplicationDbContext db, Guid usuarioId)
    {
        var usuario = TestDbContextFactory.MockCurrentUser(usuarioId, visaoTotal: true, podeGerir: true).Object;
        return new LeadService(db, usuario, new EquipeComercialService(db, usuario), new NoOpLeadAssignmentService(), new NoOpMetaConversionService(), new NoOpAuditSink());
    }

    /// <summary>Grava a data de criação (e zera a de atualização) direto no banco, sem a auditoria mexer nelas.</summary>
    private static Task DatarAsync(ApplicationDbContext db, Guid leadId, DateTimeOffset criadoEm, DateTimeOffset? ultimoContato = null) =>
        db.CrmLeads.Where(l => l.Id == leadId)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.CriadoEm, criadoEm).SetProperty(l => l.AtualizadoEm, (DateTimeOffset?)null).SetProperty(l => l.ResponsavelAtribuidoEm, (DateTimeOffset?)null).SetProperty(l => l.UltimoContatoEm, ultimoContato));

    [Fact]
    public async Task Conversao_EVendasPorVendedor_SaoSobreOsLeadsDoPeriodo()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var admin = await factory.CriarUsuarioAsync(db, "Admin");
        var ana = await factory.CriarUsuarioAsync(db, "Ana");
        var inativo = await factory.CriarUsuarioAsync(db, "Inativo sem nada");
        inativo.Ativo = false;
        var ganho = await factory.CriarEtapaAsync(db, "Ganho", 9, TipoEtapaPipeline.Ganho);
        var leads = Enumerable.Range(1, 4).Select(i => new CrmLead { NomeOuRazaoSocial = $"L{i}", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = ana.Id }).ToList();
        db.CrmLeads.AddRange(leads);
        await db.SaveChangesAsync();
        db.CrmOpportunities.Add(new CrmOpportunity
        {
            LeadId = leads[0].Id, Titulo = "V", ResponsavelId = ana.Id, EtapaId = ganho.Id, ValorFinal = 100m, PagamentoAdesao = 40m,
            DataEfetivaFechamento = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var r = await Painel(db, admin.Id).ObterAsync(new DashboardFilterRequest(null, null, null), CancellationToken.None);

        Assert.Equal(4, r.Indicadores.NovosLeads);
        Assert.Equal(25m, r.Indicadores.TaxaConversao); // 1 venda ÷ 4 leads (e não 100%: ganhas ÷ ganhas+perdidas)
        var linha = r.DesempenhoPorVendedor.Single(v => v.VendedorId == ana.Id);
        Assert.Equal(4, linha.LeadsAtribuidos);
        Assert.Equal(1, linha.VendasGanhas);
        Assert.Equal(25m, linha.TaxaConversao);
        Assert.Equal(40m, linha.ValorAdesao);
        Assert.DoesNotContain(r.DesempenhoPorVendedor, v => v.VendedorId == inativo.Id); // inativo e sem nada: fora da lista
        Assert.Equal(1, r.ResumoMensal!.Last().Vendas);
        Assert.Equal(4, r.ResumoMensal!.Last().Leads);
        Assert.Equal(12, r.ResumoMensal!.Count);
    }

    [Fact]
    public async Task LeadsParados_SoOsRecentesEmColunaAbertaDeConsultorAtivoSemMovimento()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var admin = await factory.CriarUsuarioAsync(db, "Admin");
        var ana = await factory.CriarUsuarioAsync(db, "Ana");
        var inativa = await factory.CriarUsuarioAsync(db, "Bia");
        inativa.Ativo = false;
        var aberta = await factory.ObterOuCriarEtapaLeadAsync(db, "Em atendimento", 1);
        var perdido = new CrmLeadStage { Nome = "Perdido", Ordem = 2, Fechada = true };
        db.CrmLeadStages.Add(perdido);
        await db.SaveChangesAsync();

        // Só vem de anúncio (Meta Lead Ads/site) quem tem MetaLeadId; os cards do Notion têm o marcador de sincronização.
        CrmLead L(string nome, Guid resp, Guid etapa) => new()
        {
            NomeOuRazaoSocial = nome, TipoPessoa = TipoPessoa.Fisica, ResponsavelId = resp, EtapaId = etapa, MetaLeadId = Guid.NewGuid().ToString(),
        };
        var parado = L("Parado", ana.Id, aberta.Id);
        var doNotion = L("Parado mas do Notion", ana.Id, aberta.Id);
        doNotion.MetaLeadId = null;
        doNotion.ConsentimentoOrigem = OrigemLead.MarcadorSincronizacaoNotion;
        var fechado = L("Perdido", ana.Id, perdido.Id);
        var deInativa = L("De inativa", inativa.Id, aberta.Id);
        var antigo = L("Antigo de 2023", ana.Id, aberta.Id);
        var comContato = L("Com contato recente", ana.Id, aberta.Id);
        db.CrmLeads.AddRange(parado, doNotion, fechado, deInativa, antigo, comContato);
        await db.SaveChangesAsync();
        var agora = DateTimeOffset.UtcNow;
        foreach (var l in new[] { parado, doNotion, fechado, deInativa, comContato }) await DatarAsync(db, l.Id, agora.AddDays(-20), l == comContato ? agora.AddDays(-1) : null);
        await DatarAsync(db, antigo.Id, agora.AddDays(-1300));

        var r = await Painel(db, admin.Id).ObterAsync(new DashboardFilterRequest(null, null, null), CancellationToken.None);

        var item = Assert.Single(r.LeadsParados);
        Assert.Equal("Parado", item.LeadNome); // o parado do Notion não entra
        Assert.Equal("Em atendimento", item.EtapaNome);
        Assert.InRange(item.DiasSemContato, 19, 21);
        Assert.Equal(1, r.LeadsParadosTotal);
    }

    [Fact]
    public async Task FunilDeLeads_ResumeOQuadroDoPeriodoNaOrdemDasColunas()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var admin = await factory.CriarUsuarioAsync(db, "Admin");
        var ana = await factory.CriarUsuarioAsync(db, "Ana");
        var atendimento = await factory.ObterOuCriarEtapaLeadAsync(db, "Em atendimento", 2);
        var cotacao = await factory.ObterOuCriarEtapaLeadAsync(db, "Cotação", 3);
        CrmLead L(Guid? etapa) => new() { NomeOuRazaoSocial = "L", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = ana.Id, EtapaId = etapa };
        db.CrmLeads.AddRange(L(null), L(atendimento.Id), L(atendimento.Id), L(cotacao.Id));
        await db.SaveChangesAsync();

        var r = await Painel(db, admin.Id).ObterAsync(new DashboardFilterRequest(null, null, null), CancellationToken.None);

        Assert.Equal(["Sem etapa", "Em atendimento", "Cotação"], r.FunilLeads!.Select(e => e.Etapa).ToArray());
        Assert.Equal([1, 2, 1], r.FunilLeads!.Select(e => e.Quantidade).ToArray());
    }

    [Fact]
    public async Task ListaDeLeads_FiltraPorVariosConsultoresEtapasEOrigens_EMostraAsColunasDaVenda()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var admin = await factory.CriarUsuarioAsync(db, "Admin");
        var ana = await factory.CriarUsuarioAsync(db, "Ana");
        var bruna = await factory.CriarUsuarioAsync(db, "Bruna");
        var carla = await factory.CriarUsuarioAsync(db, "Carla");
        var regional = await factory.CriarRegionalAsync(db, "MG132");
        var growth = await factory.CriarGrupoAsync(db, regional.Id, "CSS Growth Sales");
        bruna.GrupoId = growth.Id;
        var atendimento = await factory.ObterOuCriarEtapaLeadAsync(db, "Em atendimento", 1);
        var venda = await factory.ObterOuCriarEtapaLeadAsync(db, "Venda concluída", 2);
        var ganho = await factory.CriarEtapaAsync(db, "Ganho", 9, TipoEtapaPipeline.Ganho);

        CrmLead L(string nome, Guid resp, Guid? etapa, string origem) => new()
        {
            NomeOuRazaoSocial = nome, TipoPessoa = TipoPessoa.Fisica, ResponsavelId = resp, EtapaId = etapa, Origem = origem, Regional = "MG132",
        };
        var l1 = L("Da Ana", ana.Id, atendimento.Id, "Pmax");
        var l2 = L("Da Bruna", bruna.Id, venda.Id, "Lookalike");
        var l3 = L("Da Carla", carla.Id, null, "UGC");
        db.CrmLeads.AddRange(l1, l2, l3);
        await db.SaveChangesAsync();
        db.CrmOpportunities.Add(new CrmOpportunity
        {
            LeadId = l2.Id, Titulo = "AGV", ResponsavelId = bruna.Id, EtapaId = ganho.Id, PagamentoAdesao = 300m, Mensalidade = 150m,
            MensalidadeComDesconto = 135m, Porcentagem = 10m, ValorIndicacao = 50m, Total = 480m,
            DataEfetivaFechamento = new DateTimeOffset(2026, 9, 10, 15, 0, 0, TimeSpan.Zero),
            Veiculo = new CrmVeiculo { Fipe = 40_000m, Rastreador = 60m, ValorVistoria = 30m },
        });
        await db.SaveChangesAsync();
        var servico = Leads(db, admin.Id);
        Task<PagedResult<LeadListItemDto>> Listar(LeadFilterRequest f) => servico.ListarAsync(f with { Pagina = 1, TamanhoPagina = 50 }, CancellationToken.None);

        var consultores = await Listar(new LeadFilterRequest { ResponsavelIds = [ana.Id, bruna.Id] });
        Assert.Equal(["Da Ana", "Da Bruna"], consultores.Itens.Select(i => i.NomeOuRazaoSocial).Order().ToArray());

        var etapas = await Listar(new LeadFilterRequest { LeadEtapaIds = [atendimento.Id, Guid.Empty] }); // inclui "Sem etapa"
        Assert.Equal(["Da Ana", "Da Carla"], etapas.Itens.Select(i => i.NomeOuRazaoSocial).Order().ToArray());

        var origens = await Listar(new LeadFilterRequest { Origens = ["Pmax", "UGC"] });
        Assert.Equal(2, origens.TotalRegistros);

        var grupo = await Listar(new LeadFilterRequest { GrupoIds = [growth.Id], Regionais = ["MG132"] });
        Assert.Equal("Da Bruna", Assert.Single(grupo.Itens).NomeOuRazaoSocial);

        var vendidos = await Listar(new LeadFilterRequest { DataVendaInicio = new DateOnly(2026, 9, 1), DataVendaFim = new DateOnly(2026, 9, 30) });
        var item = Assert.Single(vendidos.Itens);
        Assert.Equal("Da Bruna", item.NomeOuRazaoSocial);
        Assert.Equal(300m, item.Venda!.Adesao);
        Assert.Equal(40_000m, item.Venda.Fipe);
        Assert.Equal(150m, item.Venda.Mensalidade);
        Assert.Equal(135m, item.Venda.MensalidadeComDesconto);
        Assert.Equal(10m, item.Venda.Porcentagem);
        Assert.Equal(60m, item.Venda.Rastreador);
        Assert.Equal(50m, item.Venda.Indicacao);
        Assert.Equal(30m, item.Venda.Vistoria);
        Assert.Equal(480m, item.Venda.Total);

        var todos = await Listar(new LeadFilterRequest());
        Assert.Null(todos.Itens.Single(i => i.NomeOuRazaoSocial == "Da Ana").Venda); // sem venda: sem colunas
    }

    [Fact]
    public async Task Quadro_FiltraPorGrupoDoConsultor()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var admin = await factory.CriarUsuarioAsync(db, "Admin");
        var ana = await factory.CriarUsuarioAsync(db, "Ana");
        var bruna = await factory.CriarUsuarioAsync(db, "Bruna");
        var regional = await factory.CriarRegionalAsync(db, "MG132");
        var growth = await factory.CriarGrupoAsync(db, regional.Id, "CSS Growth Sales");
        var novoGrupo = await factory.CriarGrupoAsync(db, regional.Id, "Grupo criado depois");
        ana.GrupoId = growth.Id;
        bruna.GrupoId = novoGrupo.Id;
        db.CrmLeads.AddRange(
            new CrmLead { NomeOuRazaoSocial = "Do Growth", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = ana.Id },
            new CrmLead { NomeOuRazaoSocial = "Do grupo novo", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = bruna.Id });
        await db.SaveChangesAsync();
        var usuario = TestDbContextFactory.MockCurrentUser(admin.Id, visaoTotal: true, podeGerir: true).Object;
        var kanban = new LeadKanbanService(db, new EquipeComercialService(db, usuario));

        var doGrowth = await kanban.ObterBoardAsync(new LeadKanbanFilterRequest { GrupoId = [growth.Id] }, CancellationToken.None);
        Assert.Equal("Do Growth", Assert.Single(doGrowth.Colunas.SelectMany(c => c.Cartoes)).NomeOuRazaoSocial);

        var dosDois = await kanban.ObterBoardAsync(new LeadKanbanFilterRequest { GrupoId = [growth.Id, novoGrupo.Id] }, CancellationToken.None);
        Assert.Equal(2, dosDois.Colunas.SelectMany(c => c.Cartoes).Count());
    }
}
