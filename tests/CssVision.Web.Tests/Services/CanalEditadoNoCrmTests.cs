using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>
/// O Canal de Aquisição escolhido no CRM não volta ao valor do Notion: a edição grava a marca que a sincronização respeita
/// (CrmLead.TipoIndicacaoEditadoNoCrm), e a etiqueta do quadro passa a mostrar o canal novo.
/// </summary>
public class CanalEditadoNoCrmTests
{
    [Fact]
    public async Task EditarOCanal_GravaAMarcaDoCrm_ESalvarSemMudarONaoGrava()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var admin = await factory.CriarUsuarioAsync(db, "Admin");
        var lead = new CrmLead
        {
            NomeOuRazaoSocial = "Cliente", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = admin.Id, TipoIndicacao = "Pessoal", CriadoManualmente = true,
            ConsentimentoOrigem = OrigemLead.MarcadorSincronizacaoNotion,
        };
        db.CrmLeads.Add(lead);
        await db.SaveChangesAsync();

        var usuario = TestDbContextFactory.MockCurrentUser(admin.Id, visaoTotal: true, podeGerir: true);
        var leads = new LeadService(db, usuario.Object, new EquipeComercialService(db, usuario.Object),
            new NoOpLeadAssignmentService(), new NoOpMetaConversionService(), new NoOpAuditSink());

        LeadUpdateRequest Pedido(string tipo, uint rowVersion) => new("Cliente", TipoPessoa.Fisica, null, "31999990000", null, null, null, null, "BH", "MG", "MG132", null, null, null, "ABC1D23",
            null, null, null, null, null, null, null, null, null, null, tipo, null, null, false, OrigemLead.MarcadorSincronizacaoNotion, rowVersion);

        // Salva sem mexer no canal: nada de marca.
        var detalhe = await leads.ObterPorIdAsync(lead.Id, CancellationToken.None);
        await leads.AtualizarAsync(lead.Id, Pedido("Pessoal", detalhe.RowVersion), CancellationToken.None);
        db.ChangeTracker.Clear();
        Assert.False((await db.CrmLeads.AsNoTracking().SingleAsync(l => l.Id == lead.Id)).TipoIndicacaoEditadoNoCrm);

        // Troca o canal: marca, e o quadro mostra o canal novo.
        detalhe = await leads.ObterPorIdAsync(lead.Id, CancellationToken.None);
        await leads.AtualizarAsync(lead.Id, Pedido("Parceria", detalhe.RowVersion), CancellationToken.None);
        db.ChangeTracker.Clear();
        var salvo = await db.CrmLeads.AsNoTracking().SingleAsync(l => l.Id == lead.Id);
        Assert.True(salvo.TipoIndicacaoEditadoNoCrm);
        Assert.Equal("Parceria", salvo.TipoIndicacao);
    }
}
