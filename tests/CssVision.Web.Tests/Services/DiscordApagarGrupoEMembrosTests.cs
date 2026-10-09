using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Authorization;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Domain.Identity;
using CssVision.Web.Services.Crm;
using CssVision.Web.Services.Discord;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Apagar o canal de um grupo (e religá-lo) e a tela de membros do servidor do Discord.</summary>
public class DiscordApagarGrupoEMembrosTests
{
    private static DiscordOptions Configurado() => new()
    {
        BotToken = "bot", ClientId = "app-1", ClientSecret = "segredo", GuildId = "servidor-1", UrlPublica = "https://crm.exemplo.com/",
    };

    private sealed class Cenario
    {
        public required ApplicationDbContext Db { get; init; }
        public required DiscordServidorFalso Servidor { get; init; }
        public required CrmRegional Mg132 { get; init; }
        public required ApplicationUser Ana { get; init; }
        public required string Chave { get; init; }
        public DiscordGruposService Grupos() => new(Db, Servidor, Options.Create(Configurado()), NullLogger<DiscordGruposService>.Instance);
        public DiscordChatService Chat() => new(Db, Servidor, new MemoryCache(new MemoryCacheOptions()), Options.Create(Configurado()));
        public DiscordMembrosService Membros() => new(Db, Servidor, new MemoryCache(new MemoryCacheOptions()), Options.Create(Configurado()));
    }

    private static async Task<Cenario> MontarAsync(TestDbContextFactory factory)
    {
        var db = factory.CreateContext();
        var mg132 = await factory.CriarRegionalAsync(db, "MG132");
        var servidor = new DiscordServidorFalso();
        var ana = await factory.CriarUsuarioAsync(db, "Ana");
        ana.RegionalId = mg132.Id;
        await db.SaveChangesAsync();
        await factory.AtribuirPapelAsync(db, ana, Roles.Comercial);
        db.CrmDiscordVinculos.Add(new CrmDiscordVinculo { UsuarioId = ana.Id, DiscordUserId = "d-ana", DiscordNome = "Ana", NoServidor = true, VinculadoEm = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        servidor.Membros["d-ana"] = [];
        await new DiscordGruposService(db, servidor, Options.Create(Configurado()), NullLogger<DiscordGruposService>.Instance).SincronizarAsync(CancellationToken.None);
        return new Cenario { Db = db, Servidor = servidor, Mg132 = mg132, Ana = ana, Chave = $"regional:{mg132.Id}" };
    }

    // ---------- apagar o canal de um grupo ----------

    [Fact]
    public async Task ApagarCanalDeGrupo_ApagaTextoEVozNoDiscord_MarcaDesligado_EMantemOGrupoEOCargo()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var antes = await c.Db.CrmDiscordCanais.AsNoTracking().SingleAsync(x => x.Chave == c.Chave);
        c.Db.CrmDiscordReacoes.Add(new CrmDiscordReacao { MensagemId = "1", LeituraId = antes.DiscordCanalId, UsuarioId = c.Ana.Id, Emoji = "👍" });
        await c.Db.SaveChangesAsync();

        await c.Grupos().ApagarCanalAsync(c.Chave, "mg132", CancellationToken.None);

        Assert.Contains(antes.DiscordCanalId, c.Servidor.CanaisApagados);
        Assert.Contains(antes.DiscordVozId!, c.Servidor.CanaisApagados);
        var depois = await c.Db.CrmDiscordCanais.AsNoTracking().SingleAsync(x => x.Chave == c.Chave);
        Assert.True(depois.Desligado);
        Assert.True(depois.Ativo); // o grupo segue existindo: a pessoa não perde o cargo
        Assert.Equal(antes.DiscordCargoId, depois.DiscordCargoId);
        Assert.Equal("", depois.DiscordCanalId);
        Assert.Null(depois.DiscordVozId);
        Assert.False(await c.Db.CrmDiscordReacoes.AnyAsync(r => r.LeituraId == antes.DiscordCanalId));
        Assert.DoesNotContain(await c.Chat().ListarCanaisAsync(c.Ana.Id, CancellationToken.None), x => x.Chave == c.Chave);
        Assert.Contains(await c.Grupos().ListarCanaisAsync(CancellationToken.None), x => x.Chave == c.Chave && x.Desligado);
    }

    [Fact]
    public async Task ApagarCanalDeGrupo_ExigeONome_ENaoApagaDuasVezes()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);

