using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Authorization;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Avisos do lead do tráfego pago parado em "Sem etapa": ao consultor que não pega e à gestão quando ele demora com frequência.</summary>
public class EsperaTrafegoServiceTests
{
    private sealed class PushFalso : IPushService
    {
        public List<(Guid UsuarioId, PushMensagem Mensagem)> Enviados { get; } = [];
        public Task<string> ChavePublicaAsync(CancellationToken ct) => Task.FromResult("");
        public Task InscreverAsync(Guid usuarioId, PushInscricaoRequest request, CancellationToken ct) => Task.CompletedTask;
        public Task RemoverAsync(Guid usuarioId, string endpoint, CancellationToken ct) => Task.CompletedTask;
        public Task<int> EnviarAsync(Guid usuarioId, PushMensagem mensagem, CancellationToken ct)
        {
            Enviados.Add((usuarioId, mensagem));
            return Task.FromResult(1);
        }
    }

    private sealed class RelogioAjustavel(DateTimeOffset agora) : TimeProvider
    {
        public DateTimeOffset Agora { get; set; } = agora;
        public override DateTimeOffset GetUtcNow() => Agora;
    }

    private sealed record Cenario(TestDbContextFactory Factory, ApplicationDbContext Db, RelogioAjustavel Relogio, PushFalso Push, EsperaTrafegoService Servico,
        Guid AnaId, Guid BrunaId, Guid GestorDaAnaId, Guid AdminId, Guid SupervisorId, Guid GestorDeOutroTimeId);

    private static async Task<Cenario> CriarAsync()
    {
        var factory = new TestDbContextFactory();
        var db = factory.CreateContext();
        var gestor = await factory.CriarUsuarioAsync(db, "Gestora da Ana");
        var outroGestor = await factory.CriarUsuarioAsync(db, "Gestor de outro time");
        var ana = await factory.CriarUsuarioAsync(db, "Ana Vendedora", gestor.Id);
        var bruna = await factory.CriarUsuarioAsync(db, "Bruna Vendedora", outroGestor.Id);
        var admin = await factory.CriarUsuarioAsync(db, "Admin");
        var supervisor = await factory.CriarUsuarioAsync(db, "Supervisor");
        await factory.AtribuirPapelAsync(db, gestor, Roles.GestorComercial);
        await factory.AtribuirPapelAsync(db, outroGestor, Roles.GestorComercial);
        await factory.AtribuirPapelAsync(db, ana, Roles.Comercial);
        await factory.AtribuirPapelAsync(db, bruna, Roles.Comercial);
        await factory.AtribuirPapelAsync(db, admin, Roles.Admin);
        await factory.AtribuirPapelAsync(db, supervisor, Roles.SupervisorComercial);
        var relogio = new RelogioAjustavel(new DateTimeOffset(2026, 10, 26, 15, 0, 0, TimeSpan.Zero));
        var push = new PushFalso();
        var servico = new EsperaTrafegoService(db, push, NullLogger<EsperaTrafegoService>.Instance, relogio);
        return new Cenario(factory, db, relogio, push, servico, ana.Id, bruna.Id, gestor.Id, admin.Id, supervisor.Id, outroGestor.Id);
    }

    /// <summary>Lead do tráfego pago em "Sem etapa" que passou para o consultor há <paramref name="minutosAtras"/> minutos.</summary>
    private static CrmLead Trafego(Cenario c, string nome, Guid consultorId, int minutosAtras, bool doNotion = false) => new()
    {
        NomeOuRazaoSocial = nome, TipoPessoa = TipoPessoa.Fisica, ResponsavelId = consultorId, CriadoManualmente = false,
        MetaLeadId = Guid.NewGuid().ToString(),
        ConsentimentoOrigem = doNotion ? OrigemLead.MarcadorMigracaoNotion : null,
        ResponsavelAtribuidoEm = c.Relogio.Agora.AddMinutes(-minutosAtras),
    };

    /// <summary>Grava os leads e depois acerta "passou para o consultor em": a auditoria do contexto grava a hora real ao atribuir o responsável.</summary>
    private static async Task SalvarAsync(Cenario c, params CrmLead[] leads)
    {
        var atribuicoes = leads.Select(l => (l.Id, l.ResponsavelAtribuidoEm)).ToList();
        c.Db.CrmLeads.AddRange(leads);
        await c.Db.SaveChangesAsync();
        foreach (var (id, quando) in atribuicoes)
            await c.Db.CrmLeads.Where(l => l.Id == id).ExecuteUpdateAsync(s => s.SetProperty(l => l.ResponsavelAtribuidoEm, quando));
        c.Db.ChangeTracker.Clear();
    }

