using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Authorization;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Domain.Identity;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Rodízio por regional: lead da MG134 só vai para consultores da MG134; lead sem regional não vai para regional exclusiva.</summary>
public class RodizioPorRegionalTests
{
    private sealed record Cenario(
        ApplicationDbContext Db, LeadAssignmentService Servico, ApplicationUser Samys, ApplicationUser Caio, ApplicationUser Ana, ApplicationUser Bruno);

    private static async Task<Cenario> MontarAsync(TestDbContextFactory factory, bool exclusiva = true)
    {
        var db = factory.CreateContext();
        var mg132 = await factory.CriarRegionalAsync(db, "MG132");
        var mg134 = await factory.CriarRegionalAsync(db, "MG134");
        async Task<ApplicationUser> Consultor(string nome, Guid regionalId)
        {
            var u = await factory.CriarUsuarioAsync(db, nome);
            await factory.AtribuirPapelAsync(db, u, Roles.Comercial);
            u.RegionalId = regionalId;
            return u;
        }
        var samys = await Consultor("Samys", mg132.Id);
        var caio = await Consultor("Caio", mg132.Id);
        var ana = await Consultor("Ana", mg134.Id);
        var bruno = await Consultor("Bruno", mg134.Id);
        await db.SaveChangesAsync();
        var opcoes = Options.Create(new DistribuicaoOptions { RegionaisExclusivas = exclusiva ? ["MG134"] : [] });
        return new Cenario(db, new LeadAssignmentService(db, opcoes: opcoes), samys, caio, ana, bruno);
    }

    private static CrmLead Trafego(string nome, string? regional = null, Guid? responsavelId = null) => new()
    {
        NomeOuRazaoSocial = nome, TipoPessoa = TipoPessoa.Fisica, MetaLeadId = Guid.NewGuid().ToString(), Regional = regional, ResponsavelId = responsavelId,
    };

    [Fact]
    public async Task LeadDaRegional134_SoVaiParaConsultoresDaRegional134()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        // O pessoal da 132 está "zerado" e o da 134 já recebeu: ainda assim o lead da 134 não sai da 134.
        c.Db.CrmLeads.AddRange(Trafego("r1", responsavelId: c.Ana.Id), Trafego("r2", responsavelId: c.Ana.Id), Trafego("r3", responsavelId: c.Bruno.Id));
        await c.Db.SaveChangesAsync();

        var escolhido = await c.Servico.ProximoResponsavelAsync(null, "MG134", CancellationToken.None);

