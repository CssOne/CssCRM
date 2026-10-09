using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Services.Notion;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static CssVision.Web.Services.Notion.NotionSyncService;

namespace CssVision.Web.Tests.Services;

/// <summary>
/// Card do Notion com a placa de um lead que já está no CRM (sem telefone/CPF/e-mail em comum) é o mesmo cliente: não vira lead novo, e a
/// venda não conta em duplicidade (caso QFZ5A97).
/// </summary>
public class NotionSyncPlacaTests
{
    private static (ApplicationDbContext Db, NotionSyncService Service) Preparar(TestDbContextFactory factory)
    {
        var db = factory.CreateContext();
        return (db, new NotionSyncService(db, null!, NullLogger<NotionSyncService>.Instance));
    }

    private static CrmLead LeadDoCrm(string placa, string? origem = null, string? pageId = null, DateTimeOffset? criadoEm = null) => new()
    {
        NomeOuRazaoSocial = "Mauricio", TipoPessoa = TipoPessoa.Fisica, Placa = placa, NotionPageId = pageId,
        Origem = "Meta ads", ConsentimentoOrigem = origem, CriadoEm = criadoEm ?? DateTimeOffset.UtcNow,
    };

    private static IdentificacaoNotion Card(string? placa, string pageId = "card-1") =>
        new(pageId, "MAURICIO FERNANDO", "MG132", null, null, null, "81888880000", placa);

    [Fact]
    public async Task CardComAPlacaDeUmLeadDoCrm_AchaOLeadExistente()
    {
        using var factory = new TestDbContextFactory();
        var (db, service) = Preparar(factory);
        var doCrm = LeadDoCrm("QFZ5A97");
        db.CrmLeads.AddRange(doCrm, LeadDoCrm("AAA1B23"));
        await db.SaveChangesAsync();

        var (lead, _) = await service.EncontrarLeadAsync(Card("QFZ5A97"), CancellationToken.None);

        Assert.Equal(doCrm.Id, lead?.Id);
    }

    [Fact]
    public async Task PlacaRepetida_LeadJaVinculado_LeadAntigo_OuLeadDoNotion_NaoCasa()
    {
        using var factory = new TestDbContextFactory();
        var (db, service) = Preparar(factory);
        db.CrmLeads.AddRange(
            LeadDoCrm("QFZ5A97", pageId: "outro-card"),                                         // já ligado a outro card
            LeadDoCrm("QFZ5A97", criadoEm: DateTimeOffset.UtcNow.AddDays(-200)),                 // antigo: pode ser outro dono do veículo
            LeadDoCrm("QFZ5A97", origem: OrigemLead.MarcadorSincronizacaoNotion));               // criado pelo próprio Notion
        await db.SaveChangesAsync();

        var (lead, _) = await service.EncontrarLeadAsync(Card("QFZ5A97"), CancellationToken.None);

        Assert.Null(lead);
    }

    [Fact]
    public async Task DoisLeadsDoCrmComAMesmaPlaca_NaoChuta()
    {
        using var factory = new TestDbContextFactory();
        var (db, service) = Preparar(factory);
        db.CrmLeads.AddRange(LeadDoCrm("QFZ5A97"), LeadDoCrm("QFZ5A97"));
        await db.SaveChangesAsync();

        var (lead, _) = await service.EncontrarLeadAsync(Card("QFZ5A97"), CancellationToken.None);

        Assert.Null(lead);
    }
}
