using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Xunit;

namespace CssVision.Web.Tests.Services;

public class ActivityServiceTests
{
    [Fact]
    public async Task ConcluirAsync_DeveMarcarComoConcluida_EAtualizarUltimoContatoDoLead()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var vendedor = await factory.CriarUsuarioAsync(db, "Vendedor1");
        var etapaLead = await factory.ObterOuCriarEtapaLeadAsync(db);
        var lead = new CrmLead { EtapaId = etapaLead.Id, NomeOuRazaoSocial = "Cliente", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = vendedor.Id };
        db.CrmLeads.Add(lead);
        await db.SaveChangesAsync();

        var currentUser = TestDbContextFactory.MockCurrentUser(vendedor.Id);
        var equipe = new EquipeComercialService(db, currentUser.Object);
        var service = new ActivityService(db, currentUser.Object, equipe, new NoOpAuditSink());

        var atividade = await service.CriarAsync(
            new ActivityCreateRequest(lead.Id, null, vendedor.Id, TipoAtividade.Ligacao, "Primeiro contato", null, DateTimeOffset.UtcNow, null),
            CancellationToken.None);

        var concluida = await service.ConcluirAsync(atividade.Id, new ActivityCompleteRequest("Cliente interessado", DateTimeOffset.UtcNow, atividade.RowVersion), CancellationToken.None);

        Assert.Equal(StatusAtividade.Concluida, concluida.Status);
        Assert.NotNull(concluida.DataHoraConclusao);

        var leadAtualizado = await db.CrmLeads.FindAsync(lead.Id);
        Assert.NotNull(leadAtualizado!.UltimoContatoEm);
    }

    [Fact]
    public async Task ConcluirAsync_DeveFalhar_QuandoJaConcluida()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var vendedor = await factory.CriarUsuarioAsync(db, "Vendedor1");
        var etapaLead = await factory.ObterOuCriarEtapaLeadAsync(db);
        var lead = new CrmLead { EtapaId = etapaLead.Id, NomeOuRazaoSocial = "Cliente", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = vendedor.Id };
        db.CrmLeads.Add(lead);
        await db.SaveChangesAsync();

        var currentUser = TestDbContextFactory.MockCurrentUser(vendedor.Id);
        var equipe = new EquipeComercialService(db, currentUser.Object);
        var service = new ActivityService(db, currentUser.Object, equipe, new NoOpAuditSink());

        var atividade = await service.CriarAsync(
            new ActivityCreateRequest(lead.Id, null, vendedor.Id, TipoAtividade.Ligacao, "Primeiro contato", null, DateTimeOffset.UtcNow, null),
            CancellationToken.None);

        var concluida = await service.ConcluirAsync(atividade.Id, new ActivityCompleteRequest(null, null, atividade.RowVersion), CancellationToken.None);

        await Assert.ThrowsAsync<CrmBusinessException>(() =>
            service.ConcluirAsync(atividade.Id, new ActivityCompleteRequest(null, null, concluida.RowVersion), CancellationToken.None));
    }

    [Fact]
    public async Task CriarAsync_DeveNegar_QuandoResponsavelForaDaEquipe()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var vendedor1 = await factory.CriarUsuarioAsync(db, "Vendedor1");
        var vendedor2 = await factory.CriarUsuarioAsync(db, "Vendedor2");
        var etapaLead = await factory.ObterOuCriarEtapaLeadAsync(db);
        var lead = new CrmLead { EtapaId = etapaLead.Id, NomeOuRazaoSocial = "Cliente", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = vendedor1.Id };
        db.CrmLeads.Add(lead);
        await db.SaveChangesAsync();

        var currentUser2 = TestDbContextFactory.MockCurrentUser(vendedor2.Id);
        var service2 = new ActivityService(db, currentUser2.Object, new EquipeComercialService(db, currentUser2.Object), new NoOpAuditSink());

        await Assert.ThrowsAsync<CrmForbiddenException>(() =>
            service2.CriarAsync(new ActivityCreateRequest(lead.Id, null, null, TipoAtividade.Ligacao, "Assunto", null, DateTimeOffset.UtcNow, null), CancellationToken.None));
    }

    [Fact]
    public async Task VisaoPeriodo_UsaOsDiasDeBrasilia_InclusiveAtividadeDasDezDaNoite()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var vendedor = await factory.CriarUsuarioAsync(db, "Vendedor1");
        var lead = new CrmLead { NomeOuRazaoSocial = "Cliente", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = vendedor.Id };
        db.CrmLeads.Add(lead);
        await db.SaveChangesAsync();
        var currentUser = TestDbContextFactory.MockCurrentUser(vendedor.Id);
        var service = new ActivityService(db, currentUser.Object, new EquipeComercialService(db, currentUser.Object), new NoOpAuditSink());
        var brasilia = TimeSpan.FromHours(-3);
        async Task Agendar(string assunto, DateTimeOffset quando) =>
            await service.CriarAsync(new ActivityCreateRequest(lead.Id, null, vendedor.Id, TipoAtividade.Ligacao, assunto, null, quando, null), CancellationToken.None);

        await Agendar("antes", new DateTimeOffset(2026, 9, 30, 23, 0, 0, brasilia));   // 30/09, fora
        await Agendar("primeiro dia", new DateTimeOffset(2026, 10, 1, 8, 0, 0, brasilia));
        await Agendar("ultimo dia 22h", new DateTimeOffset(2026, 10, 31, 22, 0, 0, brasilia)); // 01/11 01:00 UTC
        await Agendar("depois", new DateTimeOffset(2026, 11, 1, 0, 30, 0, brasilia));

        var outubro = await service.ListarAsync(new ActivityFilterRequest
        {
            Visao = VisaoAtividade.Periodo, DataReferencia = new DateOnly(2026, 10, 1), DataFim = new DateOnly(2026, 10, 31), TamanhoPagina = 100,
        }, CancellationToken.None);

        Assert.Equal(["primeiro dia", "ultimo dia 22h"], outubro.Itens.Select(a => a.Assunto).OrderBy(x => x));
    }
}