        Assert.Equal(c.Bruno.Id, escolhido); // quem recebeu menos na 134
        Assert.Equal(c.Bruno.Id, await c.Servico.ProximoResponsavelAsync(null, " mg 134 ", CancellationToken.None)); // grafia diferente = mesma regional
    }

    [Fact]
    public async Task LeadSemRegional_NaoVaiParaRegionalExclusiva_MesmoComConsultoresZerados()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        // Samys e Caio já receberam muito; Ana e Bruno estão com zero — e mesmo assim não entram no rodízio geral.
        for (var i = 0; i < 5; i++) c.Db.CrmLeads.Add(Trafego($"s{i}", responsavelId: c.Samys.Id));
        for (var i = 0; i < 6; i++) c.Db.CrmLeads.Add(Trafego($"k{i}", responsavelId: c.Caio.Id));
        await c.Db.SaveChangesAsync();

        var escolhido = await c.Servico.ProximoResponsavelAsync(null, CancellationToken.None);

        Assert.Equal(c.Samys.Id, escolhido);
    }

    [Fact]
    public async Task SemRegionalExclusivaConfigurada_ORodizioGeralContinuaIgual()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory, exclusiva: false);
        for (var i = 0; i < 5; i++) c.Db.CrmLeads.Add(Trafego($"s{i}", responsavelId: c.Samys.Id));
        for (var i = 0; i < 5; i++) c.Db.CrmLeads.Add(Trafego($"k{i}", responsavelId: c.Caio.Id));
        await c.Db.SaveChangesAsync();

        var escolhido = await c.Servico.ProximoResponsavelAsync(null, CancellationToken.None);

        Assert.Equal(c.Ana.Id, escolhido); // sem exclusividade, quem recebeu menos (Ana, empatada com Bruno, por nome) entra
    }

    [Fact]
    public async Task LeadDaRegional134SemConsultorDisponivel_FicaSemResponsavel_NaoVaiParaOutraEquipe()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        c.Ana.RecebeLeads = false;
        c.Bruno.LimiteMensalLeads = 1;
        c.Db.CrmLeads.Add(Trafego("ja-recebeu", responsavelId: c.Bruno.Id)); // Bruno bateu o limite
        await c.Db.SaveChangesAsync();

        Assert.Null(await c.Servico.ProximoResponsavelAsync(null, "MG134", CancellationToken.None));
        Assert.NotNull(await c.Servico.ProximoResponsavelAsync(null, "MG132", CancellationToken.None)); // a outra equipe segue normal
    }

    [Fact]
    public async Task RegionalDesconhecida_NaoVazaParaOutraEquipe()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);

        Assert.Null(await c.Servico.ProximoResponsavelAsync(null, "MG999", CancellationToken.None));
    }

    [Fact]
    public async Task Pendentes_RespeitamARegional_ELeadDeEquipeParadaNaoTravaOsOutros()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        c.Ana.RecebeLeads = false;
        c.Bruno.RecebeLeads = false; // ninguém da 134 disponível
        var da134 = Trafego("da-134", "MG134");
        var geral = Trafego("geral");
        da134.CriadoEm = DateTimeOffset.UtcNow.AddMinutes(-10); // o da 134 é o primeiro da fila
        geral.CriadoEm = DateTimeOffset.UtcNow.AddMinutes(-5);
        c.Db.CrmLeads.AddRange(da134, geral);
        await c.Db.SaveChangesAsync();

        var distribuidos = await c.Servico.DistribuirPendentesAsync(CancellationToken.None);

        Assert.Equal(1, distribuidos);
        c.Db.ChangeTracker.Clear();
        Assert.Null((await c.Db.CrmLeads.AsNoTracking().SingleAsync(l => l.NomeOuRazaoSocial == "da-134")).ResponsavelId);
        var doGeral = (await c.Db.CrmLeads.AsNoTracking().SingleAsync(l => l.NomeOuRazaoSocial == "geral")).ResponsavelId;
        Assert.Contains(doGeral, new Guid?[] { c.Samys.Id, c.Caio.Id }); // não foi para a 134

        // Quando alguém da 134 fica disponível, o lead da 134 sai da fila para ele.
        var ana = await c.Db.Users.SingleAsync(u => u.Id == c.Ana.Id);
        ana.RecebeLeads = true;
        await c.Db.SaveChangesAsync();
        Assert.Equal(1, await c.Servico.DistribuirPendentesAsync(CancellationToken.None));
        c.Db.ChangeTracker.Clear();
        Assert.Equal(c.Ana.Id, (await c.Db.CrmLeads.AsNoTracking().SingleAsync(l => l.NomeOuRazaoSocial == "da-134")).ResponsavelId);
    }

    private static PublicLeadCreateRequest Pedido(string nome, string whatsapp, string? regional = null) => new(
        Nome: nome, WhatsApp: whatsapp, Email: null, Estado: null, Placa: null, Veiculo: null, TemSeguro: null, UtilidadeVeiculo: null,
        Gclid: null, ClickId: null, MetaEmail: null, MetaLeadId: null, Fonte: null, Campanha: null, Oque: "AGV", Projeto: null, Regional: regional);

    [Fact]
    public async Task FormularioPublico_GuardaARegionalEEscolheSoDaRegional()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var intake = new PublicLeadIntakeService(c.Db, c.Servico, NullLogger<PublicLeadIntakeService>.Instance);

        var resultado = await intake.CriarAsync(
            Pedido("Cliente 134", "31999990001", regional: "mg 134"),
            CancellationToken.None);

        c.Db.ChangeTracker.Clear();
        var lead = await c.Db.CrmLeads.AsNoTracking().SingleAsync(l => l.Id == resultado.LeadId);
        Assert.Equal("MG134", lead.Regional);
        Assert.Contains(lead.ResponsavelId, new Guid?[] { c.Ana.Id, c.Bruno.Id });

        // Sem regional no formulário: rodízio geral, sem a regional exclusiva.
        var semRegional = await intake.CriarAsync(
            Pedido("Cliente geral", "31999990002"),
            CancellationToken.None);
        c.Db.ChangeTracker.Clear();
        var geral = await c.Db.CrmLeads.AsNoTracking().SingleAsync(l => l.Id == semRegional.LeadId);
        Assert.Null(geral.Regional);
        Assert.Contains(geral.ResponsavelId, new Guid?[] { c.Samys.Id, c.Caio.Id });
    }
}
