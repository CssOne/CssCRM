using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Authorization;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Domain.Identity;
using CssVision.Web.Services.Crm;
using CssVision.Web.Services.Discord;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>"Conversar sobre este lead": resumo do lead (sem dados de contato) + link, só para quem enxerga o lead.</summary>
public class DiscordLeadNoChatTests
{
    private static DiscordOptions Configurado() => new()
    {
        BotToken = "bot", ClientId = "app-1", ClientSecret = "segredo", GuildId = "servidor-1", UrlPublica = "https://crm.exemplo.com/",
    };

    private sealed class LeadsFalsos : ILeadCompartilhavel
    {
        public Dictionary<Guid, LeadCompartilhado> Visiveis { get; } = [];

        public int Consultas { get; private set; }

        public Task<LeadCompartilhado> ObterAsync(Guid leadId, CancellationToken ct)
        {
            Consultas++;
            return Visiveis.TryGetValue(leadId, out var lead) ? Task.FromResult(lead) : throw new CrmNotFoundException("Lead", leadId);
        }
    }

    private sealed class Cenario
    {
        public required TestDbContextFactory Factory { get; init; }
        public required ApplicationDbContext Db { get; init; }
        public required DiscordServidorFalso Servidor { get; init; }
        public required CrmRegional Mg132 { get; init; }
        public required LeadsFalsos Leads { get; init; }

        public DiscordChatService Servico() => new(Db, Servidor, new MemoryCache(new MemoryCacheOptions()), Options.Create(Configurado()), null, Leads);
    }

    private static async Task<Cenario> MontarAsync(TestDbContextFactory factory)
    {
        var db = factory.CreateContext();
        var mg132 = await factory.CriarRegionalAsync(db, "MG132");
        var servidor = new DiscordServidorFalso();
        await new DiscordGruposService(db, servidor, Options.Create(Configurado()), NullLogger<DiscordGruposService>.Instance).SincronizarAsync(CancellationToken.None);
        return new Cenario { Factory = factory, Db = db, Servidor = servidor, Mg132 = mg132, Leads = new LeadsFalsos() };
    }

    private static async Task<ApplicationUser> PessoaAsync(Cenario c, string nome)
    {
        var u = await c.Factory.CriarUsuarioAsync(c.Db, nome);
        u.RegionalId = c.Mg132.Id;
        await c.Db.SaveChangesAsync();
        await c.Factory.AtribuirPapelAsync(c.Db, u, Roles.Comercial);
        c.Db.CrmDiscordVinculos.Add(new CrmDiscordVinculo { UsuarioId = u.Id, DiscordUserId = $"d-{nome.ToLowerInvariant()}", DiscordNome = nome, NoServidor = true, VinculadoEm = DateTimeOffset.UtcNow });
        await c.Db.SaveChangesAsync();
        return u;
    }

    private static Guid Lead(Cenario c, string nome = "Maria Souza", string? etapa = "Em atendimento (Leads)", string? responsavel = "Ana", string? produto = "AGV", string? regional = "MG132")
    {
        var id = Guid.NewGuid();
        c.Leads.Visiveis[id] = new LeadCompartilhado(id, nome, etapa, responsavel, produto, regional);
        return id;
    }

    [Fact]
    public async Task Compartilha_ResumoDoLead_ComLinkParaOCrm_SemDadosDeContato()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var ana = await PessoaAsync(c, "Ana");
        var leadId = Lead(c);

        await c.Servico().CompartilharLeadAsync(ana.Id, "geral", leadId, null, CancellationToken.None);

