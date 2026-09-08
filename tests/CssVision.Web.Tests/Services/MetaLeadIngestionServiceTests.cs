using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Marketing;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace CssVision.Web.Tests.Services;

public class MetaLeadIngestionServiceTests
{
    private static MetaLeadResponse LeadDaGraphApi(string leadgenId, string? formId, string nome = "João Silva", string? email = "joao@teste.com", string? telefone = "11988887777") =>
        new(
            leadgenId,
            formId,
            AdId: null,
            CampaignId: null,
            CreatedTime: null,
            FieldData:
            [
                new MetaFieldDatum("full_name", [nome]),
                new MetaFieldDatum("email", email is null ? [] : [email]),
                new MetaFieldDatum("phone_number", telefone is null ? [] : [telefone]),
            ]);

    [Fact]
    public async Task ProcessLeadEventAsync_DeveCriarLead_ComCampanhaETagsDoFormConfig()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();

        var formId = "1544241000014231"; // mapeado em MetaFormConfig: Campanha "UGC", Tags Meta ads+UGC
        var graph = new Mock<IMetaGraphClient>();
        graph.Setup(g => g.FetchLeadAsync("leadgen-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(LeadDaGraphApi("leadgen-1", formId));

        var service = new MetaLeadIngestionService(db, graph.Object, NullLogger<MetaLeadIngestionService>.Instance);
        await service.ProcessLeadEventAsync("leadgen-1", formId, CancellationToken.None);

        var lead = await db.CrmLeads.Include(l => l.LeadTags).ThenInclude(lt => lt.Tag).SingleAsync();
        Assert.Equal("João Silva", lead.NomeOuRazaoSocial);
        Assert.Equal("leadgen-1", lead.MetaLeadId);
        Assert.Equal(formId, lead.MetaFormId);
        Assert.Equal("UGC", lead.Campanha);
        Assert.Equal("AGV", lead.ProdutoInteresse);
        Assert.Equal("Meta ads", lead.Origem);
        Assert.Equal(StatusLead.Novo, lead.Status);
        Assert.Equal(["Meta ads", "UGC"], lead.LeadTags.Select(lt => lt.Tag.Nome).OrderBy(n => n));
    }

    [Fact]
    public async Task ProcessLeadEventAsync_DeveUsarDefaults_QuandoFormNaoMapeado()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();

        var graph = new Mock<IMetaGraphClient>();
        graph.Setup(g => g.FetchLeadAsync("leadgen-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(LeadDaGraphApi("leadgen-2", "form-desconhecido"));

        var service = new MetaLeadIngestionService(db, graph.Object, NullLogger<MetaLeadIngestionService>.Instance);
        await service.ProcessLeadEventAsync("leadgen-2", "form-desconhecido", CancellationToken.None);

        var lead = await db.CrmLeads.Include(l => l.LeadTags).ThenInclude(lt => lt.Tag).SingleAsync();
        Assert.Equal("Lead convertido", lead.Campanha);
        Assert.Null(lead.ProdutoInteresse);
        Assert.Equal(["Meta ads"], lead.LeadTags.Select(lt => lt.Tag.Nome));
    }

    [Fact]
    public async Task ProcessLeadEventAsync_DeveSerIdempotente_QuandoMesmoLeadgenIdJaProcessado()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();

        db.CrmLeads.Add(new CrmLead { NomeOuRazaoSocial = "Já existe", TipoPessoa = TipoPessoa.Fisica, MetaLeadId = "leadgen-3" });
        await db.SaveChangesAsync();

        var graph = new Mock<IMetaGraphClient>();
        var service = new MetaLeadIngestionService(db, graph.Object, NullLogger<MetaLeadIngestionService>.Instance);

        await service.ProcessLeadEventAsync("leadgen-3", null, CancellationToken.None);

        Assert.Equal(1, await db.CrmLeads.CountAsync());
        graph.Verify(g => g.FetchLeadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessLeadEventAsync_DeveAssociarAContatoExistente_EmVezDeCriarDuplicata()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();

        db.CrmLeads.Add(new CrmLead
        {
            NomeOuRazaoSocial = "Cliente Antigo",
            TipoPessoa = TipoPessoa.Fisica,
            EmailNormalizado = "joao@teste.com",
            MetaLeadId = "leadgen-antigo",
        });
        await db.SaveChangesAsync();

        var graph = new Mock<IMetaGraphClient>();
        graph.Setup(g => g.FetchLeadAsync("leadgen-4", It.IsAny<CancellationToken>()))
            .ReturnsAsync(LeadDaGraphApi("leadgen-4", "1050561764291664", email: "joao@teste.com"));

        var service = new MetaLeadIngestionService(db, graph.Object, NullLogger<MetaLeadIngestionService>.Instance);
        await service.ProcessLeadEventAsync("leadgen-4", "1050561764291664", CancellationToken.None);

        Assert.Equal(1, await db.CrmLeads.CountAsync());
        var lead = await db.CrmLeads.Include(l => l.LeadTags).ThenInclude(lt => lt.Tag).SingleAsync();
        Assert.Equal("leadgen-antigo", lead.MetaLeadId); // não sobrescreve o lead original
        Assert.Contains(lead.LeadTags, lt => lt.Tag.Nome == "UGC VENDA");
    }
}
