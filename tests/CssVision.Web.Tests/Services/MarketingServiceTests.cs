using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Aba Tráfego pago: fonte, filtros (inclusive "O que?" sem acento) e agrupamentos.</summary>
public class MarketingServiceTests
{
    private static CrmLead Lead(string oQue, string? metaLeadId = null, string? consentimento = null, string? estado = "MG", Guid? responsavel = null, Guid? etapa = null) => new()
    {
        NomeOuRazaoSocial = "Lead", TipoPessoa = TipoPessoa.Fisica, ProdutoInteresse = oQue, MetaLeadId = metaLeadId,
        ConsentimentoOrigem = consentimento, Estado = estado, ResponsavelId = responsavel, EtapaId = etapa, Telefone = "31999990000",
    };

    [Fact]
    public async Task PorPadrao_ConsideraSoTrafegoPago_EAgrupaOQueSemAcento()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        db.CrmLeads.AddRange(
            Lead("AGV", metaLeadId: "m1"),
            Lead("AGV ELETRICO", metaLeadId: "m2"),        // grafia do Notion
            Lead("agv elétrico", consentimento: OrigemLead.MarcadorFormularioSite),
            Lead("AGV TRUCK", metaLeadId: "m3"),
            Lead("AGV"));                                   // cadastro manual: não é tráfego
        await db.SaveChangesAsync();

        var painel = await new MarketingService(db).ObterAsync(new MarketingFilterRequest(), CancellationToken.None);

        Assert.Equal(4, painel.Indicadores.TotalLeads);
        Assert.Equal(["AGV", "AGV ELÉTRICO", "AGV TRUCK"], painel.PorOQue!.Select(o => o.Nome));
        Assert.Equal(2, painel.PorOQue!.Single(o => o.Nome == "AGV ELÉTRICO").TotalLeads);
        Assert.Contains(painel.PorCanal!, c => c.Nome == MarketingCanais.Site && c.TotalLeads == 1);