    [Fact]
    public async Task LeadNaoPegoDepois15Min_AvisaOConsultorUmaVez_EDepoisDe1h_AvisaDeNovoMaisForte()
    {
        var c = await CriarAsync();
        using var _ = c.Factory;
        await using var db = c.Db;
        await SalvarAsync(c, Trafego(c, "Cliente Novo", c.AnaId, 5));

        await c.Servico.VerificarAsync(CancellationToken.None);
        Assert.Empty(c.Push.Enviados); // ainda dentro do prazo

        c.Relogio.Agora = c.Relogio.Agora.AddMinutes(11);
        await c.Servico.VerificarAsync(CancellationToken.None);
        var aviso = Assert.Single(c.Push.Enviados);
        Assert.Equal(c.AnaId, aviso.UsuarioId);
        Assert.Equal("Lead esperando você", aviso.Mensagem.Titulo);
        Assert.Contains("Cliente Novo", aviso.Mensagem.Corpo);

        await c.Servico.VerificarAsync(CancellationToken.None);
        Assert.Single(c.Push.Enviados); // não repete

        c.Relogio.Agora = c.Relogio.Agora.AddMinutes(50);
        await c.Servico.VerificarAsync(CancellationToken.None);
        Assert.Equal(2, c.Push.Enviados.Count);
        Assert.Equal("Lead esfriando!", c.Push.Enviados[1].Mensagem.Titulo);
        Assert.All(c.Push.Enviados, e => Assert.Equal(c.AnaId, e.UsuarioId)); // só 1 lead: ainda não é reincidência

        await c.Servico.VerificarAsync(CancellationToken.None);
        Assert.Equal(2, c.Push.Enviados.Count);
    }

    [Fact]
    public async Task LeadQueOConsultorPegouAntes_NaoAvisa()
    {
        var c = await CriarAsync();
        using var _ = c.Factory;
        await using var db = c.Db;
        var etapa = await db.CrmLeadStages.FirstAsync();
        var lead = Trafego(c, "Pego a tempo", c.AnaId, 30);
        lead.EtapaId = etapa.Id;
        var arquivado = Trafego(c, "Arquivado", c.AnaId, 30);
        arquivado.Arquivado = true;
        await SalvarAsync(c, lead, arquivado);

        await c.Servico.VerificarAsync(CancellationToken.None);
        Assert.Empty(c.Push.Enviados);
    }

    [Fact]
    public async Task SoTrafegoPagoDireto_SemResponsavelOuAntigoDemais_NaoAvisa()
    {
        var c = await CriarAsync();
        using var _ = c.Factory;
        await using var db = c.Db;
        var semResponsavel = Trafego(c, "Sem dono", c.AnaId, 30);
        semResponsavel.ResponsavelId = null;
        await SalvarAsync(c,
            Trafego(c, "Do Notion", c.AnaId, 30, doNotion: true),
            semResponsavel,
            Trafego(c, "Acúmulo antigo", c.AnaId, 60 * 30),
            new CrmLead { NomeOuRazaoSocial = "Indicação", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = c.AnaId, CriadoManualmente = true, ResponsavelAtribuidoEm = c.Relogio.Agora.AddMinutes(-30) });

        await c.Servico.VerificarAsync(CancellationToken.None);
        Assert.Empty(c.Push.Enviados);
    }

