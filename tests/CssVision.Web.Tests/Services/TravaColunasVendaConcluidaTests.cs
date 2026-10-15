using CssVision.Web.Domain.Crm;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Nenhum caminho deixa card de Indicação em "Venda concluída (Leads)" nem card Lead em "Venda concluída (Indicação)".</summary>
public class TravaColunasVendaConcluidaTests
{
    [Fact]
    public async Task CardGravadoNaColunaErrada_VaiParaAColunaDaEtiqueta()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var colunaLeads = await factory.ObterOuCriarEtapaLeadAsync(db, "Venda concluída (Leads)", 8);
        var colunaIndicacao = await factory.ObterOuCriarEtapaLeadAsync(db, "Venda concluída (Indicação)", 9);

        CrmLead Novo(string nome, string? tipo, bool manual, Guid etapa) =>
            new() { NomeOuRazaoSocial = nome, TipoPessoa = TipoPessoa.Fisica, TipoIndicacao = tipo, CriadoManualmente = manual, EtapaId = etapa };
        var indicacaoNaColunaLeads = Novo("ind", "Indicação", true, colunaLeads.Id);
        var leadNaColunaIndicacao = Novo("lead", "Lead", false, colunaIndicacao.Id);
        var indicacaoLeadNaColunaIndicacao = Novo("indlead", "Indicação Lead", false, colunaIndicacao.Id);
        db.CrmLeads.AddRange(indicacaoNaColunaLeads, leadNaColunaIndicacao, indicacaoLeadNaColunaIndicacao);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        Assert.Equal(colunaIndicacao.Id, (await db.CrmLeads.FirstAsync(l => l.Id == indicacaoNaColunaLeads.Id)).EtapaId);
        Assert.Equal(colunaLeads.Id, (await db.CrmLeads.FirstAsync(l => l.Id == leadNaColunaIndicacao.Id)).EtapaId);
        Assert.Equal(colunaIndicacao.Id, (await db.CrmLeads.FirstAsync(l => l.Id == indicacaoLeadNaColunaIndicacao.Id)).EtapaId);

        // Edição: o canal muda para Indicação e o card, que estava em "(Leads)", acompanha.
        var editado = await db.CrmLeads.FirstAsync(l => l.Id == leadNaColunaIndicacao.Id);
        editado.TipoIndicacao = "Indicação";
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        Assert.Equal(colunaIndicacao.Id, (await db.CrmLeads.FirstAsync(l => l.Id == leadNaColunaIndicacao.Id)).EtapaId);
    }
}
