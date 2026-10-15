using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CssVision.Web.Tests.Services;

public class UnificarContasDoSamysTests
{
    [Fact]
    public async Task TudoDaContaDeConsultorVaiParaAdministrador_EElaSome()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        using var userManager = TestDbContextFactory.CreateUserManager(db);
        var consultor = await factory.CriarUsuarioAsync(db, "Samys Alexandre");
        var admin = await factory.CriarUsuarioAsync(db, "Samys Admin");
        var outro = await factory.CriarUsuarioAsync(db, "Outro");
        consultor.Email = UnificarContasDoSamysBackgroundService.EmailConsultor; consultor.NotionUserId = "notion-1";
        admin.Email = UnificarContasDoSamysBackgroundService.EmailAdministrador;
        var ganho = await factory.CriarEtapaAsync(db, "Ganho", 9, TipoEtapaPipeline.Ganho);
        var lead = new CrmLead { NomeOuRazaoSocial = "C", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = consultor.Id };
        var doOutro = new CrmLead { NomeOuRazaoSocial = "D", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = outro.Id };
        db.CrmLeads.AddRange(lead, doOutro);
        await db.SaveChangesAsync();
        db.CrmOpportunities.Add(new CrmOpportunity { LeadId = lead.Id, Titulo = "V", ResponsavelId = consultor.Id, EtapaId = ganho.Id, DataEfetivaFechamento = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var resumo = await UnificarContasDoSamysBackgroundService.ExecutarAsync(db, userManager, CancellationToken.None);
        db.ChangeTracker.Clear();

        Assert.Contains("movidos", resumo);
        Assert.Equal(admin.Id, (await db.CrmLeads.FirstAsync(l => l.Id == lead.Id)).ResponsavelId);
        Assert.Equal(admin.Id, (await db.CrmOpportunities.FirstAsync(o => o.LeadId == lead.Id)).ResponsavelId);
        Assert.Equal(outro.Id, (await db.CrmLeads.FirstAsync(l => l.Id == doOutro.Id)).ResponsavelId);
        var contaAdmin = await db.Users.FirstAsync(u => u.Id == admin.Id);
        Assert.False(contaAdmin.AtuaNasVendas);
        Assert.Equal("notion-1", contaAdmin.NotionUserId);
        var contaAntiga = await db.Users.FirstOrDefaultAsync(u => u.Id == consultor.Id);
        Assert.True(contaAntiga is null || !contaAntiga.Ativo);
    }
}
