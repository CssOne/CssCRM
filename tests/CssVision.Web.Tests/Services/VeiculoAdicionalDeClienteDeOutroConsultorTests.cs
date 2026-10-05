using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Outro veículo de um cliente que já é atendido por outro consultor: a venda e o card novo ficam com quem registra.</summary>
public class VeiculoAdicionalDeClienteDeOutroConsultorTests
{
    private static LeadService Servico(Data.ApplicationDbContext db, Guid usuarioId)
    {
        var usuario = TestDbContextFactory.MockCurrentUser(usuarioId).Object; // consultora comum
        return new LeadService(db, usuario, new EquipeComercialService(db, usuario), new NoOpLeadAssignmentService(), new NoOpMetaConversionService(), new NoOpAuditSink());
    }

    [Fact]
    public async Task ConsultoraCriaCardNovoNoProprioNome_SemCopiarDadosDoClienteDoOutroConsultor()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var caroline = await factory.CriarUsuarioAsync(db, "Caroline");
        var ana = await factory.CriarUsuarioAsync(db, "Ana");
        await factory.ObterOuCriarEtapaLeadAsync(db, "Venda concluída (Leads)", 5);
        await factory.ObterOuCriarEtapaLeadAsync(db, "Venda concluída (Indicação)", 6);
        var clienteDaCaroline = new CrmLead
        {
            NomeOuRazaoSocial = "Davidson Castilho", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = caroline.Id,
            DocumentoNormalizado = "13115365667", Email = "davidson@exemplo.com", EmailNormalizado = "davidson@exemplo.com",
            Telefone = "(31) 98888-7777", TelefoneNormalizado = "5531988887777", Placa = "OLE0E76", CriadoManualmente = true, TipoIndicacao = "Indicação",
        };
        db.CrmLeads.Add(clienteDaCaroline);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var ids = await Servico(db, ana.Id).CriarVeiculosAdicionaisAsync(clienteDaCaroline.Id, new LeadVeiculosAdicionaisRequest(null, 1, VendaConcluida: true), CancellationToken.None);

        var novo = await db.CrmLeads.AsNoTracking().SingleAsync(l => l.Id == Assert.Single(ids));
        Assert.Equal(ana.Id, novo.ResponsavelId);                       // a venda é de quem registra
        Assert.Equal(clienteDaCaroline.Id, novo.VeiculoAdicionalDeLeadId);
        Assert.Equal("Davidson Castilho", novo.NomeOuRazaoSocial);      // o nome o aviso de duplicidade já mostra
        Assert.Null(novo.DocumentoNormalizado);                         // o resto do cadastro fica com a Caroline
        Assert.Null(novo.Email);
        Assert.Null(novo.Telefone);
        Assert.Null(novo.Placa);
        Assert.NotNull(novo.EtapaId);

        // O card original continua da Caroline.
        var original = await db.CrmLeads.AsNoTracking().SingleAsync(l => l.Id == clienteDaCaroline.Id);
        Assert.Equal(caroline.Id, original.ResponsavelId);
    }

    [Fact]
    public async Task ClienteInexistenteContinuaDandoNaoEncontrado()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var ana = await factory.CriarUsuarioAsync(db, "Ana");

        await Assert.ThrowsAsync<CrmNotFoundException>(() =>
            Servico(db, ana.Id).CriarVeiculosAdicionaisAsync(Guid.NewGuid(), new LeadVeiculosAdicionaisRequest(null, 1), CancellationToken.None));
    }
}
