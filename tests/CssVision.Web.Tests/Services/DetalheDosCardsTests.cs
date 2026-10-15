using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>A lista por trás de cada card do Relatório comercial e de cada fatia do gráfico de origem das vendas sai da mesma consulta do número.</summary>
public class DetalheDosCardsTests
{
    [Fact]
    public async Task DetalheDoRelatorioEDoPainel_BatemComOsNumeros()
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
        var t1 = L("t1", false, "Lead", meta: "m1"); var n1 = L("n1", false, "Lead", origem: notion, campanha: "UGC");
        var i1 = L("i1", true, "Indicação pessoal"); var semSinal = L("sem-sinal", false, "Lead");
        var perdidoSemVenda = L("so-lead", false, "Lead", meta: "m2");
        db.CrmLeads.AddRange(t1, n1, i1, semSinal, perdidoSemVenda);
        await db.SaveChangesAsync();
        foreach (var (lead, adesao) in new[] { (t1, 100m), (n1, 200m), (i1, 300m), (semSinal, 400m) })
        {
            db.CrmOpportunities.Add(new CrmOpportunity
            {
                LeadId = lead.Id, Titulo = lead.NomeOuRazaoSocial, ResponsavelId = ana.Id, EtapaId = ganho.Id, DataEfetivaFechamento = DateTimeOffset.UtcNow,
                PagamentoAdesao = adesao, TipoIndicacao = lead.TipoIndicacao,
            });
        }
        await db.SaveChangesAsync();

        var usuario = TestDbContextFactory.MockCurrentUser(admin.Id, visaoTotal: true, podeGerir: true);
        var equipe = new EquipeComercialService(db, usuario.Object);
        var relatorio = new RelatorioComercialService(db, usuario.Object, equipe);
        var hoje = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-3));
        Task<RelatorioDetalheDto> Detalhe(string tipo) => relatorio.ObterDetalheAsync(tipo, hoje.AddDays(-1), hoje, null, null, null, CancellationToken.None);

        var totais = (await relatorio.ObterAsync(hoje.AddDays(-1), hoje, null, null, CancellationToken.None)).Totais;

        // Cada card e o tamanho da lista que ele abre.
        Assert.Equal(totais.Leads, (await Detalhe("leads")).Total);
        Assert.Equal(totais.Vendas, (await Detalhe("vendas")).Total);
        Assert.Equal(totais.VendasTrafegoPago, (await Detalhe("trafego-vendas")).Total);
        Assert.Equal(totais.LeadsTrafegoPago, (await Detalhe("trafego-leads")).Total);
        Assert.Equal(totais.VendasIndicacaoCard, (await Detalhe("indicacao-vendas")).Total);
        Assert.Equal(totais.LeadsIndicacaoCard, (await Detalhe("indicacao-leads")).Total);
        Assert.Equal(totais.VendasSemOrigemIdentificada, (await Detalhe("sem-origem-vendas")).Total);

        var adesaoLista = await Detalhe("adesao");
        Assert.Equal(4, adesaoLista.Total);
        Assert.Equal(1000m, adesaoLista.Itens.Sum(i => i.Valor));
        Assert.Equal("Adesão", adesaoLista.RotuloValor);
        Assert.Equal(["i1", "n1", "sem-sinal", "t1"], adesaoLista.Itens.Select(i => i.Cliente).Order());

        // Painel: as fatias do gráfico "Origem das vendas".
        var painel = new DashboardService(db, equipe, new ActivityService(db, usuario.Object, equipe, new NoOpAuditSink()), usuario.Object);
        var filtro = new DashboardFilterRequest(HorarioBrasilia.PrimeiroDiaDoMes(HorarioBrasilia.Hoje), HorarioBrasilia.Hoje, null);
        var origens = (await painel.ObterAsync(filtro, CancellationToken.None)).OrigemVendas.ToDictionary(o => o.Origem, o => o.Quantidade);
        foreach (var (origem, quantidade) in origens)
        {
            var lista = await painel.ObterVendasDaOrigemAsync(filtro, origem, CancellationToken.None);
            Assert.Equal(quantidade, lista.Total);
            Assert.Equal(quantidade, lista.Itens.Count);
        }
    }
}
