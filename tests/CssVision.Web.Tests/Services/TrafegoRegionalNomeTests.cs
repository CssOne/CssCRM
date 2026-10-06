using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Xunit;

namespace CssVision.Web.Tests.Services;

public class TrafegoRegionalNomeTests
{
    [Theory]
    [InlineData("MG 132", "MG132")]
    [InlineData("mg132", "MG132")]
    [InlineData(" MG134 ", "MG134")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void NomeDaRegionalNormalizado(string? entrada, string? esperado) => Assert.Equal(esperado, MarketingService.NormalizarRegional(entrada));

    [Fact]
    public async Task VariacoesDoNomeViramUmaSoRegionalNoTrafegoPago()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        CrmLead L(string nome, string? regional) => new()
        {
            NomeOuRazaoSocial = nome, TipoPessoa = TipoPessoa.Fisica, MetaLeadId = Guid.NewGuid().ToString(), Regional = regional,
        };
        db.CrmLeads.AddRange(L("a", "MG132"), L("b", "MG 132"), L("c", "mg132"), L("d", "MG134"));
        await db.SaveChangesAsync();

        var painel = await new MarketingService(db).ObterAsync(new MarketingFilterRequest(), CancellationToken.None);

        Assert.Equal(["MG132", "MG134"], painel.PorRegional!.Select(r => r.Regional));
        Assert.Equal([3, 1], painel.PorRegional!.Select(r => r.TotalLeads));
        Assert.Equal(["MG132", "MG134"], painel.Opcoes!.Regionais);
        var so132 = await new MarketingService(db).ObterAsync(new MarketingFilterRequest { Regional = ["MG 132"] }, CancellationToken.None);
        Assert.Equal(3, so132.Indicadores.TotalLeads);
    }
}
