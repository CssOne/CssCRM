using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Authorization;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Domain.Identity;
using CssVision.Web.Services.Discord;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Resumo do dia e aviso de lead novo nos canais das regionais.</summary>
public class DiscordResumoELeadNovoTests
{
    private sealed class RelogioFalso(DateTimeOffset inicio) : TimeProvider
    {
        public DateTimeOffset Agora { get; set; } = inicio;

        public override DateTimeOffset GetUtcNow() => Agora;
    }

    private static DiscordOptions Configurado() => new()
    {
        BotToken = "bot", ClientId = "app-1", ClientSecret = "segredo", GuildId = "servidor-1", UrlPublica = "https://crm.exemplo.com/",
    };

    // 7/10/2026 às 18:30 em Brasília (21:30 UTC): já passou das 18h.
    private static readonly DateTimeOffset FimDoDia = new(2026, 10, 7, 21, 30, 0, TimeSpan.Zero);

    private sealed class Cenario
    {
        public required TestDbContextFactory Factory { get; init; }
        public required ApplicationDbContext Db { get; init; }
        public required DiscordServidorFalso Servidor { get; init; }
        public required CrmRegional Mg132 { get; init; }
        public required CrmRegional Mg134 { get; init; }
        public required RelogioFalso Relogio { get; init; }
        public required CrmPipelineStage Ganho { get; init; }

        public DiscordAvisosNosCanaisService Servico() =>
            new(Db, Servidor, Options.Create(Configurado()), NullLogger<DiscordAvisosNosCanaisService>.Instance, Relogio);

        public string CanalDa(CrmRegional r) => Db.CrmDiscordCanais.Single(c => c.Chave == $"regional:{r.Id}").DiscordCanalId;
    }

    private static async Task<Cenario> MontarAsync(TestDbContextFactory factory, DateTimeOffset agora)
    {
        var db = factory.CreateContext();
        var mg132 = await factory.CriarRegionalAsync(db, "MG132");
        var mg134 = await factory.CriarRegionalAsync(db, "MG134");
        var servidor = new DiscordServidorFalso();
        await new DiscordGruposService(db, servidor, Options.Create(Configurado()), NullLogger<DiscordGruposService>.Instance).SincronizarAsync(CancellationToken.None);
        var ganho = await factory.CriarEtapaAsync(db, "Ganho", 9, TipoEtapaPipeline.Ganho);
        servidor.Avisos.Clear();
        servidor.Cartoes.Clear();
        return new Cenario { Factory = factory, Db = db, Servidor = servidor, Mg132 = mg132, Mg134 = mg134, Relogio = new RelogioFalso(agora), Ganho = ganho };
    }

    private static async Task<ApplicationUser> ConsultoraAsync(Cenario c, string nome, CrmRegional regional)
    {
        var u = await c.Factory.CriarUsuarioAsync(c.Db, nome);
        u.RegionalId = regional.Id;
        await c.Db.SaveChangesAsync();
        await c.Factory.AtribuirPapelAsync(c.Db, u, Roles.Comercial);
        return u;
    }

    private static async Task VendaAsync(Cenario c, ApplicationUser consultora, decimal adesao, DateTimeOffset? fechamento = null)
    {
        var lead = new CrmLead { NomeOuRazaoSocial = "Cliente Secreto", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = consultora.Id };
        c.Db.CrmLeads.Add(lead);
        await c.Db.SaveChangesAsync();
        c.Db.CrmOpportunities.Add(new CrmOpportunity
        {
            LeadId = lead.Id, Titulo = "V", ResponsavelId = consultora.Id, EtapaId = c.Ganho.Id, PagamentoAdesao = adesao,
            DataEfetivaFechamento = fechamento ?? new DateTimeOffset(2026, 10, 7, 18, 0, 0, TimeSpan.Zero),
        });
        await c.Db.SaveChangesAsync();
    }

    private static Task LigarAsync(Cenario c, bool resumo = false, bool leadNovo = false) =>
        c.Servico().DefinirConfiguracaoAsync(new DiscordAvisosCanaisDto(false, false, false, resumo, leadNovo), CancellationToken.None);

    // ---------- resumo do dia ----------

