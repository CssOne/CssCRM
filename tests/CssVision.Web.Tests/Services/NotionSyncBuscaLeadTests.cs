using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Notion;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static CssVision.Web.Services.Notion.NotionSyncService;

namespace CssVision.Web.Tests.Services;

/// <summary>Como a sincronização acha o lead de um card do Notion (NotionSyncService.EncontrarLeadAsync).</summary>
public class NotionSyncBuscaLeadTests
{
    private static (ApplicationDbContext Db, NotionSyncService Service) Preparar(TestDbContextFactory factory)
    {
        var db = factory.CreateContext();
        return (db, new NotionSyncService(db, null!, NullLogger<NotionSyncService>.Instance));
    }

    private static CrmLead Lead(string nome, string? doc = null, string? email = null, string? telefone = null,
        string? pageId = null, string regional = "MG132", bool arquivado = false) => new()
    {
        NomeOuRazaoSocial = nome,
        TipoPessoa = TipoPessoa.Fisica,
        DocumentoNormalizado = doc,
        EmailNormalizado = email,
        TelefoneNormalizado = telefone,
        NotionPageId = pageId,
        Regional = regional,
        Origem = "Sincronização Notion",
        Arquivado = arquivado,
    };

    private static IdentificacaoNotion Card(string nome, string pageId = "card-1", string regional = "MG132",
        string? doc = null, string? docSemZero = null, string? email = null, string? telefone = null) =>
        new(pageId, nome, regional, doc, docSemZero, email, telefone);

    [Theory]
    [InlineData("100.309", null, null)]           // número qualquer digitado no campo CPF
    [InlineData("12345678900", null, null)]       // 11 dígitos, dígitos verificadores errados
    [InlineData("52998224725", "52998224725", null)]
    [InlineData("1234567890", "01234567890", "1234567890")] // CPF que começa com 0 perde o zero no Notion (campo número)
    [InlineData(null, null, null)]
    public void NormalizarCpfNotion_SoAceitaDocumentoValido(string? bruto, string? esperado, string? semZero)
    {
        Assert.Equal((esperado, semZero), NormalizarCpfNotion(bruto));
    }

    [Fact]
    public async Task CardJaVinculado_AchaPeloVinculo_MesmoComDadosDiferentes()
    {
        using var factory = new TestDbContextFactory();
        var (db, service) = Preparar(factory);
        var vinculado = Lead("Nome antigo", pageId: "card-1");
        db.CrmLeads.AddRange(vinculado, Lead("Outro", email: "x@y.com"));
        await db.SaveChangesAsync();

        var (lead, arquivado) = await service.EncontrarLeadAsync(Card("Nome novo", email: "x@y.com"), CancellationToken.None);

        Assert.Same(vinculado, lead);
        Assert.False(arquivado);
    }

    [Fact]
    public async Task CardLigadoALeadArquivado_NaoRecriaNemUsaOutroLead()
    {
        using var factory = new TestDbContextFactory();
        var (db, service) = Preparar(factory);
        db.CrmLeads.AddRange(Lead("Cliente", pageId: "card-1", arquivado: true), Lead("Cliente", email: "c@c.com"));
        await db.SaveChangesAsync();

        var (lead, arquivado) = await service.EncontrarLeadAsync(Card("Cliente", email: "c@c.com"), CancellationToken.None);

        Assert.Null(lead);
        Assert.True(arquivado);
    }

    [Fact]
    public async Task CpfInvalido_NaoCasaComLeadDeOutroCliente()
    {
        // Caso real: card "Gilmar Vieira" com CPF 100.309 atualizava o lead "Yan", gravado com documento "100".
        using var factory = new TestDbContextFactory();
        var (db, service) = Preparar(factory);
        var yan = Lead("Yan", doc: "100");
        var gilmar = Lead("Gilmar Vieira", email: "gilmaresc@yahoo.com.br", telefone: "11976272110", regional: "CSS Growth Sales");
        db.CrmLeads.AddRange(yan, gilmar);
        await db.SaveChangesAsync();

        var (doc, semZero) = NormalizarCpfNotion("100.309");
        var (lead, _) = await service.EncontrarLeadAsync(
            Card("Gilmar Vieira", regional: "CSS Growth Sales", doc: doc, docSemZero: semZero, email: "gilmaresc@yahoo.com.br"), CancellationToken.None);

        Assert.Same(gilmar, lead);
    }

    [Fact]
    public async Task CpfQuePerdeuOZero_CasaComLeadGravadoSemOZero()
    {
        using var factory = new TestDbContextFactory();
        var (db, service) = Preparar(factory);
        var antigo = Lead("Cliente", doc: "1234567890");
        db.CrmLeads.Add(antigo);
        await db.SaveChangesAsync();

        var (doc, semZero) = NormalizarCpfNotion("1234567890");
        var (lead, _) = await service.EncontrarLeadAsync(Card("Cliente", doc: doc, docSemZero: semZero), CancellationToken.None);

        Assert.Same(antigo, lead);
    }

    [Fact]
    public async Task LeadSemNenhumDado_EAchadoPeloNome_QuandoEUnicoNaRegional()
    {
        // Caso real: "Claudete Gomes" ficou sem telefone/e-mail no CRM (o WhatsApp do card tinha o e-mail dela).
        using var factory = new TestDbContextFactory();
        var (db, service) = Preparar(factory);
        var claudete = Lead("Claudete Gomes");
        db.CrmLeads.AddRange(claudete, Lead("Claudete Gomes", regional: "MG134"));
        await db.SaveChangesAsync();

        var (lead, _) = await service.EncontrarLeadAsync(
            Card("Claudete Gomes", email: "claudetegomess@hotmail.com", telefone: "4897400280"), CancellationToken.None);

        Assert.Same(claudete, lead);
    }

    [Fact]
    public async Task NomeRepetidoNaRegional_NaoChuta()
    {
        using var factory = new TestDbContextFactory();
        var (db, service) = Preparar(factory);
        db.CrmLeads.AddRange(Lead("55 31 8664-0322"), Lead("55 31 8664-0322"));
        await db.SaveChangesAsync();

        var (lead, _) = await service.EncontrarLeadAsync(Card("55 31 8664-0322"), CancellationToken.None);

        Assert.Null(lead);
    }

    [Fact]
    public async Task BuscaPorNome_IgnoraLeadJaVinculadoAOutroCard()
    {
        using var factory = new TestDbContextFactory();
        var (db, service) = Preparar(factory);
        db.CrmLeads.Add(Lead("S & G BRUNETTA", pageId: "card-outro"));
        await db.SaveChangesAsync();

        var (lead, _) = await service.EncontrarLeadAsync(Card("S & G BRUNETTA", pageId: "card-2"), CancellationToken.None);

        Assert.Null(lead);
    }
}