        var errado = await Assert.ThrowsAsync<CrmBusinessException>(() => c.Grupos().ApagarCanalAsync(c.Chave, "outro", CancellationToken.None));
        Assert.Equal("canal_confirmacao", errado.Codigo);
        Assert.Empty(c.Servidor.CanaisApagados);

        await c.Grupos().ApagarCanalAsync(c.Chave, "MG132", CancellationToken.None);
        var outraVez = await Assert.ThrowsAsync<CrmBusinessException>(() => c.Grupos().ApagarCanalAsync(c.Chave, "MG132", CancellationToken.None));
        Assert.Equal("canal_ja_apagado", outraVez.Codigo);
    }

    [Fact]
    public async Task ApagarCanalDeGrupo_ComFalhaDoDiscord_NaoMarcaNada()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        c.Servidor.SemPermissao = true;

        var erro = await Assert.ThrowsAsync<CrmBusinessException>(() => c.Grupos().ApagarCanalAsync(c.Chave, "MG132", CancellationToken.None));

        Assert.Equal("discord_indisponivel", erro.Codigo);
        Assert.False((await c.Db.CrmDiscordCanais.AsNoTracking().SingleAsync(x => x.Chave == c.Chave)).Desligado);
    }

    [Fact]
    public async Task Sincronizar_NaoRecriaOCanalApagado_AteReligar_Depois_RecriaComOMesmoCargo()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var cargo = (await c.Db.CrmDiscordCanais.AsNoTracking().SingleAsync(x => x.Chave == c.Chave)).DiscordCargoId;
        await c.Grupos().ApagarCanalAsync(c.Chave, "MG132", CancellationToken.None);
        var textosAntes = c.Servidor.TextosCriados.Count;
        var vozesAntes = c.Servidor.VozesCriadas.Count;

        await c.Grupos().SincronizarAsync(CancellationToken.None);
        Assert.Equal(textosAntes, c.Servidor.TextosCriados.Count); // nada recriado
        Assert.Equal(vozesAntes, c.Servidor.VozesCriadas.Count);
        Assert.True((await c.Db.CrmDiscordCanais.AsNoTracking().SingleAsync(x => x.Chave == c.Chave)).Desligado);

        await c.Grupos().ReligarCanalAsync(c.Chave, CancellationToken.None);
        await c.Grupos().SincronizarAsync(CancellationToken.None);

        var religado = await c.Db.CrmDiscordCanais.AsNoTracking().SingleAsync(x => x.Chave == c.Chave);
        Assert.False(religado.Desligado);
        Assert.Equal(cargo, religado.DiscordCargoId); // o cargo nunca saiu
        Assert.False(string.IsNullOrEmpty(religado.DiscordCanalId));
        Assert.False(string.IsNullOrEmpty(religado.DiscordVozId));
        Assert.Equal(textosAntes + 1, c.Servidor.TextosCriados.Count);
        Assert.Contains(await c.Chat().ListarCanaisAsync(c.Ana.Id, CancellationToken.None), x => x.Chave == c.Chave);
    }

    [Fact]
    public async Task Religar_GrupoQueNaoEstaDesligado_Falha()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);

        var erro = await Assert.ThrowsAsync<CrmBusinessException>(() => c.Grupos().ReligarCanalAsync(c.Chave, CancellationToken.None));

        Assert.Equal("canal_nao_desligado", erro.Codigo);
    }

    [Fact]
    public async Task GrupoSemCanal_NaoRecebeAvisosAutomaticosDaRegional()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        await c.Grupos().ApagarCanalAsync(c.Chave, "MG132", CancellationToken.None);
        var ganho = await factory.CriarEtapaAsync(c.Db, "Ganho", 9, TipoEtapaPipeline.Ganho);
        var lead = new CrmLead { NomeOuRazaoSocial = "Cliente", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = c.Ana.Id };
        c.Db.CrmLeads.Add(lead);
        await c.Db.SaveChangesAsync();
        var venda = new CrmOpportunity { LeadId = lead.Id, Titulo = "V", ResponsavelId = c.Ana.Id, EtapaId = ganho.Id, PagamentoAdesao = 100m, DataEfetivaFechamento = DateTimeOffset.UtcNow };
        c.Db.CrmOpportunities.Add(venda);
        await c.Db.SaveChangesAsync();
        var avisos = new DiscordAvisosNosCanaisService(c.Db, c.Servidor, Options.Create(Configurado()), NullLogger<DiscordAvisosNosCanaisService>.Instance);
        await avisos.DefinirConfiguracaoAsync(new CssVision.Web.Api.Contracts.Crm.DiscordAvisosCanaisDto(true, false, false), CancellationToken.None);
        c.Servidor.Cartoes.Clear();

        await avisos.PublicarVendaAsync(venda.Id, CancellationToken.None);

        Assert.Empty(c.Servidor.Cartoes);
    }

    // ---------- membros ----------

    [Fact]
    public async Task Membros_CruzaComOsVinculos_MostraCargosEQuemEstaSemContaOuForaDoServidor()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var cargoGrupo = (await c.Db.CrmDiscordCanais.AsNoTracking().SingleAsync(x => x.Chave == c.Chave)).DiscordCargoId;
        c.Servidor.CargosComNome.AddRange([new DiscordCargoDoServidor(cargoGrupo, "CRM · MG132"), new DiscordCargoDoServidor("9", "Convidado")]);
        var entrou = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        c.Servidor.MembrosDoServidor.AddRange(
        [
            new DiscordMembroDoServidor("d-ana", "ana.discord", "Ana G.", "Ana Lima", null, false, entrou, [cargoGrupo, "9"]),
            new DiscordMembroDoServidor("d-estranho", "estranho", null, null, null, false, null, []),
            new DiscordMembroDoServidor("bot-1", "outrobot", null, null, null, true, null, []),
        ]);
        // Beto vinculou, mas não está mais no servidor.
        var beto = await factory.CriarUsuarioAsync(c.Db, "Beto");
        c.Db.CrmDiscordVinculos.Add(new CrmDiscordVinculo { UsuarioId = beto.Id, DiscordUserId = "d-beto", DiscordNome = "Beto", NoServidor = false, VinculadoEm = DateTimeOffset.UtcNow });
        await c.Db.SaveChangesAsync();

        var r = await c.Membros().ListarAsync(CancellationToken.None);

        Assert.Equal((3, 1, 1, 1), (r.Total, r.Vinculados, r.SemVinculo, r.Bots));
        var ana = r.Itens.Single(m => m.Id == "d-ana");
        Assert.Equal("Ana Lima", ana.Nome); // o apelido no servidor tem prioridade
        Assert.Equal(["Convidado", "CRM · MG132"], ana.Cargos); // em ordem alfabética
        Assert.Equal(("Ana", "MG132"), (ana.Vinculo!.Nome.Split(' ')[0], ana.Vinculo.Regional));
        Assert.Null(r.Itens.Single(m => m.Id == "d-estranho").Vinculo);
        Assert.Equal(["Beto"], r.ForaDoServidor.Select(f => f.Nome.Split(' ')[0]));
        Assert.True(r.Itens.Single(m => m.Id == "bot-1").Bot);
        Assert.Equal("bot-1", r.Itens[^1].Id); // bots por último
    }

    [Fact]
    public async Task Membros_GuardaPor30Segundos_ESemDiscordConfigurado_OuComFalha_ExplicaOMotivo()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var servico = c.Membros();
        var antes = c.Servidor.LeiturasDeMembros;

        await servico.ListarAsync(CancellationToken.None);
        await servico.ListarAsync(CancellationToken.None);
        Assert.Equal(antes + 1, c.Servidor.LeiturasDeMembros); // a segunda veio do cache

        var semConfig = new DiscordMembrosService(c.Db, c.Servidor, new MemoryCache(new MemoryCacheOptions()), Options.Create(new DiscordOptions()));
        Assert.Equal("discord_nao_configurado", (await Assert.ThrowsAsync<CrmBusinessException>(() => semConfig.ListarAsync(CancellationToken.None))).Codigo);

        c.Servidor.SemPermissao = true;
        Assert.Equal("discord_indisponivel", (await Assert.ThrowsAsync<CrmBusinessException>(() => c.Membros().ListarAsync(CancellationToken.None))).Codigo);
    }
}
