using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Aviso ao consultor no dia em que vence o seguro do associado (lead em "Follow Up" por "Já tem seguro"), uma vez só.</summary>
public class SeguroVencimentoTests
{
    private static readonly DateOnly Vencimento = new(2026, 12, 10);

    private static async Task<(Guid ConsultorId, CrmLead Lead)> CenarioAsync(TestDbContextFactory factory, CssVision.Web.Data.ApplicationDbContext db, string etapa = "Follow Up")
    {
        var ana = await factory.CriarUsuarioAsync(db, "Ana");
        var followUp = new CrmLeadStage { Nome = etapa, Ordem = 9 };
        db.CrmLeadStages.Add(followUp);
        var lead = new CrmLead { NomeOuRazaoSocial = "Carlos Souza", ResponsavelId = ana.Id, Etapa = followUp, SeguroVenceEm = Vencimento };
        db.CrmLeads.Add(lead);
        await db.SaveChangesAsync();
        return (ana.Id, lead);
    }

    private static (SeguroVencimentoNotificador Servico, Mock<IPushService> Push) Notificador(CssVision.Web.Data.ApplicationDbContext db)
    {
        var push = new Mock<IPushService>();
        push.Setup(p => p.EnviarAsync(It.IsAny<Guid>(), It.IsAny<PushMensagem>(), It.IsAny<CancellationToken>())).ReturnsAsync(1);
        return (new SeguroVencimentoNotificador(db, push.Object, NullLogger<SeguroVencimentoNotificador>.Instance), push);
    }

    [Fact]
    public async Task NoDiaDoVencimento_AvisaOConsultor_UmaVezSo_ComPush()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var (ana, lead) = await CenarioAsync(factory, db);
        var (servico, push) = Notificador(db);

        Assert.Equal(0, await servico.NotificarAsync(Vencimento.AddDays(-1), CancellationToken.None));   // ainda não venceu
        Assert.Equal(1, await servico.NotificarAsync(Vencimento, CancellationToken.None));
        Assert.Equal(0, await servico.NotificarAsync(Vencimento.AddDays(1), CancellationToken.None));    // já avisado

        var aviso = await db.CrmAvisosConsultor.SingleAsync();
        Assert.Equal(ana, aviso.ConsultorId);
        Assert.Contains("Carlos Souza vence hoje", aviso.Mensagem);
        push.Verify(p => p.EnviarAsync(ana, It.IsAny<PushMensagem>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(Vencimento, (await db.CrmLeads.SingleAsync(l => l.Id == lead.Id)).SeguroAvisoEnviadoEm);
    }

    [Fact]
    public async Task LeadQueSaiuDoFollowUp_NaoRecebeAviso()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        await CenarioAsync(factory, db, etapa: "Em atendimento");
        var (servico, _) = Notificador(db);

        Assert.Equal(0, await servico.NotificarAsync(Vencimento, CancellationToken.None));
    }
}