    [Fact]
    public async Task ConsultorQueDemoraComFrequencia_AvisaAGestaoUmaVezPorDia()
    {
        var c = await CriarAsync();
        using var _ = c.Factory;
        await using var db = c.Db;
        // Ana: 3 leads que passaram de 1 h sem atendimento. Bruna: 1 só.
        await SalvarAsync(c,
            Trafego(c, "A1", c.AnaId, 70), Trafego(c, "A2", c.AnaId, 80), Trafego(c, "A3", c.AnaId, 90),
            Trafego(c, "B1", c.BrunaId, 75));

        await c.Servico.VerificarAsync(CancellationToken.None);

        var aviso = c.Push.Enviados.Where(e => e.Mensagem.Titulo == "Consultor demorando para pegar leads").ToList();
        // Administrador, supervisor e o gestor comercial da Ana; não o gestor de outro time.
        Assert.Equal(new[] { c.AdminId, c.SupervisorId, c.GestorDaAnaId }.Order(), aviso.Select(e => e.UsuarioId).Order());
        Assert.All(aviso, e => Assert.Contains("Ana Vendedora", e.Mensagem.Corpo));
        Assert.All(aviso, e => Assert.Contains("3 leads", e.Mensagem.Corpo));
        // Cada um dos 4 leads também gerou o aviso forte ao próprio consultor.
        Assert.Equal(4, c.Push.Enviados.Count(e => e.Mensagem.Titulo == "Lead esfriando!"));

        await c.Servico.VerificarAsync(CancellationToken.None);
        Assert.Equal(3, c.Push.Enviados.Count(e => e.Mensagem.Titulo == "Consultor demorando para pegar leads")); // não repete na hora

        c.Relogio.Agora = c.Relogio.Agora.AddHours(25);
        // Os leads de ontem passaram do piso de 24 h; o histórico (7 dias) ainda conta, e o consultor segue reincidente.
        await c.Servico.VerificarAsync(CancellationToken.None);
        Assert.Equal(6, c.Push.Enviados.Count(e => e.Mensagem.Titulo == "Consultor demorando para pegar leads"));
    }

    [Fact]
    public async Task Painel_MostraEsperaAgoraEReincidencia_RespeitandoOsConsultoresVisiveis()
    {
        var c = await CriarAsync();
        using var _ = c.Factory;
        await using var db = c.Db;
        await SalvarAsync(c,
            Trafego(c, "A1", c.AnaId, 70), Trafego(c, "A2", c.AnaId, 80), Trafego(c, "A3", c.AnaId, 90),
            Trafego(c, "B1", c.BrunaId, 20));
        await c.Servico.VerificarAsync(CancellationToken.None);

        var painel = await c.Servico.ObterPainelAsync(null, CancellationToken.None);
        Assert.Equal(["Ana Vendedora", "Bruna Vendedora"], painel.Consultores.Select(x => x.Nome));
        var ana = painel.Consultores[0];
        Assert.True(ana.Reincidente);
        Assert.Equal(3, ana.EsperandoAgora);
        Assert.Equal(3, ana.PassaramDoPrazoForte);
        Assert.Equal(c.Relogio.Agora.AddMinutes(-90), ana.MaisAntigoDesde);
        var bruna = painel.Consultores[1];
        Assert.False(bruna.Reincidente);
        Assert.Equal(1, bruna.PassaramDoPrimeiroPrazo);
        Assert.Equal(0, bruna.PassaramDoPrazoForte);

        // Gestor de outro time só enxerga os consultores que são visíveis para ele.
        var restrito = await c.Servico.ObterPainelAsync([c.BrunaId], CancellationToken.None);
        Assert.Equal("Bruna Vendedora", Assert.Single(restrito.Consultores).Nome);

        // Quando o consultor pega o lead, ele deixa de contar como esperando.
        var etapa = await db.CrmLeadStages.FirstAsync();
        await db.CrmLeads.Where(l => l.NomeOuRazaoSocial == "A1").ExecuteUpdateAsync(s => s.SetProperty(l => l.EtapaId, etapa.Id));
        Assert.Equal(2, (await c.Servico.ObterPainelAsync(null, CancellationToken.None)).Consultores[0].EsperandoAgora);
    }

    [Fact]
    public async Task LeadQuePassaParaOutroConsultor_RecomecaAEspera()
    {
        var c = await CriarAsync();
        using var _ = c.Factory;
        await using var db = c.Db;
        var lead = Trafego(c, "Redistribuído", c.AnaId, 20);
        await SalvarAsync(c, lead);
        await c.Servico.VerificarAsync(CancellationToken.None);
        Assert.Equal(c.AnaId, Assert.Single(c.Push.Enviados).UsuarioId);

        var desde = c.Relogio.Agora.AddMinutes(-16);
        await db.CrmLeads.Where(l => l.Id == lead.Id).ExecuteUpdateAsync(s => s.SetProperty(l => l.ResponsavelId, c.BrunaId).SetProperty(l => l.ResponsavelAtribuidoEm, desde));
        await c.Servico.VerificarAsync(CancellationToken.None);

        Assert.Equal(2, c.Push.Enviados.Count);
        Assert.Equal(c.BrunaId, c.Push.Enviados[1].UsuarioId);
    }
}
