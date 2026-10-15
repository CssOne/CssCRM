using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CssVision.Web.Tests.Services;

public class CardsDeAnuncioVoltamParaLeadTests
{
    [Fact]
    public async Task CardOriginalComUmaVenda_VoltaParaLead_ComDuasVendasOuVeiculoAdicional_FicaComoEsta()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var colunaLeads = await factory.ObterOuCriarEtapaLeadAsync(db, "Venda concluída (Leads)", 8);
        var colunaIndicacao = await factory.ObterOuCriarEtapaLeadAsync(db, "Venda concluída (Indicação)", 9);
        var ana = await factory.CriarUsuarioAsync(db, "Ana");
        var ganho = await factory.CriarEtapaAsync(db, "Ganho", 9, TipoEtapaPipeline.Ganho);

        async Task<CrmLead> Card(string nome, int vendas, Guid? adicionalDe = null)
        {
            var lead = new CrmLead
            {
                NomeOuRazaoSocial = nome, TipoPessoa = TipoPessoa.Fisica, ResponsavelId = ana.Id, TipoIndicacao = "Indicação Lead", CriadoManualmente = true,
                EtapaId = colunaIndicacao.Id, MetaLeadId = "m-" + nome, VeiculoAdicionalDeLeadId = adicionalDe,
            };
            db.CrmLeads.Add(lead);
            await db.SaveChangesAsync();
            for (var i = 0; i < vendas; i++)
                db.CrmOpportunities.Add(new CrmOpportunity { LeadId = lead.Id, Titulo = "V", ResponsavelId = ana.Id, EtapaId = ganho.Id, TipoIndicacao = "Indicação Lead" });
            await db.SaveChangesAsync();
            return lead;
        }

        var umaVenda = await Card("um", 1);
        var duasVendas = await Card("dois", 2);
        var adicional = await Card("adicional", 1, adicionalDe: umaVenda.Id);

        var corrigidos = await CardsOriginaisDeAnuncioVoltamParaLeadBackgroundService.ExecutarAsync(db, [umaVenda.Id, duasVendas.Id, adicional.Id], CancellationToken.None);
        db.ChangeTracker.Clear();

        Assert.Equal(1, corrigidos);
        var corrigido = await db.CrmLeads.FirstAsync(l => l.Id == umaVenda.Id);
        Assert.Equal("Lead", corrigido.TipoIndicacao);
        Assert.Equal(colunaLeads.Id, corrigido.EtapaId);
        Assert.True(corrigido.TipoIndicacaoEditadoNoCrm);
        Assert.Null((await db.CrmOpportunities.FirstAsync(o => o.LeadId == umaVenda.Id)).TipoIndicacao);
        Assert.Equal("Indicação Lead", (await db.CrmLeads.FirstAsync(l => l.Id == duasVendas.Id)).TipoIndicacao);
        Assert.Equal(colunaIndicacao.Id, (await db.CrmLeads.FirstAsync(l => l.Id == adicional.Id)).EtapaId);
    }
}
