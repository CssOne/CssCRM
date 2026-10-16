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

    [Fact]
    public async Task VendaDoCardFeitoAMao_PassaParaOLeadDoAnuncio_EOCardEArquivado()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var colunaLeads = await factory.ObterOuCriarEtapaLeadAsync(db, "Venda concluída (Leads)", 8);
        var colunaIndicacao = await factory.ObterOuCriarEtapaLeadAsync(db, "Venda concluída (Indicação)", 9);
        var ana = await factory.CriarUsuarioAsync(db, "Ana");
        var ganho = await factory.CriarEtapaAsync(db, "Ganho", 9, TipoEtapaPipeline.Ganho);
        var anuncio = new CrmLead { NomeOuRazaoSocial = "Meta", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = ana.Id, TipoIndicacao = "Lead", MetaLeadId = "m1", EtapaId = colunaLeads.Id };
        var card = new CrmLead { NomeOuRazaoSocial = "Notion", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = ana.Id, TipoIndicacao = "Indicação", CriadoManualmente = true, EtapaId = colunaIndicacao.Id, DocumentoNormalizado = "123" };
        db.CrmLeads.AddRange(anuncio, card);
        await db.SaveChangesAsync();
        db.CrmOpportunities.Add(new CrmOpportunity { LeadId = anuncio.Id, Titulo = "vazia", ResponsavelId = ana.Id, EtapaId = ganho.Id });
        var real = new CrmOpportunity { LeadId = card.Id, Titulo = "real", ResponsavelId = ana.Id, EtapaId = ganho.Id, PagamentoAdesao = 550m };
        db.CrmOpportunities.Add(real);
        await db.SaveChangesAsync();

        var unidos = await CardsOriginaisDeAnuncioVoltamParaLeadBackgroundService.UnirDuplicadosAsync(db, [(card.Id, anuncio.Id)], CancellationToken.None);
        db.ChangeTracker.Clear();

        Assert.Equal(1, unidos);
        Assert.Equal(anuncio.Id, (await db.CrmOpportunities.FirstAsync(o => o.Id == real.Id)).LeadId);
        Assert.True((await db.CrmLeads.FirstAsync(l => l.Id == card.Id)).Arquivado);
        Assert.True((await db.CrmOpportunities.FirstAsync(o => o.Titulo == "vazia")).Arquivado);
        var lead = await db.CrmLeads.FirstAsync(l => l.Id == anuncio.Id);
        Assert.Equal("123", lead.DocumentoNormalizado);
        Assert.Equal(colunaLeads.Id, lead.EtapaId);
    }
}
