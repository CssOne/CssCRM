using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Página do lead: linha do tempo sem Origem para o consultor e anexos da venda na oportunidade.</summary>
public class LeadDetalheTests
{
    private static LeadService Servico(CssVision.Web.Data.ApplicationDbContext db, Guid usuarioId, bool visaoTotal)
    {
        var usuario = TestDbContextFactory.MockCurrentUser(usuarioId, visaoTotal: visaoTotal).Object;
        return new LeadService(db, usuario, new EquipeComercialService(db, usuario), new NoOpLeadAssignmentService(), new NoOpMetaConversionService(), new NoOpAuditSink());
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(true, "Origem: Meta ads")]
    public async Task LinhaDoTempo_OrigemSoParaAdministrador(bool administrador, string? detalheEsperado)
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var consultor = await factory.CriarUsuarioAsync(db, "Consultor");
        var lead = new CrmLead { NomeOuRazaoSocial = "Cliente", TipoPessoa = TipoPessoa.Fisica, Origem = "Meta ads", ResponsavelId = consultor.Id };
        db.CrmLeads.Add(lead);
        await db.SaveChangesAsync();

        var itens = await Servico(db, consultor.Id, administrador).ObterTimelineAsync(lead.Id, CancellationToken.None);

        var cadastro = Assert.Single(itens, i => i.Tipo == TipoEventoTimeline.LeadCriado);
        Assert.Equal(detalheEsperado, cadastro.Descricao);
    }

    [Fact]
    public async Task Oportunidade_TrazOsAnexosDaVenda()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var consultor = await factory.CriarUsuarioAsync(db, "Consultor");
        var ganho = await factory.CriarEtapaAsync(db, "Ganho", 1, TipoEtapaPipeline.Ganho);
        var lead = new CrmLead { NomeOuRazaoSocial = "Cliente", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = consultor.Id };
        db.CrmLeads.Add(lead);
        db.CrmOpportunities.Add(new CrmOpportunity
        {
            LeadId = lead.Id, Titulo = "Venda", ResponsavelId = consultor.Id, EtapaId = ganho.Id,
            TermoAdesaoArquivoUrl = "https://bucket/uploads/termo.pdf",
            PagamentoAdesaoArquivoUrl = "https://bucket/uploads/comprovante.pdf",
        });
        await db.SaveChangesAsync();

        var detalhe = await Servico(db, consultor.Id, false).ObterPorIdAsync(lead.Id, CancellationToken.None);

        var venda = Assert.Single(detalhe.Oportunidades);
        Assert.Equal("https://bucket/uploads/termo.pdf", venda.TermoAdesaoArquivoUrl);
        Assert.Equal("https://bucket/uploads/comprovante.pdf", venda.PagamentoAdesaoArquivoUrl);
    }
}
