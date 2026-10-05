using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Services.Notion;
using CssVision.Web.Tests.Infrastructure;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Painel da TV com vendas só do Notion (MG134): nunca contam duas vezes e respeitam o escopo da regional.</summary>
public class TvComercialNotionTests
{
    private sealed class NotionFalso(IReadOnlyList<TvNotionVenda> vendas, TvAdministrativoDto? admin = null) : ITvNotionFonte
    {
        public long Versao => 1;
        public Task<IReadOnlyList<TvNotionVenda>> VendasAsync(DateOnly primeiroDiaDoMes, CancellationToken ct) => Task.FromResult(vendas);
        public Task<TvAdministrativoDto?> AdministrativoAsync(DateOnly primeiroDiaDoMes, CancellationToken ct) => Task.FromResult(admin);
    }

    private static TvComercialService Servico(ApplicationDbContext db, Guid usuarioId, ITvNotionFonte notion, bool visaoTotal = true)
    {
        var usuario = TestDbContextFactory.MockCurrentUser(usuarioId, visaoTotal: visaoTotal, gestorComercial: !visaoTotal, podeGerir: true).Object;
        return new TvComercialService(db, new EquipeComercialService(db, usuario), usuario, notion);
    }

    private static TvNotionVenda Venda(string pageId, string consultor, decimal adesao, string? placa = null, string? notionUserId = null, string? email = null) =>
        new(pageId, consultor, notionUserId ?? "notion-" + consultor, email, null, "Cliente " + pageId[..4], TvNotionFonte.Normalizar(placa), adesao,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "MG134");

    [Fact]
    public async Task VendaDoNotion_NaoContaDuasVezes_ENaoPerdeAsQueSoExistemLa()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var admin = await factory.CriarUsuarioAsync(db, "Admin");
        var ana = await factory.CriarUsuarioAsync(db, "Ana");
        var regional = new CrmRegional { Nome = "MG134" };
        db.CrmRegionais.Add(regional);
        await db.SaveChangesAsync();
        var ganho = await factory.CriarEtapaAsync(db, "Ganho", 9, TipoEtapaPipeline.Ganho);

        // CRM: uma venda importada do Notion (card conhecido) e uma registrada na mão (placa ABC1D23).
        var l1 = new CrmLead { NomeOuRazaoSocial = "C1", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = ana.Id };
        var l2 = new CrmLead { NomeOuRazaoSocial = "C2", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = ana.Id };
        db.CrmLeads.AddRange(l1, l2);
        await db.SaveChangesAsync();
        var cardJaImportado = Guid.NewGuid().ToString();
        db.CrmOpportunities.AddRange(
            new CrmOpportunity { LeadId = l1.Id, Titulo = "V", ResponsavelId = ana.Id, EtapaId = ganho.Id, PagamentoAdesao = 100, DataEfetivaFechamento = DateTimeOffset.UtcNow, NotionPageId = cardJaImportado },
            new CrmOpportunity
            {
                LeadId = l2.Id, Titulo = "V", ResponsavelId = ana.Id, EtapaId = ganho.Id, PagamentoAdesao = 50, DataEfetivaFechamento = DateTimeOffset.UtcNow,
                Veiculo = new CrmVeiculo { Placa = "ABC1D23" },
            });
        await db.SaveChangesAsync();

        var notion = new NotionFalso(
        [
            Venda(cardJaImportado, "Ana", 100),                                   // mesmo card que o CRM já tem → não conta de novo
            Venda(Guid.NewGuid().ToString(), "Bruno", 70, placa: "abc-1d23"),     // mesma placa de venda do CRM → não conta de novo
            Venda(Guid.NewGuid().ToString(), "Bruno", 300, placa: "XYZ9K88"),     // só no Notion → conta
            Venda(Guid.NewGuid().ToString(), "Bruno", 300, placa: "xyz-9k88"),    // repetida dentro do Notion (outra base) → conta uma vez
            Venda(Guid.NewGuid().ToString(), "Carla", 80),                        // só no Notion, sem placa → conta
        ]);

