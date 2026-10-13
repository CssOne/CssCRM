using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Services.Notion;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Card de tráfego pago: direto do anúncio/site, ou card do Notion com sinal de anúncio (ID do Meta, GCLID, UTM, campanha, tag Facebook/Google).</summary>
public class TrafegoPagoDoCardTests
{
    [Theory]
    [InlineData("Facebook ADS", true)]
    [InlineData("facebook ads", true)]
    [InlineData("Google ads", true)]
    [InlineData(" Google ads ", true)]
    [InlineData("Orgânico", false)]
    [InlineData("CAMINHÃO", false)]
    [InlineData("BRASIL", false)]
    [InlineData(null, false)]
    public void TagDeAnuncio(string? tag, bool esperado) => Assert.Equal(esperado, OrigemLead.EhTagDeAnuncio(tag));

    [Fact]
    public void LeadSemNenhumSinal_NaoEhDeTrafegoPago() =>
        Assert.False(OrigemLead.DeTrafegoPago(new CrmLead { ConsentimentoOrigem = OrigemLead.MarcadorSincronizacaoNotion }));

    [Theory]
    [InlineData("meta")]
    [InlineData("gclid")]
    [InlineData("utm")]
    [InlineData("tag")]
    [InlineData("site")]
    [InlineData("campanha-notion")]
    public void CadaSinalDeAnuncio_BastaParaSerDeTrafegoPago(string sinal)
    {
        var lead = new CrmLead { ConsentimentoOrigem = OrigemLead.MarcadorSincronizacaoNotion };
        switch (sinal)
        {
            case "meta": lead.MetaLeadId = "123"; break;
            case "gclid": lead.Gclid = "abc"; break;
            case "utm": lead.UtmSource = "facebook"; break;
            case "tag": lead.TagDeAnuncioNotion = true; break;
            case "site": lead.ConsentimentoOrigem = OrigemLead.MarcadorFormularioSite; break;
            default: lead.Campanha = "Lookalike"; break;
        }

        Assert.True(OrigemLead.DeTrafegoPago(lead));
    }

    [Fact]
    public void CampanhaSoContaParaCardsDoNotion() =>
        // Mesma regra de DeTrafegoPagoInclusiveNotion: a campanha de um lead criado no CRM não é sinal de anúncio.
        Assert.False(OrigemLead.DeTrafegoPago(new CrmLead { ConsentimentoOrigem = "Cadastro manual", Campanha = "Lookalike" }));

    [Fact]
    public async Task CardDeOutroVeiculo_HerdaOSinalDeAnuncioDoCardPrincipal()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var vendedor = await factory.CriarUsuarioAsync(db, "Vendedor");
        var principal = new CrmLead { NomeOuRazaoSocial = "Cliente", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = vendedor.Id, TagDeAnuncioNotion = true, ConsentimentoOrigem = OrigemLead.MarcadorSincronizacaoNotion };
        db.CrmLeads.Add(principal);
        await db.SaveChangesAsync();
        var adicional = new CrmLead { NomeOuRazaoSocial = "Cliente", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = vendedor.Id, VeiculoAdicionalDeLeadId = principal.Id };
        var avulso = new CrmLead { NomeOuRazaoSocial = "Outro", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = vendedor.Id };
        db.CrmLeads.AddRange(adicional, avulso);
        await db.SaveChangesAsync();

        Assert.True(await ClassificacaoDoCard.DeTrafegoPagoAsync(db, adicional, CancellationToken.None));
        Assert.False(await ClassificacaoDoCard.DeTrafegoPagoAsync(db, avulso, CancellationToken.None));
    }

    [Fact]
    public async Task CargaDaTag_MarcaSoOsLeadsLigadosAosCardsComATag()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var vendedor = await factory.CriarUsuarioAsync(db, "Vendedor");
        CrmLead Novo(string nome, string? pagina) => new() { NomeOuRazaoSocial = nome, TipoPessoa = TipoPessoa.Fisica, ResponsavelId = vendedor.Id, NotionPageId = pagina };
        var comTag = Novo("Com tag", "pagina-1");
        var outroComTag = Novo("Outro com tag", "pagina-2");
        var semTag = Novo("Sem tag", "pagina-3");
        var semNotion = Novo("Sem Notion", null);
        db.CrmLeads.AddRange(comTag, outroComTag, semTag, semNotion);
        await db.SaveChangesAsync();

        var marcados = await PreencherTagDeAnuncioBackgroundService.MarcarAsync(db, ["pagina-1", "pagina-2", "pagina-inexistente"], CancellationToken.None);

        Assert.Equal(2, marcados);
        db.ChangeTracker.Clear();
        var porNome = await db.CrmLeads.AsNoTracking().ToDictionaryAsync(l => l.NomeOuRazaoSocial, l => l.TagDeAnuncioNotion);
        Assert.True(porNome["Com tag"]);
        Assert.True(porNome["Outro com tag"]);
        Assert.False(porNome["Sem tag"]);
        Assert.False(porNome["Sem Notion"]);

        // Rodar de novo não remarca o que já está marcado.
        Assert.Equal(0, await PreencherTagDeAnuncioBackgroundService.MarcarAsync(db, ["pagina-1", "pagina-2"], CancellationToken.None));
    }
}
