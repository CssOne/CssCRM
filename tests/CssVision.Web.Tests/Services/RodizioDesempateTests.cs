using CssVision.Web.Authorization;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Domain.Identity;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Rodízio: desempate por quem recebeu lead há mais tempo (e não por ordem alfabética) e lead excluído não gasta a vez.</summary>
public class RodizioDesempateTests
{
    private sealed record Cenario(ApplicationDbContext Db, LeadAssignmentService Servico, ApplicationUser Ana, ApplicationUser Bruno, ApplicationUser Carla);

    private static async Task<Cenario> MontarAsync(TestDbContextFactory factory)
    {
        var db = factory.CreateContext();
        async Task<ApplicationUser> Consultor(string nome)
        {
            var u = await factory.CriarUsuarioAsync(db, nome);
            await factory.AtribuirPapelAsync(db, u, Roles.Comercial);
            return u;
        }
        var ana = await Consultor("Ana");
        var bruno = await Consultor("Bruno");
        var carla = await Consultor("Carla");
        await db.SaveChangesAsync();
        return new Cenario(db, new LeadAssignmentService(db), ana, bruno, carla);
    }

    private static CrmLead Trafego(string nome, Guid responsavelId, DateTimeOffset recebidoEm, bool arquivado = false) => new()
    {
        NomeOuRazaoSocial = nome, TipoPessoa = TipoPessoa.Fisica, MetaLeadId = Guid.NewGuid().ToString(), ResponsavelId = responsavelId,
        ResponsavelAtribuidoEm = recebidoEm, Arquivado = arquivado,
    };

    /// <summary>O contexto carimba "agora" ao criar o lead já atribuído; aqui voltamos a data para simular leads de antes.</summary>
    private static async Task SalvarComDatasAsync(ApplicationDbContext db, params CrmLead[] leads)
    {
        var datas = leads.ToDictionary(l => l.NomeOuRazaoSocial, l => l.ResponsavelAtribuidoEm!.Value);
        db.CrmLeads.AddRange(leads);
        await db.SaveChangesAsync();
        foreach (var lead in leads) lead.ResponsavelAtribuidoEm = datas[lead.NomeOuRazaoSocial];
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Empate_VaiParaQuemRecebeuHaMaisTempo_NaoParaOPrimeiroDoAlfabeto()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var agora = DateTimeOffset.UtcNow;
        // Todos com 1 lead no mês; a Carla recebeu o dela há mais tempo, a Ana foi a mais recente.
        await SalvarComDatasAsync(c.Db,
            Trafego("a", c.Ana.Id, agora.AddMinutes(-5)),
            Trafego("b", c.Bruno.Id, agora.AddMinutes(-30)),
            Trafego("c", c.Carla.Id, agora.AddMinutes(-90)));

        Assert.Equal(c.Carla.Id, await c.Servico.ProximoResponsavelAsync(null, CancellationToken.None));
    }

    [Fact]
    public async Task LeadExcluido_NaoGastaAVezDoConsultor()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var agora = DateTimeOffset.UtcNow;
        // Ana recebeu um lead que foi excluído; Bruno e Carla receberam um cada. Com a exclusão a Ana tem 0: é a próxima.
        await SalvarComDatasAsync(c.Db,
            Trafego("a", c.Ana.Id, agora.AddMinutes(-1), arquivado: true),
            Trafego("b", c.Bruno.Id, agora.AddMinutes(-20)),
            Trafego("c", c.Carla.Id, agora.AddMinutes(-40)));

        Assert.Equal(c.Ana.Id, await c.Servico.ProximoResponsavelAsync(null, CancellationToken.None));
    }

    [Fact]
    public async Task LeadExcluido_TambemNaoContaNosLimites()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        c.Ana.LimiteMensalLeads = 1;
        c.Db.CrmLeads.Add(Trafego("a", c.Ana.Id, DateTimeOffset.UtcNow.AddMinutes(-1), arquivado: true));
        await c.Db.SaveChangesAsync();

        Assert.True(await c.Servico.PodeReceberAsync(c.Ana.Id, CancellationToken.None));
    }
}