        var r = await Servico(db, admin.Id, notion).ObterAsync(null, null, CancellationToken.None);

        Assert.Equal(4, r.Resumo.VendasNoMes);          // 2 do CRM + Bruno + Carla
        Assert.Equal(2, r.VendasSoNoNotion);
        Assert.Equal(530m, r.Resumo.ValorNoMes);        // 100 + 50 + 300 + 80
        Assert.Contains(r.RankingConsultores, c => c.Nome == "Bruno" && c.QuantidadeVendas == 1 && c.Regional == "MG134");
        Assert.Contains(r.RankingConsultores, c => c.Nome == "Carla" && c.QuantidadeVendas == 1);
        Assert.Equal(2, r.RankingConsultores.Single(c => c.Nome == "Ana").QuantidadeVendas); // as vendas dela não dobraram
        Assert.Equal(4, r.UltimasVendas.Count);
    }

    [Fact]
    public async Task ConsultorDoNotionComUsuarioNoCrm_SomaNaMesmaLinhaDoRanking()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var admin = await factory.CriarUsuarioAsync(db, "Admin");
        var ana = await factory.CriarUsuarioAsync(db, "Ana");
        ana.NotionUserId = "notion-ana";
        await db.SaveChangesAsync();

        var notion = new NotionFalso([Venda(Guid.NewGuid().ToString(), "Ana (Notion)", 200, notionUserId: "notion-ana")]);

        var r = await Servico(db, admin.Id, notion).ObterAsync(null, null, CancellationToken.None);

        var linha = Assert.Single(r.RankingConsultores);
        Assert.Equal(ana.Id, linha.ConsultorId);
        Assert.Equal("Ana", linha.Nome);
        Assert.Equal(200m, linha.ValorVendido);
    }

    [Fact]
    public async Task GestorDeOutraRegional_NaoVeVendasDoMg134()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var gestor = await factory.CriarUsuarioAsync(db, "Gestor");
        var r132 = new CrmRegional { Nome = "MG132" };
        var r134 = new CrmRegional { Nome = "MG134" };
        db.CrmRegionais.AddRange(r132, r134);
        await db.SaveChangesAsync();
        gestor.RegionalId = r132.Id;
        await db.SaveChangesAsync();
        var notion = new NotionFalso([Venda(Guid.NewGuid().ToString(), "Bruno", 300)]);

        var deOutraRegional = await Servico(db, gestor.Id, notion, visaoTotal: false).ObterAsync(null, null, CancellationToken.None);
        Assert.Empty(deOutraRegional.RankingConsultores);
        Assert.Equal(0, deOutraRegional.VendasSoNoNotion);

        gestor.RegionalId = r134.Id;
        await db.SaveChangesAsync();
        var domg134 = await Servico(db, gestor.Id, notion, visaoTotal: false).ObterAsync(null, null, CancellationToken.None);
        Assert.Single(domg134.RankingConsultores);
    }

    [Fact]
    public async Task Administrativo_ChegaAoPainel_EFaltaDoNotionNaoQuebra()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var admin = await factory.CriarUsuarioAsync(db, "Admin");
        var dto = new TvAdministrativoDto([new TvAdministrativoIndicadorDto("tracker", "Rastreadores", "Rastreador instalado", 3, 1, null)]);

        var com = await Servico(db, admin.Id, new NotionFalso([], dto)).ObterAsync(null, null, CancellationToken.None);
        var sem = await Servico(db, admin.Id, new NotionFalso([], null)).ObterAsync(null, null, CancellationToken.None);

        Assert.Equal(3, Assert.Single(com.Administrativo!.Indicadores).Total);
        Assert.Null(sem.Administrativo);
    }

    [Theory]
    [InlineData("abc-1d23", "ABC1D23")]
    [InlineData(" ole0e76 ", "OLE0E76")]
    [InlineData("123", null)]
    [InlineData(null, null)]
    public void PlacaNormalizada(string? entrada, string? esperado) => Assert.Equal(esperado, TvNotionFonte.Normalizar(entrada));
}
