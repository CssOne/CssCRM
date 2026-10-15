using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Services.Notion;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>O Canal de Aquisição "Pessoal" passou a se chamar "Indicação pessoal": no Notion, nas listas e nos dados já gravados.</summary>
public class RenomearCanalPessoalTests
{
    [Theory]
    [InlineData("PESSOAL")]
    [InlineData("Pessoal")]
    [InlineData("pessoal ")]
    [InlineData("Indicação pessoal")]
    [InlineData("INDICACAO PESSOAL")]
    public void NotionPessoal_ViraIndicacaoPessoal(string valor) =>
        Assert.Equal("Indicação pessoal", NotionSyncService.NormalizarTipoIndicacao(valor));

    [Fact]
    public async Task CargaRenomeiaLeadsEVendas_EDeixaOsOutrosCanais()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var ana = await factory.CriarUsuarioAsync(db, "Ana");
        var ganho = await factory.CriarEtapaAsync(db, "Ganho", 9, TipoEtapaPipeline.Ganho);
        CrmLead L(string nome, string? tipo) => new() { NomeOuRazaoSocial = nome, TipoPessoa = TipoPessoa.Fisica, ResponsavelId = ana.Id, TipoIndicacao = tipo };
        var a = L("a", "Pessoal"); var b = L("b", "PESSOAL"); var c = L("c", "Parceria"); var d = L("d", "Indicação Lead");
        db.CrmLeads.AddRange(a, b, c, d);
        await db.SaveChangesAsync();
        db.CrmOpportunities.AddRange(
            new CrmOpportunity { LeadId = a.Id, Titulo = "v1", ResponsavelId = ana.Id, EtapaId = ganho.Id, TipoIndicacao = "Pessoal" },
            new CrmOpportunity { LeadId = c.Id, Titulo = "v2", ResponsavelId = ana.Id, EtapaId = ganho.Id, TipoIndicacao = "Parceria" });
        await db.SaveChangesAsync();

        var (leads, vendas) = await RenomearCanalPessoalBackgroundService.ExecutarAsync(db, CancellationToken.None);

        Assert.Equal((2, 1), (leads, vendas));
        db.ChangeTracker.Clear();
        var tipos = await db.CrmLeads.AsNoTracking().ToDictionaryAsync(l => l.NomeOuRazaoSocial, l => l.TipoIndicacao);
        Assert.Equal(["Indicação pessoal", "Indicação pessoal", "Parceria", "Indicação Lead"], [tipos["a"], tipos["b"], tipos["c"], tipos["d"]]);
        Assert.Equal("Indicação pessoal", (await db.CrmOpportunities.AsNoTracking().SingleAsync(o => o.Titulo == "v1")).TipoIndicacao);
        Assert.Equal("Parceria", (await db.CrmOpportunities.AsNoTracking().SingleAsync(o => o.Titulo == "v2")).TipoIndicacao);
    }
}
