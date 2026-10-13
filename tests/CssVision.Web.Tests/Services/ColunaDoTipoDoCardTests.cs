using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>"(Leads)" só aceita cartão de Lead; "Em atendimento (Indicação)" só aceita Indicação; Lead pode ir para "Venda concluída (Indicação)" (Indicação Lead).</summary>
public class ColunaDoTipoDoCardTests
{
    private sealed record Cenario(ApplicationDbContext Db, ICurrentUserService Usuario, CrmLeadStage AtendLeads, CrmLeadStage AtendIndic, CrmLeadStage VendaLeads, CrmLeadStage VendaIndic, TestDbContextFactory Factory, Guid VendedorId);

    private static LeadService Servico(ApplicationDbContext db, ICurrentUserService usuario) =>
        new(db, usuario, new EquipeComercialService(db, usuario), new NoOpLeadAssignmentService(), new NoOpMetaConversionService(), new NoOpAuditSink());

    private static async Task<Cenario> PrepararAsync(TestDbContextFactory factory)
    {
        var db = factory.CreateContext();
        var vendedor = await factory.CriarUsuarioAsync(db, "Vendedor");
        return new Cenario(db, TestDbContextFactory.MockCurrentUser(vendedor.Id).Object,
            await factory.ObterOuCriarEtapaLeadAsync(db, "Em atendimento (Leads)", 1),
            await factory.ObterOuCriarEtapaLeadAsync(db, "Em atendimento (Indicação)", 2),
            await factory.ObterOuCriarEtapaLeadAsync(db, "Venda concluída (Leads)", 3),
            await factory.ObterOuCriarEtapaLeadAsync(db, "Venda concluída (Indicação)", 4), factory, vendedor.Id);
    }

    private static async Task<CrmLead> NovoLeadAsync(Cenario c, string tipo)
    {
        // "Lead": etiqueta Lead (tráfego). "Indicação": criado manualmente com tipo diferente de Lead.
        var lead = tipo == "Lead"
            ? new CrmLead { NomeOuRazaoSocial = "Cliente Lead", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = c.VendedorId, TipoIndicacao = "Lead", CriadoManualmente = false }
            : new CrmLead { NomeOuRazaoSocial = "Cliente Indicação", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = c.VendedorId, TipoIndicacao = "Indicação", CriadoManualmente = true };
        c.Db.CrmLeads.Add(lead);
        await c.Db.SaveChangesAsync();
        return lead;
    }

    private static Task<LeadDetailDto> Mover(Cenario c, CrmLead lead, CrmLeadStage destino) =>
        Servico(c.Db, c.Usuario).MudarEtapaAsync(lead.Id, new ChangeLeadStageRequest(destino.Id, lead.RowVersion), CancellationToken.None);

    [Theory]
    [InlineData("Em atendimento (Leads)")]
    [InlineData("Venda concluída (Leads)")]
    public async Task CartaoDeIndicacao_NaoVaiParaColunaDeLeads(string coluna)
    {
        using var factory = new TestDbContextFactory();
        var c = await PrepararAsync(factory);
        var lead = await NovoLeadAsync(c, "Indicação");
        var destino = coluna.StartsWith("Em atend") ? c.AtendLeads : c.VendaLeads;

        var erro = await Assert.ThrowsAsync<CrmBusinessException>(() => Mover(c, lead, destino));

        Assert.Equal("coluna_so_para_lead", erro.Codigo);
    }

    [Fact]
    public async Task CartaoDeLead_NaoVaiParaEmAtendimentoIndicacao()
    {
        using var factory = new TestDbContextFactory();
        var c = await PrepararAsync(factory);
        var lead = await NovoLeadAsync(c, "Lead");

        var erro = await Assert.ThrowsAsync<CrmBusinessException>(() => Mover(c, lead, c.AtendIndic));

        Assert.Equal("coluna_so_para_indicacao", erro.Codigo);
    }

    [Fact]
    public async Task CartaoDeLead_VaiParaAsColunasDeLeads()
    {
        using var factory = new TestDbContextFactory();
        var c = await PrepararAsync(factory);
        var lead = await NovoLeadAsync(c, "Lead");

        await Mover(c, lead, c.AtendLeads);
        c.Db.ChangeTracker.Clear();
        Assert.Equal(c.AtendLeads.Id, (await c.Db.CrmLeads.SingleAsync()).EtapaId);
    }

    [Fact]
    public async Task CartaoDeIndicacao_VaiParaAsColunasDeIndicacao()
    {
        using var factory = new TestDbContextFactory();
        var c = await PrepararAsync(factory);
        var lead = await NovoLeadAsync(c, "Indicação");

        await Mover(c, lead, c.AtendIndic);
        c.Db.ChangeTracker.Clear();
        var atual = await c.Db.CrmLeads.SingleAsync();
        Assert.Equal(c.AtendIndic.Id, atual.EtapaId);

        await Mover(c, atual, c.VendaIndic);
        c.Db.ChangeTracker.Clear();
        Assert.Equal(c.VendaIndic.Id, (await c.Db.CrmLeads.SingleAsync()).EtapaId);
    }

    [Fact]
    public async Task CartaoDeLead_NaoVaiParaVendaConcluidaIndicacao()
    {
        using var factory = new TestDbContextFactory();
        var c = await PrepararAsync(factory);
        var lead = await NovoLeadAsync(c, "Lead");

        var erro = await Assert.ThrowsAsync<CrmBusinessException>(() => Mover(c, lead, c.VendaIndic));

        Assert.Equal("coluna_so_para_indicacao", erro.Codigo);
    }

    [Fact]
    public async Task LeadSemSinalDeAnuncio_NaoEntraEmVendaConcluidaLeads()
    {
        using var factory = new TestDbContextFactory();
        var c = await PrepararAsync(factory);
        var lead = await NovoLeadAsync(c, "Lead"); // sem ID do Meta, GCLID, campanha, UTM nem tag de anúncio

        var erro = await Assert.ThrowsAsync<CrmBusinessException>(() => Mover(c, lead, c.VendaLeads));

        Assert.Equal("venda_leads_so_trafego_pago", erro.Codigo);
    }

    [Fact]
    public async Task LeadDeTrafegoPago_EntraEmVendaConcluidaLeads()
    {
        using var factory = new TestDbContextFactory();
        var c = await PrepararAsync(factory);
        var lead = await NovoLeadAsync(c, "Lead");
        lead.MetaLeadId = "meta-123";
        await c.Db.SaveChangesAsync();

        await Mover(c, lead, c.VendaLeads);

        c.Db.ChangeTracker.Clear();
        Assert.Equal(c.VendaLeads.Id, (await c.Db.CrmLeads.SingleAsync()).EtapaId);
    }

    [Fact]
    public async Task CartaoDeIndicacao_NaoEntraEmVendaConcluidaLeads()
    {
        using var factory = new TestDbContextFactory();
        var c = await PrepararAsync(factory);
        var lead = await NovoLeadAsync(c, "Indicação");

        var erro = await Assert.ThrowsAsync<CrmBusinessException>(() => Mover(c, lead, c.VendaLeads));

        Assert.Equal("coluna_so_para_lead", erro.Codigo);
    }
}
