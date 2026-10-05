using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Visão geral: card "Novos leads" (tráfego pago sem etapa), ranking por conversão e evolução com adesão.</summary>
public class VisaoGeralRankingEEvolucaoTests
{
    private static DashboardService Painel(ApplicationDbContext db, Guid usuarioId)
    {
        var usuario = TestDbContextFactory.MockCurrentUser(usuarioId, visaoTotal: true, podeGerir: true).Object;
        var equipe = new EquipeComercialService(db, usuario);
        return new DashboardService(db, equipe, new ActivityService(db, usuario, equipe, new NoOpAuditSink()), usuario);
    }

    [Fact]
    public async Task NovosLeads_ContaSoTrafegoPagoSemEtapa_EOConversaoSegueUsandoTodosOsLeads()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var admin = await factory.CriarUsuarioAsync(db, "Admin");
        var ana = await factory.CriarUsuarioAsync(db, "Ana");
        var etapa = await factory.ObterOuCriarEtapaLeadAsync(db, "Em atendimento", 1);
        CrmLead Lead(string nome, string? metaId, Guid? etapaId) => new()
        {
            NomeOuRazaoSocial = nome, TipoPessoa = TipoPessoa.Fisica, ResponsavelId = ana.Id, MetaLeadId = metaId, EtapaId = etapaId,
        };
        db.CrmLeads.AddRange(
            Lead("Anuncio sem etapa 1", "m1", null), Lead("Anuncio sem etapa 2", "m2", null),
            Lead("Anuncio ja trabalhado", "m3", etapa.Id),
            Lead("Manual sem etapa", null, null));
        await db.SaveChangesAsync();

        var r = await Painel(db, admin.Id).ObterAsync(new DashboardFilterRequest(null, null, null), CancellationToken.None);

        Assert.Equal(2, r.Indicadores.NovosLeadsTrafegoSemEtapa);
        Assert.Equal(4, r.Indicadores.NovosLeads); // denominador da conversão continua sendo todos os leads do mês
    }

    [Fact]
    public async Task Desempenho_VemEmRankingPorConversao_ESemLeadsFicaPorUltimo_EEvolucaoTrazAdesao()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var admin = await factory.CriarUsuarioAsync(db, "Admin");
        var ana = await factory.CriarUsuarioAsync(db, "Ana");     // 1 venda / 4 leads = 25%
        var bruna = await factory.CriarUsuarioAsync(db, "Bruna"); // 1 venda / 2 leads = 50%
        var caio = await factory.CriarUsuarioAsync(db, "Caio");   // 3 vendas, sem leads novos: não tem conversão
        var ganho = await factory.CriarEtapaAsync(db, "Ganho", 9, TipoEtapaPipeline.Ganho);
        CrmLead Lead(string nome, Guid resp) => new() { NomeOuRazaoSocial = nome, TipoPessoa = TipoPessoa.Fisica, ResponsavelId = resp };
        var daAna = Enumerable.Range(1, 4).Select(i => Lead($"A{i}", ana.Id)).ToList();
        var daBruna = Enumerable.Range(1, 2).Select(i => Lead($"B{i}", bruna.Id)).ToList();
        var daCaio = Enumerable.Range(1, 3).Select(i => Lead($"C{i}", caio.Id)).ToList();
        db.CrmLeads.AddRange(daAna.Concat(daBruna).Concat(daCaio));
        await db.SaveChangesAsync();
        // Os leads do Caio são antigos: ele vendeu no mês, mas não recebeu lead no mês.
        var umAnoAtras = DateTimeOffset.UtcNow.AddYears(-1);
        await db.CrmLeads.Where(l => l.ResponsavelId == caio.Id).ExecuteUpdateAsync(s => s.SetProperty(l => l.CriadoEm, umAnoAtras));
        CrmOpportunity Venda(CrmLead lead, Guid resp, decimal total, decimal adesao) => new()
        {
            LeadId = lead.Id, Titulo = "V", ResponsavelId = resp, EtapaId = ganho.Id, ValorFinal = total, PagamentoAdesao = adesao,
            DataEfetivaFechamento = DateTimeOffset.UtcNow,
        };
        db.CrmOpportunities.AddRange(Venda(daAna[0], ana.Id, 100, 30), Venda(daBruna[0], bruna.Id, 80, 20),
            Venda(daCaio[0], caio.Id, 1000, 500), Venda(daCaio[1], caio.Id, 1000, 500), Venda(daCaio[2], caio.Id, 1000, 500));
        await db.SaveChangesAsync();

        var r = await Painel(db, admin.Id).ObterAsync(new DashboardFilterRequest(null, null, null), CancellationToken.None);

        var ordem = r.DesempenhoPorVendedor.Where(v => new[] { ana.Id, bruna.Id, caio.Id }.Contains(v.VendedorId)).Select(v => v.VendedorNome).ToList();
        Assert.Equal(["Bruna", "Ana", "Caio"], ordem);

        var mesAtual = r.EvolucaoVendas.Last();
        Assert.Equal(5, mesAtual.Quantidade);
        Assert.Equal(3180m, mesAtual.ValorGanho);
        Assert.Equal(1550m, mesAtual.Adesao);
    }
}
