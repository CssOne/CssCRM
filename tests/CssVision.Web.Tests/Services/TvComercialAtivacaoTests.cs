using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>O painel da TV conta a venda no dia em que ela foi ativada ("Ativo em"); sem ativação, vale a data da venda.</summary>
public class TvComercialAtivacaoTests
{
    [Fact]
    public async Task VendaFechadaEmSetembroEAtivadaEmOutubro_ContaEmOutubro_NaoEmSetembro()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var admin = await factory.CriarUsuarioAsync(db, "Admin");
        var isabel = await factory.CriarUsuarioAsync(db, "Isabel");
        var ganho = await factory.CriarEtapaAsync(db, "Ganho", 9, TipoEtapaPipeline.Ganho);
        var lead = new CrmLead { NomeOuRazaoSocial = "Cliente", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = isabel.Id };
        db.CrmLeads.Add(lead);
        await db.SaveChangesAsync();
        CrmOpportunity V(DateTimeOffset venda, DateTimeOffset? ativo) => new()
        {
            LeadId = lead.Id, Titulo = "V", ResponsavelId = isabel.Id, EtapaId = ganho.Id, PagamentoAdesao = 100m,
            DataEfetivaFechamento = venda, AtivoEm = ativo,
        };
        db.CrmOpportunities.AddRange(
            V(new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero)), // ativada em 1º/10
            V(new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 7, 0, 0, 0, TimeSpan.Zero)), // mesma data
            V(new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero), null),                                                   // sem ativação: vale a venda
            V(new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 9, 21, 0, 0, 0, TimeSpan.Zero)));  // setembro
        await db.SaveChangesAsync();

        var usuario = TestDbContextFactory.MockCurrentUser(admin.Id, visaoTotal: true, podeGerir: true).Object;
        var servico = new TvComercialService(db, new EquipeComercialService(db, usuario));

        // Quando o teste roda depois de outubro de 2026, o mês pedido ainda é válido (só se limita ao mês atual para frente).
        var outubro = await servico.ObterAsync(10, 2026, CancellationToken.None);
        var setembro = await servico.ObterAsync(9, 2026, CancellationToken.None);

        Assert.Equal(3, outubro.Resumo.VendasNoMes);
        Assert.Equal(1, setembro.Resumo.VendasNoMes);
    }

    [Fact]
    public void DataDaAtivacao_DiaGravadoAMeiaNoiteViraMeioDia_ESemAtivacaoVaiParaDataDaVenda()
    {
        var venda = new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero), TvComercialService.DataDaAtivacao(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), venda));
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 14, 30, 0, TimeSpan.Zero), TvComercialService.DataDaAtivacao(new DateTimeOffset(2026, 10, 1, 14, 30, 0, TimeSpan.Zero), venda));
        Assert.Equal(venda, TvComercialService.DataDaAtivacao(null, venda));
    }
}
