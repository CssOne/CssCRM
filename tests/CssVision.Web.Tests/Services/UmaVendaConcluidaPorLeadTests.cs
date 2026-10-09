using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Trava: um cliente (lead) tem no máximo uma venda concluída — outro veículo é outro card ("Outro veículo").</summary>
public class UmaVendaConcluidaPorLeadTests
{
    private sealed record Cenario(OpportunityService Servico, CrmPipelineStage Aberta, CrmPipelineStage Ganho, ApplicationDbContext Db, Guid VendedorId, Guid LeadId);

    private static async Task<Cenario> MontarAsync(TestDbContextFactory factory)
    {
        var db = factory.CreateContext();
        var vendedor = await factory.CriarUsuarioAsync(db, "Vendedor1");
        var aberta = await factory.CriarEtapaAsync(db, "Novo lead", 1);
        var ganho = await factory.CriarEtapaAsync(db, "Ganho", 2, TipoEtapaPipeline.Ganho);
        var etapaLead = await factory.ObterOuCriarEtapaLeadAsync(db);
        var lead = new CrmLead { EtapaId = etapaLead.Id, NomeOuRazaoSocial = "Cliente", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = vendedor.Id };
        db.CrmLeads.Add(lead);
        await db.SaveChangesAsync();
        var currentUser = TestDbContextFactory.MockCurrentUser(vendedor.Id);
        var servico = new OpportunityService(db, currentUser.Object, new EquipeComercialService(db, currentUser.Object),
            new NoOpMetaConversionService(), new NoOpAuditSink(), new FakeFileStorageService());
        return new Cenario(servico, aberta, ganho, db, vendedor.Id, lead.Id);
    }

    private static Task<OpportunityDto> NovaProposta(Cenario c) => c.Servico.CriarAsync(
        new OpportunityCreateRequest(c.LeadId, "Venda concluída", c.VendedorId, c.Aberta.Id, null, 1000m, null, null, null, null, null, null, null, null, null, null, false, false, null),
        CancellationToken.None);

    private static ChangeStageRequest Concluir(Cenario c, OpportunityDto o) =>
        new(c.Ganho.Id, o.RowVersion, null, null, 1200m, HorarioBrasilia.Hoje);

    [Fact]
    public async Task SegundaVendaConcluida_NoMesmoCliente_EBloqueada_EAPrimeiraSegue()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var primeira = await NovaProposta(c);
        var segunda = await NovaProposta(c);

        await c.Servico.MudarEtapaAsync(primeira.Id, Concluir(c, primeira), CancellationToken.None);
        var erro = await Assert.ThrowsAsync<CrmBusinessException>(() =>
            c.Servico.MudarEtapaAsync(segunda.Id, Concluir(c, segunda), CancellationToken.None));

        Assert.Equal("venda_ja_concluida", erro.Codigo);
        c.Db.ChangeTracker.Clear();
        var salvas = await c.Db.CrmOpportunities.AsNoTracking().Include(o => o.Etapa).Where(o => o.LeadId == c.LeadId).ToListAsync();
        Assert.Equal(1, salvas.Count(o => o.Etapa.Tipo == TipoEtapaPipeline.Ganho));
        Assert.Equal(TipoEtapaPipeline.Aberta, salvas.Single(o => o.Id == segunda.Id).Etapa.Tipo);
    }

    [Fact]
    public async Task CriarOportunidadeJaGanha_NoClienteComVendaConcluida_EBloqueado()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var primeira = await NovaProposta(c);
        await c.Servico.MudarEtapaAsync(primeira.Id, Concluir(c, primeira), CancellationToken.None);

        var erro = await Assert.ThrowsAsync<CrmBusinessException>(() => c.Servico.CriarAsync(
            new OpportunityCreateRequest(c.LeadId, "Outra", c.VendedorId, c.Ganho.Id, null, 1000m, null, null, null, null, null, null, null, null, null, null, false, false, null),
            CancellationToken.None));
        Assert.Equal("venda_ja_concluida", erro.Codigo);
    }

    [Fact]
    public async Task VendaExcluida_NaoImpedeUmaNovaVendaConcluida()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var primeira = await NovaProposta(c);
        await c.Servico.MudarEtapaAsync(primeira.Id, Concluir(c, primeira), CancellationToken.None);
        await c.Servico.ExcluirAsync(primeira.Id, CancellationToken.None);

        var segunda = await NovaProposta(c);
        var ok = await c.Servico.MudarEtapaAsync(segunda.Id, Concluir(c, segunda), CancellationToken.None);
        Assert.Equal(c.Ganho.Id, ok.EtapaId);
    }

    [Fact]
    public async Task VendaConcluida_DeClientesDiferentes_NaoSeAtrapalham()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var outroLead = new CrmLead { NomeOuRazaoSocial = "Outro", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = c.VendedorId };
        c.Db.CrmLeads.Add(outroLead);
        await c.Db.SaveChangesAsync();

        var a = await NovaProposta(c);
        await c.Servico.MudarEtapaAsync(a.Id, Concluir(c, a), CancellationToken.None);
        var b = await c.Servico.CriarAsync(
            new OpportunityCreateRequest(outroLead.Id, "Venda concluída", c.VendedorId, c.Aberta.Id, null, 1000m, null, null, null, null, null, null, null, null, null, null, false, false, null),
            CancellationToken.None);
        var ok = await c.Servico.MudarEtapaAsync(b.Id, Concluir(c, b), CancellationToken.None);
        Assert.Equal(c.Ganho.Id, ok.EtapaId);
    }
}
