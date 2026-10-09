using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Authorization;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Domain.Identity;
using CssVision.Web.Services.Discord;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Administrador cria canais extras e renomeia canais do Discord pelo CRM.</summary>
public class DiscordGestaoDeCanaisTests
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
        public required CrmRegional Mg134 { get; init; }
        public required ApplicationUser Ana { get; init; }
        public required ApplicationUser Beto { get; init; }
        public DiscordGruposService Grupos() => new(Db, Servidor, Options.Create(Configurado()), NullLogger<DiscordGruposService>.Instance);
        public DiscordChatService Chat() => new(Db, Servidor, new MemoryCache(new MemoryCacheOptions()), Options.Create(Configurado()));
    }

    private static async Task<Cenario> MontarAsync(TestDbContextFactory factory)
    {
        var db = factory.CreateContext();
        var mg132 = await factory.CriarRegionalAsync(db, "MG132");
        var mg134 = await factory.CriarRegionalAsync(db, "MG134");
        async Task<ApplicationUser> Pessoa(string nome, CrmRegional regional)
        {
            var u = await factory.CriarUsuarioAsync(db, nome);
            u.RegionalId = regional.Id;
            await db.SaveChangesAsync();
            await factory.AtribuirPapelAsync(db, u, Roles.Comercial);
            return u;
        }

        var c = new Cenario { Db = db, Servidor = new DiscordServidorFalso(), Mg132 = mg132, Mg134 = mg134, Ana = await Pessoa("Ana", mg132), Beto = await Pessoa("Beto", mg134) };
        await c.Grupos().SincronizarAsync(CancellationToken.None);
        return c;
    }

    [Fact]
    public async Task Criar_FazOCanalNoDiscordComOCargoDoGrupo_ESoQuemEDoGrupoOVeNoChat()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var chaveDaRegional = $"regional:{c.Mg132.Id}";
        var cargoDaRegional = (await c.Db.CrmDiscordCanais.AsNoTracking().SingleAsync(x => x.Chave == chaveDaRegional)).DiscordCargoId;

        var criado = await c.Grupos().CriarCanalAsync("  Metas   de Outubro ", chaveDaRegional, null, CancellationToken.None);

        var texto = c.Servidor.TextosCriados.Last();
        Assert.Equal("metas-de-outubro", texto.Nome);
        Assert.Equal(cargoDaRegional, texto.CargoId);
        Assert.True(criado.Extra);
        Assert.StartsWith("extra:", criado.Chave);
        Assert.Equal("Metas de Outubro", criado.Nome);
        Assert.Equal(chaveDaRegional, criado.AcessoChave);

        var doAna = await c.Chat().ListarCanaisAsync(c.Ana.Id, CancellationToken.None);
        var doBeto = await c.Chat().ListarCanaisAsync(c.Beto.Id, CancellationToken.None);
        Assert.Contains(doAna, x => x.Chave == criado.Chave && x.Nome == "Metas de Outubro");
        Assert.DoesNotContain(doBeto, x => x.Chave == criado.Chave);
    }

    [Fact]
    public async Task CanalExtra_NaoEDesativadoPelaSincronizacao_NemPerdeOQueFoiEscolhido()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var criado = await c.Grupos().CriarCanalAsync("Avisos", "geral", null, CancellationToken.None);

        await c.Grupos().SincronizarAsync(CancellationToken.None);

        var salvo = await c.Db.CrmDiscordCanais.AsNoTracking().SingleAsync(x => x.Chave == criado.Chave);
        Assert.True(salvo.Ativo);
        Assert.Equal("geral", salvo.AcessoChave);
        Assert.Contains(await c.Grupos().ListarCanaisAsync(CancellationToken.None), x => x.Chave == criado.Chave && x.Extra);
    }

    [Theory]
    [InlineData("", "canal_sem_nome")]
    [InlineData("   ", "canal_sem_nome")]
    public async Task Criar_SemNome_Falha(string nome, string codigo)
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var antes = c.Servidor.TextosCriados.Count;

        var erro = await Assert.ThrowsAsync<CrmBusinessException>(() => c.Grupos().CriarCanalAsync(nome, "geral", null, CancellationToken.None));

        Assert.Equal(codigo, erro.Codigo);
        Assert.Equal(antes, c.Servidor.TextosCriados.Count);
    }

    [Fact]
    public async Task Criar_ComNomeLongo_GrupoInexistente_OuNomeRepetido_Falha()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);

        var longo = await Assert.ThrowsAsync<CrmBusinessException>(() => c.Grupos().CriarCanalAsync(new string('a', 61), "geral", null, CancellationToken.None));
        var semGrupo = await Assert.ThrowsAsync<CrmBusinessException>(() => c.Grupos().CriarCanalAsync("Novo", "grupo:inexistente", null, CancellationToken.None));
        var daConversa = await Assert.ThrowsAsync<CrmBusinessException>(() => c.Grupos().CriarCanalAsync("Novo", "conversas", null, CancellationToken.None));
        await c.Grupos().CriarCanalAsync("Avisos", "geral", null, CancellationToken.None);
        var repetido = await Assert.ThrowsAsync<CrmBusinessException>(() => c.Grupos().CriarCanalAsync("avisos", "geral", null, CancellationToken.None));

        Assert.Equal("canal_nome_longo", longo.Codigo);
        Assert.Equal("canal_acesso_invalido", semGrupo.Codigo);
        Assert.Equal("canal_acesso_invalido", daConversa.Codigo);
        Assert.Equal("canal_nome_repetido", repetido.Codigo);
    }

    [Fact]
    public async Task Criar_ComFalhaDoDiscord_NaoGuardaNada()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var antes = await c.Db.CrmDiscordCanais.CountAsync();
        c.Servidor.SemPermissao = true;

        var erro = await Assert.ThrowsAsync<CrmBusinessException>(() => c.Grupos().CriarCanalAsync("Avisos", "geral", null, CancellationToken.None));

        Assert.Equal("discord_indisponivel", erro.Codigo);
        Assert.Equal(antes, await c.Db.CrmDiscordCanais.CountAsync());
    }

    [Fact]
    public async Task Renomear_TrocaONomeNoDiscordENoCrm_ESobreviveANovaSincronizacao()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var chave = $"regional:{c.Mg132.Id}";
        var canal = await c.Db.CrmDiscordCanais.AsNoTracking().SingleAsync(x => x.Chave == chave);

        var r = await c.Grupos().RenomearCanalAsync(chave, "Minas Gerais 132", CancellationToken.None);

        Assert.Equal("Minas Gerais 132", r.Nome);
        Assert.Contains(c.Servidor.Renomeados, x => x.CanalId == canal.DiscordCanalId && x.Nome == "minas-gerais-132");
        Assert.Contains(c.Servidor.Renomeados, x => x.CanalId == canal.DiscordVozId && x.Nome == "Voz · Minas Gerais 132");

        await c.Grupos().SincronizarAsync(CancellationToken.None); // a sincronização reescreve o nome padrão, não o escolhido
        Assert.Contains(await c.Chat().ListarCanaisAsync(c.Ana.Id, CancellationToken.None), x => x.Chave == chave && x.Nome == "Minas Gerais 132");
    }

    [Fact]
    public async Task Renomear_CanalExtra_AtualizaONomeDele()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var criado = await c.Grupos().CriarCanalAsync("Avisos", "geral", null, CancellationToken.None);

        var r = await c.Grupos().RenomearCanalAsync(criado.Chave, "Comunicados", CancellationToken.None);

        Assert.Equal("Comunicados", r.Nome);
        Assert.Contains(c.Servidor.Renomeados, x => x.Nome == "comunicados");
    }

    [Fact]
    public async Task Renomear_ComFalhaDoDiscord_OuNomeRepetido_OuCanalInexistente_NaoMudaNada()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var chave = $"regional:{c.Mg132.Id}";
        var original = (await c.Db.CrmDiscordCanais.AsNoTracking().SingleAsync(x => x.Chave == chave)).Nome;

        var repetido = await Assert.ThrowsAsync<CrmBusinessException>(() => c.Grupos().RenomearCanalAsync(chave, "Geral", CancellationToken.None));
        var inexistente = await Assert.ThrowsAsync<CrmBusinessException>(() => c.Grupos().RenomearCanalAsync("extra:nao-existe", "Qualquer", CancellationToken.None));
        c.Servidor.SemPermissao = true;
        var falha = await Assert.ThrowsAsync<CrmBusinessException>(() => c.Grupos().RenomearCanalAsync(chave, "Outro nome", CancellationToken.None));

        Assert.Equal("canal_nome_repetido", repetido.Codigo);
        Assert.Equal("canal_nao_encontrado", inexistente.Codigo);
        Assert.Equal("discord_indisponivel", falha.Codigo);
        var depois = await c.Db.CrmDiscordCanais.AsNoTracking().SingleAsync(x => x.Chave == chave);
        Assert.Null(depois.NomePersonalizado);
        Assert.Equal(original, depois.Nome);
    }

    [Fact]
    public async Task Renomear_OCanalDeConversasDiretas_NaoEPermitido()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);

        var erro = await Assert.ThrowsAsync<CrmBusinessException>(() => c.Grupos().RenomearCanalAsync("conversas", "Outro", CancellationToken.None));

        Assert.Equal("canal_nao_encontrado", erro.Codigo);
    }
}
