using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Origem é só do administrador: o consultor não a recebe — nem disfarçada de campanha/UTM — e não a apaga ao salvar.</summary>
public class ConsultorNaoVeOrigemTests
{
    [Fact]
    public async Task Consultor_NaoRecebeOrigemNemCampanhaNemUtm_EAoSalvarNaoApagaOsDados_MasAGestaoVe()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var consultor = await factory.CriarUsuarioAsync(db, "Consultor");
        var gestor = await factory.CriarUsuarioAsync(db, "Gestor");
        var regional = await factory.CriarRegionalAsync(db, "MG132");
        consultor.RegionalId = regional.Id; gestor.RegionalId = regional.Id; // o gestor regional enxerga a equipe da regional
        await db.SaveChangesAsync();
        var lead = new CrmLead
        {
            NomeOuRazaoSocial = "Cliente", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = consultor.Id, Origem = "Lookalike", Campanha = "Lookalike",
            UtmSource = "facebook", Gclid = "abc", MetaLeadId = "123", Telefone = "31999990000", Cidade = "BH", Estado = "MG", Regional = "MG132", Placa = "ABC1D23",
        };
        db.CrmLeads.Add(lead);
        await db.SaveChangesAsync();
        var comoConsultor = TestDbContextFactory.MockCurrentUser(consultor.Id);
        var comoGestor = TestDbContextFactory.MockCurrentUser(gestor.Id, gestorComercial: true, podeGerir: true);
        LeadService Leads(Moq.Mock<ICurrentUserService> u) => new(db, u.Object, new EquipeComercialService(db, u.Object),
            new NoOpLeadAssignmentService(), new NoOpMetaConversionService(), new NoOpAuditSink());

        var detalhe = await Leads(comoConsultor).ObterPorIdAsync(lead.Id, CancellationToken.None);
        Assert.Null(detalhe.Origem);
        Assert.Null(detalhe.Campanha);
        Assert.Null(detalhe.UtmSource);
        Assert.Null(detalhe.Gclid);
        Assert.Null(detalhe.MetaLeadId);

        // O consultor salva o cadastro (mandando de volta o que recebeu: nulos) — a origem e o marketing continuam lá.
        var pedido = new LeadUpdateRequest("Cliente", TipoPessoa.Fisica, null, "31999990000", null, null, null, null, "BH", "MG", "MG132", null, null, null, "ABC1D23",
            null, null, null, null, null, null, null, null, null, null, null, null, null, false, null, detalhe.RowVersion);
        await Leads(comoConsultor).AtualizarAsync(lead.Id, pedido, CancellationToken.None);
        db.ChangeTracker.Clear();
        var salvo = await db.CrmLeads.AsNoTracking().SingleAsync(l => l.Id == lead.Id);
        Assert.Equal(("Lookalike", "Lookalike", "facebook", "abc", "123"), (salvo.Origem, salvo.Campanha, salvo.UtmSource, salvo.Gclid, salvo.MetaLeadId));

        // A gestão (regional) vê a campanha e o marketing, mas continua sem a origem (só visão total).
        var paraGestor = await Leads(comoGestor).ObterPorIdAsync(lead.Id, CancellationToken.None);
        Assert.Null(paraGestor.Origem);
        Assert.Equal(("Lookalike", "facebook"), (paraGestor.Campanha, paraGestor.UtmSource));

        // Quadro: o cartão do consultor não leva origem nem campanha.
        var quadro = new LeadKanbanService(db, new EquipeComercialService(db, comoConsultor.Object), currentUser: comoConsultor.Object);
        var cartao = (await quadro.ObterBoardAsync(new LeadKanbanFilterRequest(), CancellationToken.None)).Colunas.SelectMany(c => c.Cartoes).Single();
        Assert.Null(cartao.Origem);
        Assert.Null(cartao.Campanha);
    }
}
