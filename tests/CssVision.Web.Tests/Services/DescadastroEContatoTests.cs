using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Api.Controllers;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Link público de "Cancelar inscrição" e o registro de contato (WhatsApp) no lead.</summary>
public class DescadastroEContatoTests
{
    [Fact]
    public async Task LinkPublico_GetSoMostraAConfirmacao_PostCancela()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var lead = new CrmLead { NomeOuRazaoSocial = "Maria", TipoPessoa = TipoPessoa.Fisica, Email = "maria@x.com" };
        db.CrmLeads.Add(lead);
        await db.SaveChangesAsync();
        var tokens = new DescadastroEmailTokens(new EphemeralDataProtectionProvider());
        var controller = new PublicDescadastroController(tokens, db);
        var token = tokens.Gerar(lead.Id);

        var get = controller.Confirmar(token);
        Assert.Equal(200, get.StatusCode);
        Assert.Contains("<form method=\"post\"", get.Content);
        Assert.Null((await db.CrmLeads.AsNoTracking().SingleAsync()).EmailDescadastradoEm); // abrir o link (ou um antivírus abrindo) não cancela

        var post = await controller.Cancelar(token, CancellationToken.None);
        Assert.Equal(200, post.StatusCode);
        Assert.NotNull((await db.CrmLeads.AsNoTracking().SingleAsync()).EmailDescadastradoEm);
    }

    [Fact]
    public async Task LinkPublico_TokenInvalido_Retorna400_ENaoMexeEmNinguem()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        db.CrmLeads.Add(new CrmLead { NomeOuRazaoSocial = "Maria", TipoPessoa = TipoPessoa.Fisica, Email = "maria@x.com" });
        await db.SaveChangesAsync();
        var controller = new PublicDescadastroController(new DescadastroEmailTokens(new EphemeralDataProtectionProvider()), db);

        Assert.Equal(400, controller.Confirmar("lixo").StatusCode);
        Assert.Equal(400, (await controller.Cancelar(null, CancellationToken.None)).StatusCode);
        Assert.Equal(400, (await controller.Cancelar("lixo", CancellationToken.None)).StatusCode);
        Assert.Null((await db.CrmLeads.AsNoTracking().SingleAsync()).EmailDescadastradoEm);
    }

    private static async Task<(LeadService Servico, CrmLead Lead, Guid VendedorId, Domain.Identity.ApplicationUser Outro, Data.ApplicationDbContext Db)> CenarioAsync(TestDbContextFactory factory)
    {
        var db = factory.CreateContext();
        var vendedor = await factory.CriarUsuarioAsync(db, "Vendedor1");
        var outro = await factory.CriarUsuarioAsync(db, "Vendedor2");
        var lead = new CrmLead { NomeOuRazaoSocial = "Maria", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = vendedor.Id };
        db.CrmLeads.Add(lead);
        await db.SaveChangesAsync();
        var currentUser = TestDbContextFactory.MockCurrentUser(vendedor.Id);
        var servico = new LeadService(db, currentUser.Object, new EquipeComercialService(db, currentUser.Object), new NoOpLeadAssignmentService(), new NoOpMetaConversionService(), new NoOpAuditSink());
        return (servico, lead, vendedor.Id, outro, db);
    }

    [Fact]
    public async Task ContatoPeloWhatsApp_AtualizaOUltimoContato_ENotaSoUmaVezPorJanela()
    {
        using var factory = new TestDbContextFactory();
        var (servico, lead, _, _, db) = await CenarioAsync(factory);

        await servico.RegistrarContatoAsync(lead.Id, "whatsapp", CancellationToken.None);
        db.ChangeTracker.Clear();
        await servico.RegistrarContatoAsync(lead.Id, "WhatsApp", CancellationToken.None);

        var salvo = await db.CrmLeads.AsNoTracking().SingleAsync();
        Assert.NotNull(salvo.UltimoContatoEm);
        var nota = await db.CrmNotes.AsNoTracking().SingleAsync(); // dois cliques seguidos: uma nota só
        Assert.Equal("Contato iniciado pelo WhatsApp.", nota.Texto);
    }

    [Fact]
    public async Task ContatoPeloWhatsApp_CanalDesconhecido_Recusa_ELeadDeOutroNaoEncontra()
    {
        using var factory = new TestDbContextFactory();
        var (servico, lead, _, outro, db) = await CenarioAsync(factory);

        var ex = await Assert.ThrowsAsync<CrmBusinessException>(() => servico.RegistrarContatoAsync(lead.Id, "Fax", CancellationToken.None));
        Assert.Equal("canal_invalido", ex.Codigo);

        var currentOutro = TestDbContextFactory.MockCurrentUser(outro.Id);
        var doOutro = new LeadService(db, currentOutro.Object, new EquipeComercialService(db, currentOutro.Object),
            new NoOpLeadAssignmentService(), new NoOpMetaConversionService(), new NoOpAuditSink());
        await Assert.ThrowsAnyAsync<Exception>(() => doOutro.RegistrarContatoAsync(lead.Id, "WhatsApp", CancellationToken.None));
    }
}
