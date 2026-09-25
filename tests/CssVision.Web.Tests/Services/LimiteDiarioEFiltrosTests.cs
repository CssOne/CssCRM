using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Authorization;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Services.Notion;
using CssVision.Web.Tests.Infrastructure;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Limite diário na distribuição e filtros de múltipla escolha do quadro de leads.</summary>
public class LimiteDiarioEFiltrosTests
{
    private static CrmLead Trafego(string nome, Guid responsavelId) => new()
    {
        NomeOuRazaoSocial = nome, TipoPessoa = TipoPessoa.Fisica, ResponsavelId = responsavelId,
        MetaLeadId = Guid.NewGuid().ToString(), CriadoManualmente = false,
    };

    [Fact]
    public async Task Rodizio_PulaQuemBateuOLimiteDiario()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var ana = await factory.CriarUsuarioAsync(db, "Ana Vendedora");
        var bruna = await factory.CriarUsuarioAsync(db, "Bruna Vendedora");
        await factory.AtribuirPapelAsync(db, ana, Roles.Comercial);
        await factory.AtribuirPapelAsync(db, bruna, Roles.Comercial);
        ana.LimiteDiarioLeads = 1;
        db.CrmLeads.Add(Trafego("Hoje", ana.Id));
        // Bruna tem 2 leads, mas de ontem: não contam no dia.
        var ontem1 = Trafego("Ontem 1", bruna.Id);
        var ontem2 = Trafego("Ontem 2", bruna.Id);
        db.CrmLeads.AddRange(ontem1, ontem2);
        await db.SaveChangesAsync();
        foreach (var l in new[] { ontem1, ontem2 }) l.ResponsavelAtribuidoEm = LeadAssignmentService.InicioDoDia().AddHours(-2);
        await db.SaveChangesAsync();

        var service = new LeadAssignmentService(db);
        Assert.False(await service.PodeReceberAsync(ana.Id, CancellationToken.None));
        Assert.True(await service.PodeReceberAsync(bruna.Id, CancellationToken.None));
        // Ana recebeu menos no mês (1 x 2), mas já bateu o limite do dia.
        Assert.Equal(bruna.Id, await service.ProximoResponsavelAsync(null, CancellationToken.None));
    }

    [Fact]
    public async Task Quadro_FiltrosDeMultiplaEscolha_EFiltroDeTipoDeIndicacao()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var ana = await factory.CriarUsuarioAsync(db, "Ana");
        var bruna = await factory.CriarUsuarioAsync(db, "Bruna");
        var carla = await factory.CriarUsuarioAsync(db, "Carla");
        CrmLead Lead(string nome, Guid resp, string? tipo) =>
            new() { NomeOuRazaoSocial = nome, TipoPessoa = TipoPessoa.Fisica, ResponsavelId = resp, TipoIndicacao = tipo };
        db.CrmLeads.AddRange(
            Lead("A-Lead", ana.Id, "Lead"),
            Lead("B-Sonhos", bruna.Id, "Contemplando Sonhos"),
            Lead("C-Pessoal", carla.Id, "Pessoal"),
            Lead("A-Indicacao", ana.Id, "Indicação")); // como vem do Notion (o SQLite dos testes só converte ASCII para minúsculas)
        await db.SaveChangesAsync();

        var service = new LeadKanbanService(db, new EquipeComercialService(db, TestDbContextFactory.MockCurrentUser(ana.Id, visaoTotal: true).Object));
        async Task<string[]> Nomes(LeadKanbanFilterRequest f) =>
            (await service.ObterBoardAsync(f, CancellationToken.None)).Colunas.SelectMany(c => c.Cartoes)
                .Select(c => c.NomeOuRazaoSocial).OrderBy(n => n).ToArray();

        Assert.Equal(["A-Indicacao", "A-Lead", "B-Sonhos"], await Nomes(new() { ResponsavelId = [ana.Id, bruna.Id] }));
        Assert.Equal(["A-Indicacao", "B-Sonhos"], await Nomes(new() { TipoIndicacao = ["contemplando sonhos", "Indicação"] }));
        // Categoria "Indicação" = qualquer tipo que não seja Lead.
        Assert.Equal(["A-Indicacao", "B-Sonhos", "C-Pessoal"], await Nomes(new() { Categoria = ["Indicação"] }));
        Assert.Equal(["A-Indicacao", "A-Lead", "B-Sonhos", "C-Pessoal"], await Nomes(new() { Categoria = ["Indicação", "Lead"] }));
    }

    [Theory]
    [InlineData("Contemplando Sonhos", false, true)]
    [InlineData("Pessoal", false, true)]
    [InlineData("Lead", true, false)]
    [InlineData(null, false, false)]
    public void TiposDeIndicacao_ContamComoIndicacao(string? tipo, bool criadoManualmente, bool esperado) =>
        Assert.Equal(esperado, NotionEtapaLead.EhIndicacao(criadoManualmente, tipo));
}
