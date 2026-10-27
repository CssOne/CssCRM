using CssVision.Web.Authorization;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>
/// "Continuar distribuindo hoje" (gestor) não pode desligar a agenda de dias e horários de quem recebe leads: é só um último
/// recurso para o lead que ninguém poderia receber pela regra normal.
/// </summary>
public class DistribuicaoRespeitaAgendaTests
{
    private sealed class RelogioFixo(DateTimeOffset agora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => agora;
    }

    // Segunda-feira 12:00 em Brasília.
    private static readonly DateTimeOffset SegundaMeioDia = new(2026, 10, 5, 15, 0, 0, TimeSpan.Zero);

    private static CrmLead Trafego(string nome, Guid? responsavelId) => new()
    {
        NomeOuRazaoSocial = nome, TipoPessoa = TipoPessoa.Fisica, ResponsavelId = responsavelId,
        MetaLeadId = Guid.NewGuid().ToString(), CriadoManualmente = false,
    };

    private static async Task<(TestDbContextFactory Factory, Domain.Identity.ApplicationUser Ana, Domain.Identity.ApplicationUser Bruna)> CenarioAsync(
        Data.ApplicationDbContext db, TestDbContextFactory factory)
    {
        var ana = await factory.CriarUsuarioAsync(db, "Ana");
        var bruna = await factory.CriarUsuarioAsync(db, "Bruna");
        await factory.AtribuirPapelAsync(db, ana, Roles.Comercial);
        await factory.AtribuirPapelAsync(db, bruna, Roles.Comercial);
        return (factory, ana, bruna);
    }

    [Fact]
    public async Task Continuar_ComAlguemNoHorario_NaoEntregaParaQuemEstaForaDoHorario()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var (_, ana, bruna) = await CenarioAsync(db, factory);
        // Ana (primeira por nome, sem nenhum lead) só trabalha à tarde; agora é meio-dia. Bruna não tem restrição.
        ana.HorarioInicioLeads = new TimeOnly(13, 0);
        ana.HorarioFimLeads = new TimeOnly(18, 0);
        await db.SaveChangesAsync();
        var distribuicao = new LeadAssignmentService(db, relogio: new RelogioFixo(SegundaMeioDia));
        await distribuicao.DefinirContinuarAposLimiteAsync(true, CancellationToken.None);

        // Vários leads seguidos com o modo ligado: todos vão para quem está no horário.
        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(bruna.Id, await distribuicao.ProximoResponsavelAsync(null, CancellationToken.None));
            db.CrmLeads.Add(Trafego($"Lead {i}", bruna.Id));
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Continuar_AfrouxaOLimite_MasMantemADiaEHorario()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var (_, ana, bruna) = await CenarioAsync(db, factory);
        ana.HorarioInicioLeads = new TimeOnly(13, 0);
        ana.HorarioFimLeads = new TimeOnly(18, 0);
        bruna.LimiteDiarioLeads = 1;
        db.CrmLeads.Add(Trafego("Já recebido hoje", bruna.Id));
        await db.SaveChangesAsync();
        var distribuicao = new LeadAssignmentService(db, relogio: new RelogioFixo(SegundaMeioDia));

        // Sem o modo: Bruna bateu o limite e Ana está fora do horário — ninguém recebe.
        Assert.Null(await distribuicao.ProximoResponsavelAsync(null, CancellationToken.None));

