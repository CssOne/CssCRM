using CssVision.Web.Authorization;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Alerta por horário, filtros do Relatório comercial e o papel Supervisor comercial.</summary>
public class HorarioFiltrosESupervisorTests
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

    [Fact]
    public async Task TodosForaDoHorario_ComLeadParado_AlertaPorHorario_EContinuarLibera()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var ana = await factory.CriarUsuarioAsync(db, "Ana");
        var bruna = await factory.CriarUsuarioAsync(db, "Bruna");
        await factory.AtribuirPapelAsync(db, ana, Roles.Comercial);
        await factory.AtribuirPapelAsync(db, bruna, Roles.Comercial);
        // As duas só recebem à tarde; agora é meio-dia.
        foreach (var u in new[] { ana, bruna })
        {
            u.HorarioInicioLeads = new TimeOnly(13, 0);
            u.HorarioFimLeads = new TimeOnly(18, 0);
        }
        db.CrmLeads.Add(Trafego("Parado", null));
        await db.SaveChangesAsync();
        var distribuicao = new LeadAssignmentService(db, relogio: new RelogioFixo(SegundaMeioDia));

        var estado = await distribuicao.ObterEstadoDistribuicaoAsync(CancellationToken.None);
        Assert.True(estado.Bloqueada);
        Assert.Equal(1, estado.LeadsBloqueados);
        Assert.Equal(1, estado.LeadsBloqueadosPorHorario);
        Assert.Equal(0, estado.LeadsBloqueadosPorLimite);
        Assert.Equal(2, estado.ConsultoresForaDoHorario);

        var mensagem = AlertaDistribuicaoService.MontarMensagem(estado);
        Assert.Contains("fora do horário", mensagem.Titulo);

        await distribuicao.DefinirContinuarAposLimiteAsync(true, CancellationToken.None);
        var depois = await distribuicao.ObterEstadoDistribuicaoAsync(CancellationToken.None);
        Assert.False(depois.Bloqueada);
        Assert.Equal(0, depois.LeadsSemResponsavel); // entregue mesmo fora do horário
    }

    [Fact]
    public async Task LimiteEHorario_JuntosAparecemNosDoisMotivos()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var ana = await factory.CriarUsuarioAsync(db, "Ana");
        var bruna = await factory.CriarUsuarioAsync(db, "Bruna");
        await factory.AtribuirPapelAsync(db, ana, Roles.Comercial);
        await factory.AtribuirPapelAsync(db, bruna, Roles.Comercial);
        ana.LimiteDiarioLeads = 1; // dentro do horário, mas no limite
        bruna.HorarioInicioLeads = new TimeOnly(13, 0); // sem limite, mas fora do horário
        bruna.HorarioFimLeads = new TimeOnly(18, 0);
        db.CrmLeads.AddRange(Trafego("Da Ana", ana.Id), Trafego("Parado", null));
        await db.SaveChangesAsync();
        var distribuicao = new LeadAssignmentService(db, relogio: new RelogioFixo(SegundaMeioDia));

        var estado = await distribuicao.ObterEstadoDistribuicaoAsync(CancellationToken.None);

        Assert.Equal(1, estado.LeadsBloqueados);
        Assert.Equal(1, estado.LeadsBloqueadosPorLimite);
        Assert.Equal(1, estado.LeadsBloqueadosPorHorario);
        Assert.Contains("limite", AlertaDistribuicaoService.MontarMensagem(estado).Titulo);
    }

    [Fact]
    public async Task Relatorio_FiltraPorConsultorEPorEtapa()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var admin = await factory.CriarUsuarioAsync(db, "Admin");
        var ana = await factory.CriarUsuarioAsync(db, "Ana");
        var bruna = await factory.CriarUsuarioAsync(db, "Bruna");
        await factory.AtribuirPapelAsync(db, ana, Roles.Comercial);
        await factory.AtribuirPapelAsync(db, bruna, Roles.Comercial);
        var ganho = await factory.CriarEtapaAsync(db, "Ganho", 9, TipoEtapaPipeline.Ganho);
        var atendimento = await factory.ObterOuCriarEtapaLeadAsync(db, "Em atendimento", 2);
        var vendaConcluida = await factory.ObterOuCriarEtapaLeadAsync(db, "Venda concluída", 5);

        CrmLead Lead(string nome, Guid resp, Guid? etapa) => new()
        {
            NomeOuRazaoSocial = nome, TipoPessoa = TipoPessoa.Fisica, ResponsavelId = resp, EtapaId = etapa,
        };
        var daAnaVenda = Lead("Ana venda", ana.Id, vendaConcluida.Id);
        var daAnaAtend = Lead("Ana atendimento", ana.Id, atendimento.Id);
        var daBrunaSemEtapa = Lead("Bruna sem etapa", bruna.Id, null);
        db.CrmLeads.AddRange(daAnaVenda, daAnaAtend, daBrunaSemEtapa);
        await db.SaveChangesAsync();
        foreach (var l in new[] { daAnaVenda, daAnaAtend, daBrunaSemEtapa }) l.CriadoEm = new DateTimeOffset(2025, 3, 10, 15, 0, 0, TimeSpan.Zero);
        db.CrmOpportunities.Add(new CrmOpportunity
        {
            LeadId = daAnaVenda.Id, Titulo = "AGV", ResponsavelId = ana.Id, EtapaId = ganho.Id,
            DataEfetivaFechamento = new DateTimeOffset(2025, 3, 12, 15, 0, 0, TimeSpan.Zero), PagamentoAdesao = 300m, Mensalidade = 150m,
        });
        await db.SaveChangesAsync();

        var usuario = TestDbContextFactory.MockCurrentUser(admin.Id, visaoTotal: true, podeGerir: true).Object;
        var relatorio = new RelatorioComercialService(db, usuario, new EquipeComercialService(db, usuario));
        var de = new DateOnly(2025, 1, 1);
        var ate = new DateOnly(2025, 12, 31);

        var todos = await relatorio.ObterAsync(de, ate, null, null, CancellationToken.None);
        Assert.Equal(3, todos.Totais.Leads);
        Assert.Equal(3, todos.PorEtapa.Count); // atendimento, venda concluída e sem etapa

        var soBruna = await relatorio.ObterAsync(de, ate, bruna.Id, null, CancellationToken.None);
        Assert.Equal(1, soBruna.Totais.Leads);
        Assert.Equal(0, soBruna.Totais.Vendas);

        var soAna = await relatorio.ObterAsync(de, ate, ana.Id, null, CancellationToken.None);
        Assert.Equal(2, soAna.Totais.Leads);
        Assert.Equal(1, soAna.Totais.Vendas);

        var soVendaConcluida = await relatorio.ObterAsync(de, ate, null, [vendaConcluida.Id], CancellationToken.None);
        Assert.Equal(1, soVendaConcluida.Totais.Leads);
        Assert.Equal(1, soVendaConcluida.Totais.Vendas);

        var atendimentoESemEtapa = await relatorio.ObterAsync(de, ate, null, [atendimento.Id, Guid.Empty], CancellationToken.None);
        Assert.Equal(2, atendimentoESemEtapa.Totais.Leads);
        Assert.Equal(0, atendimentoESemEtapa.Totais.Vendas);
        Assert.Contains(atendimentoESemEtapa.PorEtapa, e => e.EtapaId is null && e.Leads == 1);
    }

    [Fact]
    public void SupervisorComercial_TemAcessoComoAdminMenosTrafegoPago()
    {
        var s = Roles.SupervisorComercial;
        Assert.Contains(s, Roles.All);
        Assert.Contains(s, Roles.VisaoTotal);
        Assert.Contains(s, Roles.Administrativos);
        Assert.Contains(s, Roles.GestaoComercial);
        Assert.Contains(s, Roles.Comerciais);
        Assert.DoesNotContain(s, Roles.AreaMarketing);
    }
}
