using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>
/// Filtro de regional do quadro de leads e da lista. Todo lead é de uma das duas regionais: a do próprio lead (ou a do grupo cujo nome está
/// no lead, como "CSS Growth Sales", hoje um grupo da MG132); sem isso, a do consultor responsável; sem nenhuma das duas, o rodízio geral
/// (qualquer regional que não seja exclusiva — aqui a MG134 é). Antes só valia o texto do lead, e os leads de tráfego pago (que chegam sem
/// regional) sumiam ao filtrar por MG132.
/// </summary>
public class FiltroDeRegionalTests
{
    private static readonly string[] Exclusivas = ["MG134"];

    /// <summary>MG132 (grupo CSS Growth Sales): Ana. MG134 (grupo Consultores Externos): Bruno. Onze leads, cada um num caso.</summary>
    private static async Task<(LeadKanbanService Servico, ApplicationDbContext Db)> MontarAsync(TestDbContextFactory factory)
    {
        var db = factory.CreateContext();
        var admin = await factory.CriarUsuarioAsync(db, "Admin");
        var mg132 = await factory.CriarRegionalAsync(db, "MG132");
        var mg134 = await factory.CriarRegionalAsync(db, "MG134");
        db.CrmGrupos.AddRange(
            new CrmGrupo { RegionalId = mg132.Id, Nome = "CSS Growth Sales" },
            new CrmGrupo { RegionalId = mg134.Id, Nome = "Consultores Externos" });
        var ana = await factory.CriarUsuarioAsync(db, "Ana");
        var bruno = await factory.CriarUsuarioAsync(db, "Bruno");
        ana.RegionalId = mg132.Id;
        bruno.RegionalId = mg134.Id;
        await db.SaveChangesAsync();

        CrmLead L(string nome, string? regional, Guid? responsavel) => new()
        {
            NomeOuRazaoSocial = nome, TipoPessoa = TipoPessoa.Fisica, Regional = regional, ResponsavelId = responsavel,
        };
        db.CrmLeads.AddRange(
            L("L1 regional no lead (MG132)", "MG132", ana.Id),
            L("L2 sem regional, da Ana (MG132)", null, ana.Id),                       // o caso do tráfego pago: sumia
            L("L3 sem regional, do Bruno (MG134)", null, bruno.Id),
            L("L4 'MG 132' com espaço, do Bruno", "MG 132", bruno.Id),              // grafia diferente; a do lead vale mais que a do consultor
            L("L5 MG134 no lead, atendido pela Ana", "MG134", ana.Id),              // a do lead vale mais que a do consultor
            L("L6 sem regional e sem responsável", null, null),                      // rodízio geral: é da MG132, a que não é exclusiva
            L("L7 texto solto '132', do Bruno", "132", bruno.Id),                    // não é regional nem grupo: vale a do consultor (MG134)
            L("L8 'CSS Growth Sales' no lead, da Ana", "CSS Growth Sales", ana.Id),  // nome de grupo da MG132: é da MG132
            L("L9 texto vazio e sem responsável", "", null),                         // vazio: rodízio geral (MG132)
            L("L10 'CSS Growth Sales' no lead, do Bruno", "CSS Growth Sales", bruno.Id), // o grupo manda mais que o consultor: MG132
            L("L11 'MG134 Consultores Externos' no lead, da Ana", "MG134 Consultores Externos", ana.Id)); // regional + grupo: MG134
        await db.SaveChangesAsync();

        var usuario = TestDbContextFactory.MockCurrentUser(admin.Id, visaoTotal: true);
        var servico = new LeadKanbanService(db, new EquipeComercialService(db, usuario.Object), null, null,
            Options.Create(new DistribuicaoOptions { RegionaisExclusivas = Exclusivas }));
        return (servico, db);
    }

    private static readonly string[] TodosOsLeads = ["L1", "L2", "L3", "L4", "L5", "L6", "L7", "L8", "L9", "L10", "L11"];

    private static async Task<string[]> NomesAsync(LeadKanbanService servico, params string[] regionais) =>
        (await servico.ObterBoardAsync(new LeadKanbanFilterRequest { Regional = regionais }, CancellationToken.None))
            .Colunas.SelectMany(c => c.Cartoes).Select(c => c.NomeOuRazaoSocial.Split(' ')[0]).OrderBy(n => int.Parse(n[1..])).ToArray();

