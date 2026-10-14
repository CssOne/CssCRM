using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Relatório comercial: conversão por venda de tráfego pago (CRM e Notion) e por venda de indicação.</summary>
public class RelatorioConversaoPorOrigemTests
{
    [Fact]
    public async Task ConversaoDeTrafegoPagoEDeIndicacao_SaoCalculadasPeloGrupoDeLeads()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var admin = await factory.CriarUsuarioAsync(db, "Admin");
        var ana = await factory.CriarUsuarioAsync(db, "Ana");
        var ganho = await factory.CriarEtapaAsync(db, "Ganho", 9, TipoEtapaPipeline.Ganho);
        var notion = OrigemLead.MarcadorSincronizacaoNotion;

        CrmLead L(string nome, bool manual, string? tipo = null, string? meta = null, string? origem = null, string? campanha = null) => new()
        {
            NomeOuRazaoSocial = nome, TipoPessoa = TipoPessoa.Fisica, ResponsavelId = ana.Id,
            CriadoManualmente = manual, TipoIndicacao = tipo, MetaLeadId = meta, ConsentimentoOrigem = origem, Campanha = campanha,
        };
        // Tráfego pago: 2 do CRM (1 vendeu) + 2 do Notion (1 vendeu) = 4 leads, 2 vendas. Indicação: 2 cards (1 vendeu). Um anúncio que fechou como
        // "Indicação Lead" conta na indicação (3 cards, 2 vendas) e não no tráfego.
        var t1 = L("t1", false, "Lead", meta: "m1"); var t2 = L("t2", false, "Lead", meta: "m2");
        var n1 = L("n1", false, "Lead", origem: notion, campanha: "UGC"); var n2 = L("n2", false, "Lead", origem: notion, campanha: "UGC");
        var i1 = L("i1", true, "Pessoal"); var i2 = L("i2", true, "Pessoal");
        var il = L("il", false, "Indicação Lead", meta: "m3");
        var todos = new[] { t1, t2, n1, n2, i1, i2, il };
        db.CrmLeads.AddRange(todos);
        await db.SaveChangesAsync();
        foreach (var lead in new[] { t1, n1, i1, il })
        {
            db.CrmOpportunities.Add(new CrmOpportunity
            {
                LeadId = lead.Id, Titulo = lead.NomeOuRazaoSocial, ResponsavelId = ana.Id, EtapaId = ganho.Id, DataEfetivaFechamento = DateTimeOffset.UtcNow,
            });
        }
        await db.SaveChangesAsync();

        var usuario = TestDbContextFactory.MockCurrentUser(admin.Id, visaoTotal: true, podeGerir: true).Object;
        var relatorio = new RelatorioComercialService(db, usuario, new EquipeComercialService(db, usuario));
        var hoje = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-3));

        var t = (await relatorio.ObterAsync(hoje.AddDays(-1), hoje, null, null, CancellationToken.None)).Totais;

        Assert.Equal(4, t.LeadsTrafegoPago);
        Assert.Equal(2, t.VendasTrafegoPago);
        Assert.Equal(50m, t.TaxaConversaoTrafegoPago);
        Assert.Equal(3, t.LeadsIndicacaoCard);
        Assert.Equal(2, t.VendasIndicacaoCard);
        Assert.Equal(66.7m, t.TaxaConversaoIndicacaoCard);
    }
}
