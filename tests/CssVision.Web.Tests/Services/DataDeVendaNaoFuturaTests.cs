using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Trava: a data da venda não pode ser futura (nem ao concluir a venda, nem ao editar). "Hoje" é o dia de Brasília.</summary>
public class DataDeVendaNaoFuturaTests
{
    private sealed record Cenario(OpportunityService Servico, OpportunityDto Oportunidade, CrmPipelineStage Ganho, ApplicationDbContext Db, Guid VendedorId);

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
        var oportunidade = await servico.CriarAsync(
            new OpportunityCreateRequest(lead.Id, "Proposta", vendedor.Id, aberta.Id, null, 1000m, null, null, null, null, null, null, null, null, null, null, false, false, null),
            CancellationToken.None);
        return new Cenario(servico, oportunidade, ganho, db, vendedor.Id);
    }

    private static ChangeStageRequest Concluir(Cenario c, DateOnly data) =>
        new(c.Ganho.Id, c.Oportunidade.RowVersion, null, null, 1200m, data);

    [Fact]
    public async Task ConcluirVenda_ComDataFutura_Falha_ENadaMuda()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);

        var erro = await Assert.ThrowsAsync<CrmBusinessException>(() =>
            c.Servico.MudarEtapaAsync(c.Oportunidade.Id, Concluir(c, HorarioBrasilia.Hoje.AddDays(1)), CancellationToken.None));

        Assert.Equal("data_venda_futura", erro.Codigo);
        c.Db.ChangeTracker.Clear();
        var salva = await c.Db.CrmOpportunities.AsNoTracking().Include(o => o.Etapa).SingleAsync(o => o.Id == c.Oportunidade.Id);
        Assert.NotEqual(TipoEtapaPipeline.Ganho, salva.Etapa.Tipo); // continua aberta
        Assert.Null(salva.DataEfetivaFechamento);
    }

    [Fact]
    public async Task ConcluirVenda_ComDataDeHojeOuAnterior_Funciona()
    {
        using var factory = new TestDbContextFactory();
        var hoje = await MontarAsync(factory);
        var ok = await hoje.Servico.MudarEtapaAsync(hoje.Oportunidade.Id, Concluir(hoje, HorarioBrasilia.Hoje), CancellationToken.None);
        Assert.Equal(hoje.Ganho.Id, ok.EtapaId);

        using var outraFactory = new TestDbContextFactory();
        var ontem = await MontarAsync(outraFactory);
        var ok2 = await ontem.Servico.MudarEtapaAsync(ontem.Oportunidade.Id, Concluir(ontem, HorarioBrasilia.Hoje.AddDays(-1)), CancellationToken.None);
        Assert.Equal(ontem.Ganho.Id, ok2.EtapaId);
    }

    [Fact]
    public async Task EditarVenda_ComDataFutura_Falha_ComDataDeHojeFunciona()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var concluida = await c.Servico.MudarEtapaAsync(c.Oportunidade.Id, Concluir(c, HorarioBrasilia.Hoje.AddDays(-2)), CancellationToken.None);

        OpportunityUpdateRequest Editar(DateOnly data, uint rowVersion) => new(
            "Venda concluída", c.VendedorId, null, 100m, null, null, null, null, null, null, null, null, null, 350m, null, false, false, null, rowVersion,
            DataEfetivaFechamento: data);

        var erro = await Assert.ThrowsAsync<CrmBusinessException>(() =>
            c.Servico.AtualizarAsync(concluida.Id, Editar(HorarioBrasilia.Hoje.AddDays(3), concluida.RowVersion), CancellationToken.None));
        Assert.Equal("data_venda_futura", erro.Codigo);

        c.Db.ChangeTracker.Clear();
        var salva = await c.Db.CrmOpportunities.AsNoTracking().SingleAsync(o => o.Id == concluida.Id);
        Assert.Equal(HorarioBrasilia.Hoje.AddDays(-2), HorarioBrasilia.Dia(salva.DataEfetivaFechamento!.Value)); // a data antiga ficou

        var atual = await c.Servico.ObterPorIdAsync(concluida.Id, CancellationToken.None);
        await c.Servico.AtualizarAsync(concluida.Id, Editar(HorarioBrasilia.Hoje, atual.RowVersion), CancellationToken.None);
        c.Db.ChangeTracker.Clear();
        var corrigida = await c.Db.CrmOpportunities.AsNoTracking().SingleAsync(o => o.Id == concluida.Id);
        Assert.Equal(HorarioBrasilia.Hoje, HorarioBrasilia.Dia(corrigida.DataEfetivaFechamento!.Value));
    }

    [Fact]
    public void Limite_EhOHojeDeBrasilia_NaoOUtc()
    {
        // O dia de hoje em Brasília nunca é bloqueado, mesmo à noite, quando o dia em UTC já virou.
        OpportunityService.ExigirDataDeVendaNaoFutura(HorarioBrasilia.Hoje);
        Assert.Throws<CrmBusinessException>(() => OpportunityService.ExigirDataDeVendaNaoFutura(HorarioBrasilia.Hoje.AddDays(1)));
    }
}
