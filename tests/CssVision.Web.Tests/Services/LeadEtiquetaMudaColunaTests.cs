using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>
/// Trocar o "Canal de Aquisição" (Lead ↔ Indicação) ao editar o cadastro leva o card para a coluna irmã. Antes só a etiqueta mudava: uma venda de
/// indicação ficou com a etiqueta "Indicação Lead" dentro de "Venda concluída (Leads)".
/// </summary>
public class LeadEtiquetaMudaColunaTests
{
    private static async Task<(LeadService Servico, Guid LeadId, CssVision.Web.Data.ApplicationDbContext Db)> MontarAsync(
        TestDbContextFactory factory, string etapaDoLead, string? tipoInicial, bool criarIrma = true, bool manual = false)
    {
        var db = factory.CreateContext();
        var admin = await factory.CriarUsuarioAsync(db, "Admin");
        var etapa = await factory.ObterOuCriarEtapaLeadAsync(db, etapaDoLead, 5);
        foreach (var nome in new[] { "Venda concluída (Leads)", "Venda concluída (Indicação)", "Em atendimento (Leads)", "Em atendimento (Indicação)" })
        {
            if (nome == etapaDoLead || (!criarIrma && nome != etapaDoLead)) continue;
            await factory.ObterOuCriarEtapaLeadAsync(db, nome, 6);
        }

        var lead = new CrmLead
        {
            NomeOuRazaoSocial = "Katy", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = admin.Id, EtapaId = etapa.Id,
            TipoIndicacao = tipoInicial, CriadoManualmente = manual,
        };
        db.CrmLeads.Add(lead);
        await db.SaveChangesAsync();

        var currentUser = TestDbContextFactory.MockCurrentUser(admin.Id, visaoTotal: true);
        var servico = new LeadService(db, currentUser.Object, new EquipeComercialService(db, currentUser.Object),
            new NoOpLeadAssignmentService(), new NoOpMetaConversionService(), new NoOpAuditSink());
        return (servico, lead.Id, db);
    }

    private static LeadUpdateRequest Pedido(string? tipo, uint rowVersion) => new(
        "Katy", TipoPessoa.Fisica, null, "31999990000", null, null, null, null, "Belo Horizonte", "MG", "MG132", null, null, null, "ABC1D23", null, null, null, null, null,
        null, null, null, null, null, tipo, null, null, false, null, rowVersion);

    private static async Task<string?> EtapaAtualAsync(CssVision.Web.Data.ApplicationDbContext db, Guid leadId)
    {
        db.ChangeTracker.Clear();
        return await db.CrmLeads.AsNoTracking().Where(l => l.Id == leadId).Select(l => l.Etapa!.Nome).SingleAsync();
    }

    [Fact]
    public async Task Lead_ViraIndicacao_AoEditar_ComVendaConcluida_VaiParaVendaConcluidaIndicacao()
    {
        using var factory = new TestDbContextFactory();
        var (servico, id, db) = await MontarAsync(factory, "Venda concluída (Leads)", "Lead");

        await servico.AtualizarAsync(id, Pedido("Indicação Lead", 0), CancellationToken.None);

        Assert.Equal("Venda concluída (Indicação)", await EtapaAtualAsync(db, id));
    }

    [Fact]
    public async Task Lead_ViraIndicacao_AoEditar_EmAtendimento_VaiParaEmAtendimentoIndicacao()
    {
        using var factory = new TestDbContextFactory();
        var (servico, id, db) = await MontarAsync(factory, "Em atendimento (Leads)", "Lead");

        await servico.AtualizarAsync(id, Pedido("Pessoal", 0), CancellationToken.None);

        Assert.Equal("Em atendimento (Indicação)", await EtapaAtualAsync(db, id));
    }

    [Fact]
    public async Task Indicacao_ViraLead_AoEditar_VoltaParaAColunaLeads()
    {
        using var factory = new TestDbContextFactory();
        var (servico, id, db) = await MontarAsync(factory, "Venda concluída (Indicação)", "Pessoal");

        await servico.AtualizarAsync(id, Pedido("Lead", 0), CancellationToken.None);

        Assert.Equal("Venda concluída (Leads)", await EtapaAtualAsync(db, id));
    }

    [Fact]
    public async Task EtiquetaQueNaoMudaDeClasse_NaoMoveOCard()
    {
        using var factory = new TestDbContextFactory();
        var (servico, id, db) = await MontarAsync(factory, "Venda concluída (Indicação)", "Pessoal");

        await servico.AtualizarAsync(id, Pedido("Parceria", 0), CancellationToken.None); // Indicação → Indicação

        Assert.Equal("Venda concluída (Indicação)", await EtapaAtualAsync(db, id));
    }

    [Fact]
    public async Task LeadQueContinuaLead_NaoMoveOCard()
    {
        using var factory = new TestDbContextFactory();
        var (servico, id, db) = await MontarAsync(factory, "Venda concluída (Leads)", "Lead");

        await servico.AtualizarAsync(id, Pedido("Lead", 0), CancellationToken.None);

        Assert.Equal("Venda concluída (Leads)", await EtapaAtualAsync(db, id));
    }

    [Fact]
    public async Task ColunaSemVersaoLeadsIndicacao_FicaOndeEsta()
    {
        using var factory = new TestDbContextFactory();
        var (servico, id, db) = await MontarAsync(factory, "Cotação", "Lead");

        await servico.AtualizarAsync(id, Pedido("Pessoal", 0), CancellationToken.None);

        Assert.Equal("Cotação", await EtapaAtualAsync(db, id));
    }

    [Fact]
    public async Task SemAColunaIrma_FicaOndeEsta_ENaoDaErro()
    {
        using var factory = new TestDbContextFactory();
        var (servico, id, db) = await MontarAsync(factory, "Venda concluída (Leads)", "Lead", criarIrma: false);

        await servico.AtualizarAsync(id, Pedido("Pessoal", 0), CancellationToken.None);

        Assert.Equal("Venda concluída (Leads)", await EtapaAtualAsync(db, id));
        Assert.Equal("Pessoal", (await db.CrmLeads.AsNoTracking().SingleAsync(l => l.Id == id)).TipoIndicacao); // a etiqueta mudou normalmente
    }

    [Fact]
    public async Task CadastroManualSemTipo_ContaComoIndicacao_ENaoMoveAoSalvarSemMudar()
    {
        using var factory = new TestDbContextFactory();
        var (servico, id, db) = await MontarAsync(factory, "Em atendimento (Indicação)", tipoInicial: null, manual: true);

        await servico.AtualizarAsync(id, Pedido(null, 0), CancellationToken.None);

        Assert.Equal("Em atendimento (Indicação)", await EtapaAtualAsync(db, id));
    }
}