    [Fact]
    public async Task Quadro_MG132_TrazOsSemRegionalDaMG132_OsDoGrupoGrowthSales_EOsDoRodizioGeral()
    {
        using var factory = new TestDbContextFactory();
        var (servico, _) = await MontarAsync(factory);

        // L1 (regional no lead), L2 (sem regional, da Ana), L4 ("MG 132" no lead), L6 e L9 (rodízio geral), L8 e L10 (grupo CSS Growth Sales).
        Assert.Equal(["L1", "L2", "L4", "L6", "L8", "L9", "L10"], await NomesAsync(servico, "MG132"));
    }

    [Fact]
    public async Task Quadro_MG134_SoTemOQueVemMarcadoComEla_OuDeConsultorDelaSemMarca()
    {
        using var factory = new TestDbContextFactory();
        var (servico, _) = await MontarAsync(factory);

        // L3 (sem regional, do Bruno), L5 (MG134 no lead, mesmo atendido pela Ana), L7 (texto solto "132", do Bruno) e L11 ("MG134
        // Consultores Externos", mesmo da Ana). Não entram o L4, o grupo Growth Sales (L8, L10) nem o rodízio geral (L6, L9).
        Assert.Equal(["L3", "L5", "L7", "L11"], await NomesAsync(servico, "MG134"));
    }

    [Fact]
    public async Task Quadro_AsDuasRegionais_CobremTodosOsLeads_CadaUmEmExatamenteUma()
    {
        using var factory = new TestDbContextFactory();
        var (servico, _) = await MontarAsync(factory);

        Assert.Equal(TodosOsLeads, await NomesAsync(servico));
        Assert.Equal(TodosOsLeads, await NomesAsync(servico, "MG132", "MG134")); // as duas juntas = tudo
        var separadas = (await NomesAsync(servico, "MG132")).Concat(await NomesAsync(servico, "MG134")).OrderBy(n => int.Parse(n[1..])).ToArray();
        Assert.Equal(TodosOsLeads, separadas); // nenhum de fora, nenhum repetido
    }

    [Fact]
    public async Task Quadro_NomeEscritoComEspacoOuMinuscula_ContaComoAMesmaRegional()
    {
        using var factory = new TestDbContextFactory();
        var (servico, _) = await MontarAsync(factory);

        Assert.Equal(["L1", "L2", "L4", "L6", "L8", "L9", "L10"], await NomesAsync(servico, " mg 132 "));
    }

    [Fact]
    public async Task FuncaoCompartilhada_ValeParaALista_ComOMesmoResultado()
    {
        using var factory = new TestDbContextFactory();
        var (_, db) = await MontarAsync(factory);
        IQueryable<CrmLead> Leads() => db.CrmLeads.AsNoTracking();

        var nomes = (await FiltroDeRegional.Aplicar(Leads(), db.CrmRegionais.AsNoTracking(), db.CrmGrupos.AsNoTracking(), ["MG132"], Exclusivas)
                .Select(l => l.NomeOuRazaoSocial).ToListAsync())
            .Select(n => n.Split(' ')[0]).OrderBy(n => int.Parse(n[1..])).ToArray();

        Assert.Equal(["L1", "L2", "L4", "L6", "L8", "L9", "L10"], nomes);
        Assert.Equal(11, await FiltroDeRegional.Aplicar(Leads(), db.CrmRegionais.AsNoTracking(), db.CrmGrupos.AsNoTracking(), [], Exclusivas).CountAsync()); // vazio não restringe
    }

    [Fact]
    public async Task SemRegionalExclusivaConfigurada_LeadSemRegionalAparecEmQualquerFiltro()
    {
        using var factory = new TestDbContextFactory();
        var (_, db) = await MontarAsync(factory);

        // Sem a configuração, nenhuma regional é exclusiva: o lead sem regional pertence ao rodízio de todas.
        var nomes = (await FiltroDeRegional.Aplicar(db.CrmLeads.AsNoTracking(), db.CrmRegionais.AsNoTracking(), db.CrmGrupos.AsNoTracking(), ["MG134"])
                .Select(l => l.NomeOuRazaoSocial).ToListAsync())
            .Select(n => n.Split(' ')[0]).OrderBy(n => int.Parse(n[1..])).ToArray();

        Assert.Equal(["L3", "L5", "L6", "L7", "L9", "L11"], nomes);
    }
}