        var envio = Assert.Single(c.Servidor.Enviadas);
        Assert.Equal(c.Db.CrmDiscordCanais.Single(x => x.Chave == "geral").DiscordCanalId, envio.CanalId);
        Assert.Equal("Ana", envio.Nome); // publicado com o nome de quem compartilhou
        Assert.Contains("**Maria Souza**", envio.Texto);
        Assert.Contains("etapa: Em atendimento (Leads)", envio.Texto);
        Assert.Contains("responsável: Ana", envio.Texto);
        Assert.Contains("produto: AGV", envio.Texto);
        Assert.Contains("MG132", envio.Texto);
        Assert.EndsWith($"https://crm.exemplo.com/app/crm/leads/{leadId}", envio.Texto);
    }

    [Fact]
    public async Task Compartilha_ComComentarioDaPessoa()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var ana = await PessoaAsync(c, "Ana");

        await c.Servico().CompartilharLeadAsync(ana.Id, "geral", Lead(c), "  alguém já atendeu esse cliente?  ", CancellationToken.None);

        Assert.Contains("\nalguém já atendeu esse cliente?\n", Assert.Single(c.Servidor.Enviadas).Texto);
    }

    [Fact]
    public async Task Compartilha_LeadSemDetalhes_MostraSoONome()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var ana = await PessoaAsync(c, "Ana");

        await c.Servico().CompartilharLeadAsync(ana.Id, "geral", Lead(c, etapa: null, responsavel: null, produto: null, regional: null), null, CancellationToken.None);

        var texto = Assert.Single(c.Servidor.Enviadas).Texto;
        Assert.StartsWith("🔗 Lead para conversar: **Maria Souza**\n", texto);
        Assert.DoesNotContain("(", texto.Split('\n')[0]);
    }

    [Fact]
    public async Task Compartilha_NaConversaDireta_VaiParaAThread()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var ana = await PessoaAsync(c, "Ana");
        var bia = await PessoaAsync(c, "Bia");
        var servico = c.Servico();
        var conversa = await servico.IniciarConversaAsync(ana.Id, bia.Id, CancellationToken.None);

        await servico.CompartilharLeadAsync(ana.Id, conversa.Chave, Lead(c), null, CancellationToken.None);

        Assert.Equal(c.Servidor.Threads.Keys.Single(), Assert.Single(c.Servidor.Enviadas).CanalId);
    }

    [Fact]
    public async Task Recusa_LeadQueAPessoaNaoEnxerga_ENaoPublicaNada()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var ana = await PessoaAsync(c, "Ana");

        await Assert.ThrowsAsync<CrmNotFoundException>(() => c.Servico().CompartilharLeadAsync(ana.Id, "geral", Guid.NewGuid(), null, CancellationToken.None));
        Assert.Empty(c.Servidor.Enviadas);
    }

    [Fact]
    public async Task Recusa_ConversaDaQualAPessoaNaoParticipa_SemNemConsultarOLead()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var ana = await PessoaAsync(c, "Ana");
        var leadId = Lead(c);

        await Assert.ThrowsAsync<CrmForbiddenException>(() => c.Servico().CompartilharLeadAsync(ana.Id, "gestao", leadId, null, CancellationToken.None));

        Assert.Equal(0, c.Leads.Consultas); // quem não participa da conversa não descobre nada sobre o lead
        Assert.Empty(c.Servidor.Enviadas);
    }

    [Fact]
    public async Task Recusa_ComentarioLongo()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var ana = await PessoaAsync(c, "Ana");

        var ex = await Assert.ThrowsAsync<CrmBusinessException>(() =>
            c.Servico().CompartilharLeadAsync(ana.Id, "geral", Lead(c), new string('x', DiscordChatService.LimiteDoComentario + 1), CancellationToken.None));

        Assert.Equal("comentario_longo", ex.Codigo);
        Assert.Empty(c.Servidor.Enviadas);
    }

    [Fact]
    public async Task NomeMuitoLongo_ECortado()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var ana = await PessoaAsync(c, "Ana");

        await c.Servico().CompartilharLeadAsync(ana.Id, "geral", Lead(c, nome: new string('N', 400)), null, CancellationToken.None);

        Assert.Contains(new string('N', 100) + "**", Assert.Single(c.Servidor.Enviadas).Texto);
        Assert.DoesNotContain(new string('N', 101), Assert.Single(c.Servidor.Enviadas).Texto);
    }

    [Fact]
    public async Task ServicoDoLead_RepassaAFalhaDeAcessoDoLeadService_SemEsconderNada()
    {
        var leadId = Guid.NewGuid();
        var visivel = new Moq.Mock<ILeadService>();
        visivel.Setup(s => s.ObterPorIdAsync(leadId, Moq.It.IsAny<CancellationToken>())).Returns(Task.FromException<CssVision.Web.Api.Contracts.Crm.LeadDetailDto>(new CrmForbiddenException("sem acesso")));

        await Assert.ThrowsAsync<CrmForbiddenException>(() => new LeadCompartilhavelService(visivel.Object).ObterAsync(leadId, CancellationToken.None));
    }
}
