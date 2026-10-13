using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Lead com venda concluída não volta para "Sem etapa"/"Cotação"/"Em atendimento": a venda ficava parada como atendimento em aberto.</summary>
public class LeadComVendaNaoVoltaParaAtendimentoTests
{
    private static LeadService Servico(ApplicationDbContext db, ICurrentUserService usuario) =>
        new(db, usuario, new EquipeComercialService(db, usuario), new NoOpLeadAssignmentService(), new NoOpMetaConversionService(), new NoOpAuditSink());

    private sealed record Cenario(ApplicationDbContext Db, ICurrentUserService Usuario, CrmLead Lead, CrmLeadStage Atendimento, CrmLeadStage Cotacao, CrmLeadStage VendaConcluida, CrmOpportunity Venda);

    private static async Task<Cenario> PrepararAsync(TestDbContextFactory factory, bool vendaArquivada = false)
    {
        var db = factory.CreateContext();
        var vendedor = await factory.CriarUsuarioAsync(db, "Vendedor");
        var atendimento = await factory.ObterOuCriarEtapaLeadAsync(db, "Em atendimento (Leads)", 1);
        var cotacao = await factory.ObterOuCriarEtapaLeadAsync(db, "Cotação", 2);
        var vendaConcluida = await factory.ObterOuCriarEtapaLeadAsync(db, "Venda concluída (Leads)", 3);
        var ganho = await factory.CriarEtapaAsync(db, "Ganho", 2, TipoEtapaPipeline.Ganho);

        var lead = new CrmLead { NomeOuRazaoSocial = "Cliente", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = vendedor.Id, TipoIndicacao = "Lead", CriadoManualmente = false, EtapaId = vendaConcluida.Id, ValorAdesao = 150m };
        db.CrmLeads.Add(lead);
        await db.SaveChangesAsync();
        var venda = new CrmOpportunity
        {
            LeadId = lead.Id, Titulo = "Venda", ResponsavelId = vendedor.Id, EtapaId = ganho.Id,
            DataEfetivaFechamento = DateTimeOffset.UtcNow, Arquivado = vendaArquivada,
        };
        db.CrmOpportunities.Add(venda);
        await db.SaveChangesAsync();
        return new Cenario(db, TestDbContextFactory.MockCurrentUser(vendedor.Id).Object, lead, atendimento, cotacao, vendaConcluida, venda);
    }

    [Fact]
    public async Task LeadComVenda_NaoVoltaParaEmAtendimento()
    {
        using var factory = new TestDbContextFactory();
        var c = await PrepararAsync(factory);

        var erro = await Assert.ThrowsAsync<CrmBusinessException>(() =>
            Servico(c.Db, c.Usuario).MudarEtapaAsync(c.Lead.Id, new ChangeLeadStageRequest(c.Atendimento.Id, c.Lead.RowVersion), CancellationToken.None));

        Assert.Equal("lead_com_venda_concluida", erro.Codigo);
        c.Db.ChangeTracker.Clear();
        Assert.Equal(c.VendaConcluida.Id, (await c.Db.CrmLeads.SingleAsync()).EtapaId);
    }

    [Fact]
    public async Task LeadComVenda_NaoVoltaParaCotacao_NemParaSemEtapa()
    {
        using var factory = new TestDbContextFactory();
        var c = await PrepararAsync(factory);

        var paraCotacao = await Assert.ThrowsAsync<CrmBusinessException>(() =>
            Servico(c.Db, c.Usuario).MudarEtapaAsync(c.Lead.Id, new ChangeLeadStageRequest(c.Cotacao.Id, c.Lead.RowVersion, ValorAdesao: 150m), CancellationToken.None));
        var paraSemEtapa = await Assert.ThrowsAsync<CrmBusinessException>(() =>
            Servico(c.Db, c.Usuario).MudarEtapaAsync(c.Lead.Id, new ChangeLeadStageRequest(null, c.Lead.RowVersion), CancellationToken.None));

        Assert.Equal("lead_com_venda_concluida", paraCotacao.Codigo);
        Assert.Equal("lead_com_venda_concluida", paraSemEtapa.Codigo);
    }

    [Fact]
    public async Task LeadComVenda_PodeIrParaAColunaDeVendaConcluida()
    {
        using var factory = new TestDbContextFactory();
        var c = await PrepararAsync(factory);
        var indicacao = await factory.ObterOuCriarEtapaLeadAsync(c.Db, "Venda concluída (Indicação)", 4);

        await Servico(c.Db, c.Usuario).MudarEtapaAsync(c.Lead.Id, new ChangeLeadStageRequest(indicacao.Id, c.Lead.RowVersion), CancellationToken.None);

        c.Db.ChangeTracker.Clear();
        Assert.Equal(indicacao.Id, (await c.Db.CrmLeads.SingleAsync()).EtapaId);
    }

    [Fact]
    public async Task LeadSemVenda_MoveLivremente()
    {
        using var factory = new TestDbContextFactory();
        var db = factory.CreateContext();
        var vendedor = await factory.CriarUsuarioAsync(db, "Vendedor");
        var atendimento = await factory.ObterOuCriarEtapaLeadAsync(db, "Em atendimento (Leads)", 1);
        var lead = new CrmLead { NomeOuRazaoSocial = "Cliente", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = vendedor.Id, TipoIndicacao = "Lead", CriadoManualmente = false };
        db.CrmLeads.Add(lead);
        await db.SaveChangesAsync();

        await Servico(db, TestDbContextFactory.MockCurrentUser(vendedor.Id).Object)
            .MudarEtapaAsync(lead.Id, new ChangeLeadStageRequest(atendimento.Id, lead.RowVersion), CancellationToken.None);

        db.ChangeTracker.Clear();
        Assert.Equal(atendimento.Id, (await db.CrmLeads.SingleAsync()).EtapaId);
    }

    [Fact]
    public async Task VendaArquivada_NaoBloqueiaOMovimento()
    {
        using var factory = new TestDbContextFactory();
        var c = await PrepararAsync(factory, vendaArquivada: true);

        await Servico(c.Db, c.Usuario).MudarEtapaAsync(c.Lead.Id, new ChangeLeadStageRequest(c.Atendimento.Id, c.Lead.RowVersion), CancellationToken.None);

        c.Db.ChangeTracker.Clear();
        Assert.Equal(c.Atendimento.Id, (await c.Db.CrmLeads.SingleAsync()).EtapaId);
    }
}
