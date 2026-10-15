using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>A venda do "Outro veículo" de um cliente do tráfego pago é venda de indicação: "Indicação Lead", coluna das indicações, sem a etiqueta do produto nem a do anúncio.</summary>
public class OutroVeiculoComoIndicacaoLeadTests
{
    [Fact]
    public async Task CargaPassaOOutroVeiculoDeLeadParaIndicacaoLead_ESoEle()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var ketelyn = await factory.CriarUsuarioAsync(db, "Ketelyn");
        var ganho = await factory.CriarEtapaAsync(db, "Ganho", 9, TipoEtapaPipeline.Ganho);
        var vcLeads = await factory.ObterOuCriarEtapaLeadAsync(db, "Venda concluída (Leads)", 5);
        var vcIndicacao = await factory.ObterOuCriarEtapaLeadAsync(db, "Venda concluída (Indicação)", 6);

        CrmLead Lead(string nome, Guid etapa, string? tipo, bool manual, Guid? principal = null, string? origem = "Meta ads", string? produto = "AGV") => new()
        {
            NomeOuRazaoSocial = nome, TipoPessoa = TipoPessoa.Fisica, ResponsavelId = ketelyn.Id, EtapaId = etapa, TipoIndicacao = tipo, CriadoManualmente = manual,
            VeiculoAdicionalDeLeadId = principal, Origem = origem, ProdutoInteresse = produto, Placa = nome.Replace(" ", "").ToUpperInvariant(),
        };
        var principalLead = Lead("principal", vcLeads.Id, null, false);
        var principalIndicacao = Lead("principal indicacao", vcIndicacao.Id, "Pessoal", true);
        db.CrmLeads.AddRange(principalLead, principalIndicacao);
        await db.SaveChangesAsync();
        var outroVeiculo = Lead("claudio hmn", vcLeads.Id, null, false, principalLead.Id);                      // o caso
        var outroDeLeadComTipo = Lead("matheus gey", vcLeads.Id, "Lead", false, principalLead.Id);             // tipo "Lead": também
        var outroDeIndicacao = Lead("outro de indicacao", vcIndicacao.Id, "Pessoal", true, principalIndicacao.Id); // já é indicação: não mexe
        var venda = Lead("venda normal", vcLeads.Id, "Lead", false);                                          // não é outro veículo: não mexe
        db.CrmLeads.AddRange(outroVeiculo, outroDeLeadComTipo, outroDeIndicacao, venda);
        await db.SaveChangesAsync();
        foreach (var l in new[] { outroVeiculo, outroDeLeadComTipo, outroDeIndicacao, venda })
        {
            db.CrmOpportunities.Add(new CrmOpportunity { LeadId = l.Id, Titulo = l.NomeOuRazaoSocial, ResponsavelId = ketelyn.Id, EtapaId = ganho.Id, DataEfetivaFechamento = DateTimeOffset.UtcNow });
        }
        await db.SaveChangesAsync();

        var placas = await OutroVeiculoComoIndicacaoLeadBackgroundService.ExecutarAsync(db, CancellationToken.None);

        Assert.Equal(["CLAUDIOHMN", "MATHEUSGEY"], placas.Order());
        db.ChangeTracker.Clear();
        var leads = await db.CrmLeads.AsNoTracking().ToDictionaryAsync(l => l.NomeOuRazaoSocial);
        foreach (var nome in new[] { "claudio hmn", "matheus gey" })
        {
            var l = leads[nome];
            Assert.Equal("Indicação Lead", l.TipoIndicacao);
            Assert.Equal(vcIndicacao.Id, l.EtapaId);          // coluna Venda concluída (Indicação)
            Assert.Null(l.ProdutoInteresse);                    // sem a etiqueta AGV
            Assert.Null(l.Origem);                              // sem a etiqueta do anúncio (Meta ads)
            Assert.True(l.TipoIndicacaoEditadoNoCrm);           // a sincronização não desfaz
        }
        Assert.Equal(("Lead", vcLeads.Id, "AGV", "Meta ads"), (leads["venda normal"].TipoIndicacao, leads["venda normal"].EtapaId, leads["venda normal"].ProdutoInteresse, leads["venda normal"].Origem));
        Assert.Equal(("Pessoal", "AGV"), (leads["outro de indicacao"].TipoIndicacao, leads["outro de indicacao"].ProdutoInteresse));

        var vendas = await db.CrmOpportunities.AsNoTracking().ToDictionaryAsync(o => o.Titulo);
        Assert.Equal("Indicação Lead", vendas["claudio hmn"].TipoIndicacao);
        Assert.Null(vendas["venda normal"].TipoIndicacao);

        // Rodar de novo não muda nada (já é indicação).
        Assert.Empty(await OutroVeiculoComoIndicacaoLeadBackgroundService.ExecutarAsync(db, CancellationToken.None));
    }
}