        // Com o modo: passa do limite da Bruna (que está no horário), em vez de entregar para a Ana fora do horário.
        await distribuicao.DefinirContinuarAposLimiteAsync(true, CancellationToken.None);
        Assert.Equal(bruna.Id, await distribuicao.ProximoResponsavelAsync(null, CancellationToken.None));
    }

    [Fact]
    public async Task Continuar_ForaDoDiaDaSemana_TambemNaoEntregaSeHaAlguemNoDia()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var (_, ana, bruna) = await CenarioAsync(db, factory);
        ana.DiasSemanaLeads = JanelaRecebimentoLeads.DiasParaMascara([2, 3, 4, 5]); // terça a sexta; agora é segunda
        await db.SaveChangesAsync();
        var distribuicao = new LeadAssignmentService(db, relogio: new RelogioFixo(SegundaMeioDia));
        await distribuicao.DefinirContinuarAposLimiteAsync(true, CancellationToken.None);

        Assert.Equal(bruna.Id, await distribuicao.ProximoResponsavelAsync(null, CancellationToken.None));
    }

    [Fact]
    public async Task Continuar_ComTodosForaDaAgenda_EntregaMesmoAssim_PoisOGestorLiberou()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var (_, ana, bruna) = await CenarioAsync(db, factory);
        foreach (var u in new[] { ana, bruna })
        {
            u.HorarioInicioLeads = new TimeOnly(13, 0);
            u.HorarioFimLeads = new TimeOnly(18, 0);
        }
        await db.SaveChangesAsync();
        var distribuicao = new LeadAssignmentService(db, relogio: new RelogioFixo(SegundaMeioDia));

        // Sem o modo, ninguém recebe; com o gestor liberando, o lead é entregue mesmo fora da agenda (senão o aviso não teria saída).
        Assert.Null(await distribuicao.ProximoResponsavelAsync(null, CancellationToken.None));
        await distribuicao.DefinirContinuarAposLimiteAsync(true, CancellationToken.None);
        Assert.Equal(ana.Id, await distribuicao.ProximoResponsavelAsync(null, CancellationToken.None));
    }

    [Fact]
    public async Task LigarORecebeLeads_ForaDoHorario_SoEntregaComOModoContinuarLigado()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var (_, jessica, _) = await CenarioAsync(db, factory);
        // Jessica só recebe a partir das 18h (faixa que atravessa a meia-noite) e estava com o "Recebe leads" desligado.
        jessica.HorarioInicioLeads = new TimeOnly(18, 0);
        jessica.HorarioFimLeads = new TimeOnly(6, 0);
        jessica.RecebeLeads = false;
        var bruna = await db.Users.SingleAsync(u => u.NomeCompleto == "Bruna");
        bruna.RecebeLeads = false; // só a Jessica poderia receber
        db.CrmLeads.Add(Trafego("Parado", null));
        await db.SaveChangesAsync();
        var distribuicao = new LeadAssignmentService(db, relogio: new RelogioFixo(SegundaMeioDia));

        // Sem o modo "continuar", ligar o "Recebe leads" dela ao meio-dia não entrega nada: o horário vale.
        jessica.RecebeLeads = true;
        await db.SaveChangesAsync();
        Assert.Equal(0, await distribuicao.DistribuirPendentesAsync(CancellationToken.None));
        Assert.Null((await db.CrmLeads.SingleAsync()).ResponsavelId);

        // O gestor libera ("Continuar distribuindo hoje"): como ninguém está no horário, o lead vai para ela.
        await distribuicao.DefinirContinuarAposLimiteAsync(true, CancellationToken.None);
        Assert.Equal(jessica.Id, (await db.CrmLeads.SingleAsync()).ResponsavelId);
    }

    [Fact]
    public async Task PodeReceber_ForaDoHorario_SoComOModoLigado()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var (_, ana, _) = await CenarioAsync(db, factory);
        ana.HorarioInicioLeads = new TimeOnly(13, 0);
        ana.HorarioFimLeads = new TimeOnly(18, 0);
        await db.SaveChangesAsync();
        var distribuicao = new LeadAssignmentService(db, relogio: new RelogioFixo(SegundaMeioDia));

        Assert.False(await distribuicao.PodeReceberAsync(ana.Id, CancellationToken.None));
        await distribuicao.DefinirContinuarAposLimiteAsync(true, CancellationToken.None);
        Assert.True(await distribuicao.PodeReceberAsync(ana.Id, CancellationToken.None)); // o gestor liberou
    }

    [Fact]
    public async Task Pendentes_ComModoLigado_VaoParaQuemEstaNoHorario()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var (_, ana, bruna) = await CenarioAsync(db, factory);
        ana.HorarioInicioLeads = new TimeOnly(13, 0);
        ana.HorarioFimLeads = new TimeOnly(18, 0);
        await db.SaveChangesAsync();
        var distribuicao = new LeadAssignmentService(db, relogio: new RelogioFixo(SegundaMeioDia));
        await distribuicao.DefinirContinuarAposLimiteAsync(true, CancellationToken.None);
        db.CrmLeads.AddRange(Trafego("P1", null), Trafego("P2", null), Trafego("P3", null));
        await db.SaveChangesAsync();

        Assert.Equal(3, await distribuicao.DistribuirPendentesAsync(CancellationToken.None));

        Assert.All(db.CrmLeads.Where(l => l.NomeOuRazaoSocial.StartsWith("P")), l => Assert.Equal(bruna.Id, l.ResponsavelId));
    }
}
