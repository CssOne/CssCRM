using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Gestão comercial: "Motivos de perda" também conta os leads da coluna "Perdido" do quadro de leads, onde as perdas acontecem.</summary>
public class MotivosPerdaGestaoTests
{
    [Fact]
    public async Task MotivosDePerda_ContaLeadsPerdidosDoQuadro_ComEMSemMotivo()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var admin = await factory.CriarUsuarioAsync(db, "Admin");
        var ana = await factory.CriarUsuarioAsync(db, "Ana");
        var perdido = await factory.ObterOuCriarEtapaLeadAsync(db, "Perdido", 9);
        var emAtendimento = await factory.ObterOuCriarEtapaLeadAsync(db, "Em atendimento", 1);
        var motivo = new CrmLossReason { Descricao = "Preço" };
        db.CrmLossReasons.Add(motivo);
        await db.SaveChangesAsync();

        CrmLead Lead(string nome, Guid etapaId, Guid? motivoId = null) =>
            new() { NomeOuRazaoSocial = nome, TipoPessoa = TipoPessoa.Fisica, ResponsavelId = ana.Id, EtapaId = etapaId, MotivoPerdaId = motivoId };
        db.CrmLeads.AddRange(
            Lead("Perdeu pelo preço 1", perdido.Id, motivo.Id),
            Lead("Perdeu pelo preço 2", perdido.Id, motivo.Id),
            Lead("Perdeu sem motivo", perdido.Id),
            Lead("Ainda em atendimento", emAtendimento.Id));
        await db.SaveChangesAsync();

        var usuario = TestDbContextFactory.MockCurrentUser(admin.Id, visaoTotal: true, podeGerir: true).Object;
        var gestao = new ManagementService(db, usuario, new EquipeComercialService(db, usuario), TestDbContextFactory.CreateUserManager(db));
        var resumo = await gestao.ObterResumoAsync(null, null, CancellationToken.None);

        Assert.Equal(2, resumo.MotivosPerda.Count);
        Assert.Equal(("Preço", 2), (resumo.MotivosPerda[0].Motivo, resumo.MotivosPerda[0].Quantidade));
        Assert.Equal(("Sem motivo informado", 1), (resumo.MotivosPerda[1].Motivo, resumo.MotivosPerda[1].Quantidade));
    }
}
