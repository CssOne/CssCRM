using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Carga única: "Teve indicação" marcado com valor 0,00 (ou sem valor) é desmarcado; com valor, fica.</summary>
public class DesmarcarIndicacaoSemValorTests
{
    [Fact]
    public async Task DesmarcaSoAsIndicacoesSemValor_EMantemOCanalEOValor()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var ana = await factory.CriarUsuarioAsync(db, "Ana");
        var ganho = await factory.CriarEtapaAsync(db, "Ganho", 9, TipoEtapaPipeline.Ganho);
        var lead = new CrmLead { NomeOuRazaoSocial = "C", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = ana.Id };
        db.CrmLeads.Add(lead);
        await db.SaveChangesAsync();
        CrmOpportunity V(string titulo, bool? indicacao, decimal? valor) => new()
        {
            LeadId = lead.Id, Titulo = titulo, ResponsavelId = ana.Id, EtapaId = ganho.Id,
            Indicacao = indicacao, TipoIndicacao = "Pessoal", ValorIndicacao = valor,
        };
        db.CrmOpportunities.AddRange(V("zero", true, 0m), V("sem-valor", true, null), V("com-valor", true, 50m), V("ja-desmarcada", false, 0m));
        await db.SaveChangesAsync();

        var desmarcadas = await DesmarcarIndicacaoSemValorBackgroundService.ExecutarAsync(db, CancellationToken.None);

        Assert.Equal(2, desmarcadas);
        db.ChangeTracker.Clear();
        var todas = await db.CrmOpportunities.AsNoTracking().ToDictionaryAsync(o => o.Titulo);
        Assert.False(todas["zero"].Indicacao);
        Assert.False(todas["sem-valor"].Indicacao);
        Assert.True(todas["com-valor"].Indicacao);
        Assert.Equal("Pessoal", todas["zero"].TipoIndicacao); // o canal de aquisição não é mexido
    }
}