    [Fact]
    public async Task Resumo_DesligadoOuAntesDas18h_NaoPublicaNada()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory, FimDoDia);
        var ana = await ConsultoraAsync(c, "Ana", c.Mg132);
        await VendaAsync(c, ana, 150m);

        Assert.Equal(0, await c.Servico().PublicarResumoDoDiaAsync(CancellationToken.None)); // desligado

        await LigarAsync(c, resumo: true);
        c.Relogio.Agora = new DateTimeOffset(2026, 10, 7, 20, 0, 0, TimeSpan.Zero); // 17:00 em Brasília
        Assert.Equal(0, await c.Servico().PublicarResumoDoDiaAsync(CancellationToken.None));
        Assert.Empty(c.Servidor.Cartoes);
    }

    [Fact]
    public async Task Resumo_PublicaNoCanalDaRegionalQueVendeu_ComTotaisERanking_SemDadosDeCliente_EUmaSoVezPorDia()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory, FimDoDia);
        var ana = await ConsultoraAsync(c, "Ana", c.Mg132);
        var beto = await ConsultoraAsync(c, "Beto", c.Mg132);
        await ConsultoraAsync(c, "Carla", c.Mg134); // MG134 não vendeu: sem resumo
        await VendaAsync(c, ana, 100m);
        await VendaAsync(c, ana, 200m);
        await VendaAsync(c, beto, 50m);
        await VendaAsync(c, beto, 999m, new DateTimeOffset(2026, 10, 6, 18, 0, 0, TimeSpan.Zero)); // ontem: não entra
        await LigarAsync(c, resumo: true);

        var publicados = await c.Servico().PublicarResumoDoDiaAsync(CancellationToken.None);

        Assert.Equal(1, publicados);
        var cartao = Assert.Single(c.Servidor.Cartoes);
        Assert.Equal(c.CanalDa(c.Mg132), cartao.CanalId);
        Assert.Contains("MG132", cartao.Cartao.Titulo);
        Assert.Equal("3", cartao.Cartao.Campos!.Single(f => f.Nome == "Vendas").Valor);
        var melhores = cartao.Cartao.Campos!.Single(f => f.Nome == "Melhores do dia").Valor;
        Assert.StartsWith("🥇 Ana — 2 vendas", melhores);
        Assert.Contains("🥈 Beto — 1 venda", melhores);
        Assert.DoesNotContain("Secreto", string.Join(" ", cartao.Cartao.Campos!.Select(f => f.Valor)) + cartao.Cartao.Descricao);

        Assert.Equal(0, await c.Servico().PublicarResumoDoDiaAsync(CancellationToken.None)); // já saiu hoje
        Assert.Single(c.Servidor.Cartoes);
    }

    [Fact]
    public async Task Resumo_ComMetaDoMes_MostraOProgresso_ESemVendaNoDiaNaoPublica()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory, FimDoDia);
        var ana = await ConsultoraAsync(c, "Ana", c.Mg132);
        await VendaAsync(c, ana, 300m);
        c.Db.CrmRegionalGoals.Add(new CrmRegionalGoal { RegionalId = c.Mg132.Id, MesReferencia = new DateOnly(2026, 10, 1), MetaQuantidadeVendas = 10, MetaValor = 1000m });
        await c.Db.SaveChangesAsync();
        await LigarAsync(c, resumo: true);

        await c.Servico().PublicarResumoDoDiaAsync(CancellationToken.None);

        var meta = Assert.Single(c.Servidor.Cartoes).Cartao.Campos!.Single(f => f.Nome == "Meta do mês").Valor;
        Assert.Contains("30%", meta);
    }

    [Fact]
    public async Task Resumo_DiaSemVendas_NaoPublicaNada()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory, FimDoDia);
        await LigarAsync(c, resumo: true);

        Assert.Equal(0, await c.Servico().PublicarResumoDoDiaAsync(CancellationToken.None));
        Assert.Empty(c.Servidor.Cartoes);
    }

    // ---------- lead novo ----------

    private static async Task<CrmLead> LeadDeAnuncioAsync(Cenario c, ApplicationUser consultora)
    {
        var lead = new CrmLead
        {
            NomeOuRazaoSocial = "Cliente Secreto", TipoPessoa = TipoPessoa.Fisica, MetaLeadId = Guid.NewGuid().ToString("N"), Telefone = "31999990000",
            ProdutoInteresse = "AGV", Origem = "Meta Ads", ResponsavelId = consultora.Id,
        };
        c.Db.CrmLeads.Add(lead);
        await c.Db.SaveChangesAsync();
        return lead;
    }

    [Fact]
    public async Task LeadNovo_LigarComecaDeAgora_NaoDespejaOQueChegouAntes_EDepoisAvisaUmPorLead_SemDadosDoCliente()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory, DateTimeOffset.UtcNow);
        var ana = await ConsultoraAsync(c, "Ana", c.Mg132);
        await LeadDeAnuncioAsync(c, ana); // chegou antes de ligar
        c.Relogio.Agora = DateTimeOffset.UtcNow; // o relógio de verdade anda; o falso acompanha na hora de ligar
        await LigarAsync(c, leadNovo: true);
        await Task.Delay(30);
        c.Relogio.Agora = DateTimeOffset.UtcNow;
        Assert.Equal(0, await c.Servico().PublicarLeadsNovosAsync(CancellationToken.None));

        await Task.Delay(30);
        var novo = await LeadDeAnuncioAsync(c, ana);
        await Task.Delay(30);
        c.Relogio.Agora = DateTimeOffset.UtcNow;
        Assert.Equal(1, await c.Servico().PublicarLeadsNovosAsync(CancellationToken.None));

        var aviso = Assert.Single(c.Servidor.Cartoes);
        Assert.Equal(c.CanalDa(c.Mg132), aviso.CanalId);
        Assert.Equal("📥 Lead novo", aviso.Cartao.Titulo);
        Assert.Contains("Ana", aviso.Cartao.Descricao);
        Assert.Equal($"https://crm.exemplo.com/app/crm/leads/kanban?lead={novo.Id}", aviso.Cartao.Url);
        var texto = aviso.Cartao.Titulo + aviso.Cartao.Descricao + string.Join(" ", aviso.Cartao.Campos!.Select(f => f.Valor));
        Assert.DoesNotContain("Secreto", texto);
        Assert.DoesNotContain("31999990000", texto);

        await Task.Delay(30);
        c.Relogio.Agora = DateTimeOffset.UtcNow;
        Assert.Equal(0, await c.Servico().PublicarLeadsNovosAsync(CancellationToken.None)); // o mesmo lead não é avisado de novo
    }

    [Fact]
    public async Task LeadNovo_Desligado_OuLeadQueNaoVeioDeAnuncio_NaoAvisa()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory, DateTimeOffset.UtcNow);
        var ana = await ConsultoraAsync(c, "Ana", c.Mg132);
        await LeadDeAnuncioAsync(c, ana);
        await Task.Delay(30);
        c.Relogio.Agora = DateTimeOffset.UtcNow;
        Assert.Equal(0, await c.Servico().PublicarLeadsNovosAsync(CancellationToken.None)); // desligado

        await LigarAsync(c, leadNovo: true);
        await Task.Delay(30);
        c.Relogio.Agora = DateTimeOffset.UtcNow;
        await c.Servico().PublicarLeadsNovosAsync(CancellationToken.None);
        await Task.Delay(30);
        c.Db.CrmLeads.Add(new CrmLead { NomeOuRazaoSocial = "Indicação", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = ana.Id }); // sem sinal de anúncio
        await c.Db.SaveChangesAsync();
        await Task.Delay(30);
        c.Relogio.Agora = DateTimeOffset.UtcNow;

        Assert.Equal(0, await c.Servico().PublicarLeadsNovosAsync(CancellationToken.None));
        Assert.Empty(c.Servidor.Cartoes);
    }

    [Fact]
    public async Task Configuracao_GuardaOsDoisAvisosNovos()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory, FimDoDia);

        await LigarAsync(c, resumo: true, leadNovo: true);

        Assert.Equal(new DiscordAvisosCanaisDto(false, false, false, true, true), await c.Servico().ObterConfiguracaoAsync(CancellationToken.None));
    }
}
