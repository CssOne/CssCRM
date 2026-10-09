using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Authorization;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Domain.Identity;
using CssVision.Web.Services.Crm;
using CssVision.Web.Services.Discord;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Avisos automáticos do CRM nos canais das regionais: venda fechada, meta batida e resumo diário de leads parados.</summary>
public class DiscordAvisosNosCanaisTests
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

    // 7/10/2026 às 13:00 em Brasília (16:00 UTC).
    private static readonly DateTimeOffset MeioDia = new(2026, 10, 7, 16, 0, 0, TimeSpan.Zero);

    private sealed class Cenario
    {
        public required TestDbContextFactory Factory { get; init; }
        public required ApplicationDbContext Db { get; init; }
        public required DiscordServidorFalso Servidor { get; init; }
        public required CrmRegional Mg132 { get; init; }
        public required CrmRegional Mg134 { get; init; }
        public required RelogioFalso Relogio { get; init; }
        public required CrmPipelineStage Ganho { get; init; }

        public DiscordAvisosNosCanaisService Servico(DiscordOptions? opcoes = null) =>
            new(Db, Servidor, Options.Create(opcoes ?? Configurado()), NullLogger<DiscordAvisosNosCanaisService>.Instance, Relogio);

        public string CanalDa(CrmRegional r) => Db.CrmDiscordCanais.Single(c => c.Chave == $"regional:{r.Id}").DiscordCanalId;
    }

    private static async Task<Cenario> MontarAsync(TestDbContextFactory factory, DateTimeOffset? agora = null)
    {
        var db = factory.CreateContext();
        var mg132 = await factory.CriarRegionalAsync(db, "MG132");
        var mg134 = await factory.CriarRegionalAsync(db, "MG134");
        var servidor = new DiscordServidorFalso();
        await new DiscordGruposService(db, servidor, Options.Create(Configurado()), NullLogger<DiscordGruposService>.Instance).SincronizarAsync(CancellationToken.None);
        var ganho = await factory.CriarEtapaAsync(db, "Ganho", 9, TipoEtapaPipeline.Ganho);
        return new Cenario { Factory = factory, Db = db, Servidor = servidor, Mg132 = mg132, Mg134 = mg134, Relogio = new RelogioFalso(agora ?? MeioDia), Ganho = ganho };
    }

    private static async Task<ApplicationUser> ConsultoraAsync(Cenario c, string nome, CrmRegional? regional)
    {
        var u = await c.Factory.CriarUsuarioAsync(c.Db, nome);
        u.RegionalId = regional?.Id;
        await c.Db.SaveChangesAsync();
        await c.Factory.AtribuirPapelAsync(c.Db, u, Roles.Comercial);
        return u;
    }

    private static async Task<CrmOpportunity> VendaAsync(Cenario c, ApplicationUser consultora, decimal adesao, DateTimeOffset? fechamento = null)
    {
        var lead = new CrmLead { NomeOuRazaoSocial = "Cliente Secreto", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = consultora.Id };
        c.Db.CrmLeads.Add(lead);
        await c.Db.SaveChangesAsync();
        var venda = new CrmOpportunity
        {
            LeadId = lead.Id, Titulo = "V", ResponsavelId = consultora.Id, EtapaId = c.Ganho.Id, PagamentoAdesao = adesao,
            DataEfetivaFechamento = fechamento ?? c.Relogio.Agora,
        };
        c.Db.CrmOpportunities.Add(venda);
        await c.Db.SaveChangesAsync();
        return venda;
    }

    private static Task LigarAsync(Cenario c, bool venda = false, bool meta = false, bool parados = false) =>
        c.Servico().DefinirConfiguracaoAsync(new DiscordAvisosCanaisDto(venda, meta, parados), CancellationToken.None);

    [Fact]
    public async Task Configuracao_NasceTodaDesligada_EGuardaOQueOAdministradorLigar()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);

        Assert.Equal(new DiscordAvisosCanaisDto(false, false, false), await c.Servico().ObterConfiguracaoAsync(CancellationToken.None));

        await LigarAsync(c, venda: true, parados: true);
        Assert.Equal(new DiscordAvisosCanaisDto(true, false, true), await c.Servico().ObterConfiguracaoAsync(CancellationToken.None));

        await LigarAsync(c); // desliga tudo
        Assert.Equal(new DiscordAvisosCanaisDto(false, false, false), await c.Servico().ObterConfiguracaoAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Venda_DesligadoNaoPublicaNada()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var ana = await ConsultoraAsync(c, "Ana", c.Mg132);
        var venda = await VendaAsync(c, ana, 300m);

        await c.Servico().PublicarVendaAsync(venda.Id, CancellationToken.None);

        Assert.Empty(c.Servidor.Avisos);
    }

    [Fact]
    public async Task Venda_PublicaNoCanalDaRegionalDaConsultora_SemDadosDoCliente()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        await LigarAsync(c, venda: true);
        var ana = await ConsultoraAsync(c, "Ana", c.Mg132);
        var venda = await VendaAsync(c, ana, 300m);

        await c.Servico().PublicarVendaAsync(venda.Id, CancellationToken.None);

        var aviso = Assert.Single(c.Servidor.Avisos);
        Assert.Equal(c.CanalDa(c.Mg132), aviso.CanalId);
        Assert.Contains("Ana", aviso.Texto);
        Assert.Contains("MG132", aviso.Texto);
        Assert.Contains("300,00", aviso.Texto);
        Assert.DoesNotContain("Cliente Secreto", aviso.Texto);
    }

    [Fact]
    public async Task Venda_SemValorDeAdesao_PublicaSemFalarDeValor()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        await LigarAsync(c, venda: true);
        var ana = await ConsultoraAsync(c, "Ana", c.Mg132);
        var venda = await VendaAsync(c, ana, 0m);

        await c.Servico().PublicarVendaAsync(venda.Id, CancellationToken.None);

        Assert.DoesNotContain("Adesão", Assert.Single(c.Servidor.Avisos).Texto);
    }

    [Fact]
    public async Task Venda_AntigaOuSemRegionalOuComDiscordDesligado_NaoPublica()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        await LigarAsync(c, venda: true);
        var ana = await ConsultoraAsync(c, "Ana", c.Mg132);
        var semRegional = await ConsultoraAsync(c, "Sem", null);

        var antiga = await VendaAsync(c, ana, 100m, fechamento: c.Relogio.Agora.AddDays(-10)); // correção de cadastro, não "acabou de fechar"
        var orfa = await VendaAsync(c, semRegional, 100m);
        var normal = await VendaAsync(c, ana, 100m);
        var servico = c.Servico();

        await servico.PublicarVendaAsync(antiga.Id, CancellationToken.None);
        await servico.PublicarVendaAsync(orfa.Id, CancellationToken.None);
        await c.Servico(new DiscordOptions()).PublicarVendaAsync(normal.Id, CancellationToken.None);
        await servico.PublicarVendaAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Empty(c.Servidor.Avisos);
    }

    [Fact]
    public async Task Venda_FalhaDoDiscordNaoSobe()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        await LigarAsync(c, venda: true);
        var ana = await ConsultoraAsync(c, "Ana", c.Mg132);
        var venda = await VendaAsync(c, ana, 100m);
        c.Servidor.SemPermissao = true;

        await c.Servico().PublicarVendaAsync(venda.Id, CancellationToken.None); // não lança
    }

    [Fact]
    public async Task MetaBatida_PublicaQuandoAVendaVirouAChave_EUmaSoVezPorMes()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        await LigarAsync(c, meta: true);
        var ana = await ConsultoraAsync(c, "Ana", c.Mg132);
        c.Db.CrmRegionalGoals.Add(new CrmRegionalGoal { RegionalId = c.Mg132.Id, MesReferencia = new DateOnly(2026, 10, 1), MetaQuantidadeVendas = 0, MetaValor = 1000m });
        await c.Db.SaveChangesAsync();
        var servico = c.Servico();

        var v1 = await VendaAsync(c, ana, 600m);
        await servico.PublicarVendaAsync(v1.Id, CancellationToken.None);
        Assert.Empty(c.Servidor.Avisos); // 600 de 1000: ainda não

        var v2 = await VendaAsync(c, ana, 500m);
        await servico.PublicarVendaAsync(v2.Id, CancellationToken.None);
        var aviso = Assert.Single(c.Servidor.Avisos);
        Assert.Contains("bateu a meta", aviso.Texto);
        Assert.Contains("MG132", aviso.Texto);
        Assert.Equal(c.CanalDa(c.Mg132), aviso.CanalId);

        var v3 = await VendaAsync(c, ana, 400m);
        await servico.PublicarVendaAsync(v3.Id, CancellationToken.None);
        Assert.Single(c.Servidor.Avisos); // já avisou neste mês
    }

    [Fact]
    public async Task MetaBatida_ComMetaDeValorEDeQuantidade_SoQuandoAsDuasForemAtingidas()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        await LigarAsync(c, meta: true);
        var ana = await ConsultoraAsync(c, "Ana", c.Mg132);
        c.Db.CrmRegionalGoals.Add(new CrmRegionalGoal { RegionalId = c.Mg132.Id, MesReferencia = new DateOnly(2026, 10, 1), MetaQuantidadeVendas = 2, MetaValor = 500m });
        await c.Db.SaveChangesAsync();
        var servico = c.Servico();

        var v1 = await VendaAsync(c, ana, 700m); // valor batido, quantidade não (1 de 2)
        await servico.PublicarVendaAsync(v1.Id, CancellationToken.None);
        Assert.Empty(c.Servidor.Avisos);

        var v2 = await VendaAsync(c, ana, 50m); // agora as duas
        await servico.PublicarVendaAsync(v2.Id, CancellationToken.None);
        Assert.Contains("bateu a meta", Assert.Single(c.Servidor.Avisos).Texto);
    }

    [Fact]
    public async Task MetaBatida_SemMetaCadastradaOuDesligado_NaoPublica()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var ana = await ConsultoraAsync(c, "Ana", c.Mg132);
        var venda = await VendaAsync(c, ana, 5000m);

        await LigarAsync(c, meta: true);
        await c.Servico().PublicarVendaAsync(venda.Id, CancellationToken.None); // sem meta cadastrada
        Assert.Empty(c.Servidor.Avisos);

        c.Db.CrmRegionalGoals.Add(new CrmRegionalGoal { RegionalId = c.Mg132.Id, MesReferencia = new DateOnly(2026, 10, 1), MetaValor = 100m });
        await c.Db.SaveChangesAsync();
        await LigarAsync(c); // desligado
        await c.Servico().PublicarVendaAsync(venda.Id, CancellationToken.None);
        Assert.Empty(c.Servidor.Avisos);
    }

    /// <summary>Lead de anúncio parado: chegou há <paramref name="diasAtras"/> dias, sem contato nem atualização desde então.</summary>
    private static async Task<CrmLead> LeadParadoAsync(Cenario c, ApplicationUser consultora, int diasAtras, bool deAnuncio = true)
    {
        var quando = c.Relogio.Agora.AddDays(-diasAtras);
        var lead = new CrmLead
        {
            NomeOuRazaoSocial = "Lead", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = consultora.Id, CriadoEm = quando,
            MetaLeadId = deAnuncio ? Guid.NewGuid().ToString() : null,
        };
        c.Db.CrmLeads.Add(lead);
        await c.Db.SaveChangesAsync();
        // O salvamento carimba a atribuição como "agora": volta para a data antiga sem passar pelo SaveChanges (que carimbaria AtualizadoEm).
        await c.Db.CrmLeads.Where(l => l.Id == lead.Id).ExecuteUpdateAsync(s => s.SetProperty(l => l.ResponsavelAtribuidoEm, quando));
        return lead;
    }

    [Fact]
    public async Task LeadsParados_PublicaUmResumoPorRegional_SoComContagem()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        await LigarAsync(c, parados: true);
        var ana = await ConsultoraAsync(c, "Ana", c.Mg132);
        var bia = await ConsultoraAsync(c, "Bia", c.Mg134);
        await LeadParadoAsync(c, ana, 7);
        await LeadParadoAsync(c, ana, 9);
        await LeadParadoAsync(c, bia, 6);
        await LeadParadoAsync(c, ana, 2);                          // recente: não é parado
        await LeadParadoAsync(c, ana, 90);                         // velho demais (fora da janela de 60 dias)
        await LeadParadoAsync(c, ana, 8, deAnuncio: false);        // card do Notion: não conta

        var publicados = await c.Servico().PublicarLeadsParadosDoDiaAsync(CancellationToken.None);

        Assert.Equal(2, publicados);
        var da132 = c.Servidor.Avisos.Single(a => a.CanalId == c.CanalDa(c.Mg132));
        var da134 = c.Servidor.Avisos.Single(a => a.CanalId == c.CanalDa(c.Mg134));
        Assert.Contains("2 leads", da132.Texto);
        Assert.Contains("1 lead ", da134.Texto);
        Assert.DoesNotContain("Ana", da132.Texto);
    }

    [Fact]
    public async Task LeadsParados_SaiUmaVezPorDia_EAmanhaDeNovo()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        await LigarAsync(c, parados: true);
        var ana = await ConsultoraAsync(c, "Ana", c.Mg132);
        await LeadParadoAsync(c, ana, 7);
        var servico = c.Servico();

        Assert.Equal(1, await servico.PublicarLeadsParadosDoDiaAsync(CancellationToken.None));
        Assert.Equal(0, await servico.PublicarLeadsParadosDoDiaAsync(CancellationToken.None)); // mesmo dia

        c.Relogio.Agora = c.Relogio.Agora.AddDays(1);
        Assert.Equal(1, await servico.PublicarLeadsParadosDoDiaAsync(CancellationToken.None));
    }

    [Fact]
    public async Task LeadsParados_NaoSaiAntesDas9h30_NemDesligado_NemSemRegionalComCanal()
    {
        using var factory = new TestDbContextFactory();
        // 7/10 às 09:00 em Brasília (12:00 UTC)
        var c = await MontarAsync(factory, new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero));
        var ana = await ConsultoraAsync(c, "Ana", c.Mg132);
        await LeadParadoAsync(c, ana, 7);

        Assert.Equal(0, await c.Servico().PublicarLeadsParadosDoDiaAsync(CancellationToken.None)); // desligado

        await LigarAsync(c, parados: true);
        Assert.Equal(0, await c.Servico().PublicarLeadsParadosDoDiaAsync(CancellationToken.None)); // ainda 9h00

        c.Relogio.Agora = new DateTimeOffset(2026, 10, 7, 12, 31, 0, TimeSpan.Zero); // 9h31
        Assert.Equal(1, await c.Servico().PublicarLeadsParadosDoDiaAsync(CancellationToken.None));
    }

    [Fact]
    public async Task LeadsParados_FalhaDoDiscordNaoRepeteATentativaNoMesmoDia()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        await LigarAsync(c, parados: true);
        var ana = await ConsultoraAsync(c, "Ana", c.Mg132);
        await LeadParadoAsync(c, ana, 7);
        c.Servidor.SemPermissao = true;

        Assert.Equal(0, await c.Servico().PublicarLeadsParadosDoDiaAsync(CancellationToken.None)); // falhou, sem lançar

        c.Servidor.SemPermissao = false;
        Assert.Equal(0, await c.Servico().PublicarLeadsParadosDoDiaAsync(CancellationToken.None)); // não insiste hoje; amanhã tenta de novo
        c.Relogio.Agora = c.Relogio.Agora.AddDays(1);
        Assert.Equal(1, await c.Servico().PublicarLeadsParadosDoDiaAsync(CancellationToken.None));
    }

    [Fact]
    public async Task DiscordDesligado_NaoPublicaNada()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        await LigarAsync(c, venda: true, meta: true, parados: true);
        var ana = await ConsultoraAsync(c, "Ana", c.Mg132);
        await LeadParadoAsync(c, ana, 7);

        Assert.Equal(0, await c.Servico(new DiscordOptions()).PublicarLeadsParadosDoDiaAsync(CancellationToken.None));
        Assert.Empty(c.Servidor.Avisos);
    }
}