        var todos = await new MarketingService(db).ObterAsync(new MarketingFilterRequest { Fonte = "todos" }, CancellationToken.None);
        Assert.Equal(5, todos.Indicadores.TotalLeads);
    }

    [Fact]
    public async Task FiltraPorVariasTagsEstadoEConsultor()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var vendedor = await factory.CriarUsuarioAsync(db, "Consultora");
        db.CrmLeads.AddRange(
            Lead("AGV", "m1", estado: "MG", responsavel: vendedor.Id),
            Lead("AGV TRUCK", "m2", estado: "SP", responsavel: vendedor.Id),
            Lead("AGV TRUCK", "m3", estado: "MG"),
            Lead("AGV ELÉTRICO", "m4", estado: "MG", responsavel: vendedor.Id));
        await db.SaveChangesAsync();
        var service = new MarketingService(db);

        var truckEAgv = await service.ObterAsync(new MarketingFilterRequest { OQue = ["AGV", "agv truck"] }, CancellationToken.None);
        Assert.Equal(3, truckEAgv.Indicadores.TotalLeads);

        var mgDaConsultora = await service.ObterAsync(
            new MarketingFilterRequest { Estado = ["MG"], ResponsavelId = [vendedor.Id] }, CancellationToken.None);
        Assert.Equal(2, mgDaConsultora.Indicadores.TotalLeads);

        var semResponsavel = await service.ObterAsync(new MarketingFilterRequest { ResponsavelId = [Guid.Empty] }, CancellationToken.None);
        Assert.Equal(1, semResponsavel.Indicadores.TotalLeads);
        Assert.Equal(1, semResponsavel.Indicadores.LeadsSemResponsavel);

        // As opções dos filtros mostram tudo o que existe no período, mesmo com filtro aplicado.
        Assert.Equal(3, mgDaConsultora.Opcoes!.OQue.Count);
    }

    [Fact]
    public async Task UltimosLeads_VemPaginados_DoMaisRecente()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var agora = DateTimeOffset.UtcNow;
        for (var i = 0; i < 25; i++)
        {
            var lead = Lead("AGV", $"m{i}");
            lead.NomeOuRazaoSocial = $"Lead {i:00}";
            lead.CriadoEm = agora.AddMinutes(-i);
            db.CrmLeads.Add(lead);
        }
        await db.SaveChangesAsync();
        var service = new MarketingService(db);

        var pagina1 = await service.ListarLeadsAsync(new MarketingFilterRequest(), 1, 10, CancellationToken.None);
        var pagina3 = await service.ListarLeadsAsync(new MarketingFilterRequest(), 3, 10, CancellationToken.None);

        Assert.Equal(25, pagina1.TotalRegistros);
        Assert.Equal(3, pagina1.TotalPaginas);
        Assert.Equal("Lead 00", pagina1.Itens[0].NomeOuRazaoSocial);
        Assert.Equal(["Lead 20", "Lead 21", "Lead 22", "Lead 23", "Lead 24"], pagina3.Itens.Select(l => l.NomeOuRazaoSocial));

        var painel = await service.ObterAsync(new MarketingFilterRequest(), CancellationToken.None);
        Assert.Equal(25, painel.TotalLeadsLista);
    }

    [Fact]
    public async Task LeadsPorConsultor_SeparadosPorMes()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var vendedor = await factory.CriarUsuarioAsync(db, "Consultora");
        var venda = await factory.ObterOuCriarEtapaLeadAsync(db, "Venda concluída (Leads)", 5);
        CrmLead Em(DateTimeOffset quando, Guid? etapa = null)
        {
            var l = Lead("AGV", Guid.NewGuid().ToString(), responsavel: vendedor.Id, etapa: etapa);
            l.CriadoEm = quando;
            return l;
        }
        // Meio-dia em Brasília (15h UTC), longe da virada do dia.
        db.CrmLeads.AddRange(
            Em(new DateTimeOffset(2026, 8, 10, 15, 0, 0, TimeSpan.Zero)),
            Em(new DateTimeOffset(2026, 8, 20, 15, 0, 0, TimeSpan.Zero), venda.Id),
            Em(new DateTimeOffset(2026, 9, 5, 15, 0, 0, TimeSpan.Zero)));
        await db.SaveChangesAsync();

        var painel = await new MarketingService(db).ObterAsync(
            new MarketingFilterRequest { DataInicio = new DateOnly(2026, 8, 1), DataFim = new DateOnly(2026, 9, 30) }, CancellationToken.None);

        var mensal = painel.PorConsultorMensal!.Where(c => c.Id == vendedor.Id).ToDictionary(c => c.Mes);
        Assert.Equal(2, mensal["2026-08"].Leads);
        Assert.Equal(1, mensal["2026-08"].Ganhos);
        Assert.Equal(1, mensal["2026-09"].Leads);
    }

    [Fact]
    public async Task ContaVendasPerdidosEEtapaAtual()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var venda = await factory.ObterOuCriarEtapaLeadAsync(db, "Venda concluída (Leads)", 5);
        var perdido = await factory.ObterOuCriarEtapaLeadAsync(db, "Perdido", 6);
        var atendimento = await factory.ObterOuCriarEtapaLeadAsync(db, "Em atendimento (Leads)", 1);
        db.CrmLeads.AddRange(
            Lead("AGV", "m1", etapa: venda.Id),
            Lead("AGV", "m2", etapa: perdido.Id),
            Lead("AGV", "m3", etapa: atendimento.Id),
            Lead("AGV", "m4"));
        await db.SaveChangesAsync();

        var painel = await new MarketingService(db).ObterAsync(new MarketingFilterRequest(), CancellationToken.None);

        Assert.Equal(1, painel.Indicadores.LeadsGanhos);
        Assert.Equal(1, painel.Indicadores.LeadsPerdidos);
        Assert.Equal(1, painel.Indicadores.LeadsEmAndamento);
        Assert.Equal(1, painel.Indicadores.LeadsSemEtapa);
        Assert.Equal(50m, painel.Indicadores.TaxaConversao);
        Assert.Equal(25m, painel.Indicadores.TaxaConversaoGeral);
        Assert.Equal(4, painel.Funil!.Sum(f => f.Quantidade));
    }
}
