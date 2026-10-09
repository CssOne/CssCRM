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

/// <summary>Reações, fixadas, busca, tópicos dentro do CRM e aviso de menção no chat.</summary>
public class DiscordChatExtrasTests
{
    private static DiscordOptions Configurado() => new()
    {
        BotToken = "bot", ClientId = "app-1", ClientSecret = "segredo", GuildId = "servidor-1", UrlPublica = "https://crm.exemplo.com/",
    };

    private sealed class AvisoGravador : IAvisoDeMencao
    {
        public List<(Guid Usuario, string De)> Avisos { get; } = [];

        public Task AvisarAsync(Guid usuarioId, string nomeDeQuemMarcou, CancellationToken ct)
        {
            Avisos.Add((usuarioId, nomeDeQuemMarcou));
            return Task.CompletedTask;
        }
    }

    private sealed class Cenario
    {
        public required ApplicationDbContext Db { get; init; }
        public required DiscordServidorFalso Servidor { get; init; }
        public required ApplicationUser Ana { get; init; }
        public required ApplicationUser Beto { get; init; }
        public required ApplicationUser Gestor { get; init; }
        public required string CanalGeral { get; init; }
        public required string CanalMg134 { get; init; }
        public AvisoGravador Aviso { get; } = new();
        public DiscordChatService Servico() => new(Db, Servidor, new MemoryCache(new MemoryCacheOptions()), Options.Create(Configurado()), avisoDeMencao: Aviso);
    }

    private static async Task<Cenario> MontarAsync(TestDbContextFactory factory)
    {
        var db = factory.CreateContext();
        var mg132 = await factory.CriarRegionalAsync(db, "MG132");
        var mg134 = await factory.CriarRegionalAsync(db, "MG134");
        var servidor = new DiscordServidorFalso();
        await new DiscordGruposService(db, servidor, Options.Create(Configurado()), NullLogger<DiscordGruposService>.Instance).SincronizarAsync(CancellationToken.None);

        async Task<ApplicationUser> Pessoa(string nome, CrmRegional regional, string papel)
        {
            var u = await factory.CriarUsuarioAsync(db, nome);
            u.RegionalId = regional.Id;
            await db.SaveChangesAsync();
            await factory.AtribuirPapelAsync(db, u, papel);
            db.CrmDiscordVinculos.Add(new CrmDiscordVinculo { UsuarioId = u.Id, DiscordUserId = $"d-{nome.ToLowerInvariant()}", DiscordNome = nome, NoServidor = true, VinculadoEm = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
            return u;
        }

        var ana = await Pessoa("Ana", mg132, Roles.Comercial);
        var beto = await Pessoa("Beto", mg132, Roles.Comercial);
        var gestor = await Pessoa("Gerente", mg132, Roles.GestorMaster);
        var geral = await db.CrmDiscordCanais.AsNoTracking().SingleAsync(c => c.Chave == "geral");
        var outra = await db.CrmDiscordCanais.AsNoTracking().SingleAsync(c => c.Chave == $"regional:{mg134.Id}");
        return new Cenario { Db = db, Servidor = servidor, Ana = ana, Beto = beto, Gestor = gestor, CanalGeral = geral.DiscordCanalId, CanalMg134 = outra.DiscordCanalId };
    }

    private static void Mensagem(Cenario c, string canal, string id, string texto, string autor = "Fulano") =>
        c.Servidor.Mensagens[canal] = [.. c.Servidor.Mensagens.GetValueOrDefault(canal) ?? [], new DiscordMensagem(id, autor, null, texto, DateTimeOffset.UtcNow.AddMinutes(int.Parse(id[^3..]) - 500), [], false)];

    // ---------- reações ----------

    [Fact]
    public async Task Reagir_PelaPrimeiraVez_ReageNoDiscordComOBot_E_ContaUma()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        Mensagem(c, c.CanalGeral, "100001", "Bom dia");

        var reacoes = await c.Servico().AlternarReacaoAsync(c.Ana.Id, "geral", "100001", "👍", CancellationToken.None);

        Assert.Contains((c.CanalGeral, "100001", "👍"), c.Servidor.ReacoesDoBot);
        var r = Assert.Single(reacoes);
        Assert.Equal(("👍", 1, true), (r.Texto, r.Contagem, r.Reagi));
    }

    [Fact]
    public async Task Reagir_DuasPessoasNoMesmoEmoji_ContaDuas_ComUmaSoReacaoDoBot_ESoSaiQuandoAUltimaTira()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        Mensagem(c, c.CanalGeral, "100001", "Bom dia");
        var servico = c.Servico();

        await servico.AlternarReacaoAsync(c.Ana.Id, "geral", "100001", "🎉", CancellationToken.None);
        var comDuas = await servico.AlternarReacaoAsync(c.Beto.Id, "geral", "100001", "🎉", CancellationToken.None);
        Assert.Equal(2, Assert.Single(comDuas).Contagem);
        Assert.Single(c.Servidor.ReacoesDoBot);

        var aposAna = await servico.AlternarReacaoAsync(c.Ana.Id, "geral", "100001", "🎉", CancellationToken.None);
        Assert.Equal(1, Assert.Single(aposAna).Contagem);
        Assert.Single(c.Servidor.ReacoesDoBot); // o Beto ainda reagiu: o bot continua

        var aposBeto = await servico.AlternarReacaoAsync(c.Beto.Id, "geral", "100001", "🎉", CancellationToken.None);
        Assert.Empty(aposBeto);
        Assert.Empty(c.Servidor.ReacoesDoBot);
    }

    [Fact]
    public async Task Reagir_SomaAsReacoesDePessoasNoDiscord_SemContarOBot()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        Mensagem(c, c.CanalGeral, "100001", "Bom dia");
        c.Servidor.ReacoesDePessoas["100001"] = new() { ["🎉"] = 3 }; // 3 pessoas reagiram direto no Discord

        var r = Assert.Single(await c.Servico().AlternarReacaoAsync(c.Ana.Id, "geral", "100001", "🎉", CancellationToken.None));

        Assert.Equal(4, r.Contagem); // 3 do Discord + a Ana; a reação do bot não entra na conta
        Assert.True(r.Reagi);
    }

    [Fact]
    public async Task ListarMensagens_TrazAsReacoes_ComSeuReagiSoParaQuemReagiu()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        Mensagem(c, c.CanalGeral, "100001", "Bom dia");
        await c.Servico().AlternarReacaoAsync(c.Ana.Id, "geral", "100001", "👍", CancellationToken.None);

        var paraAna = await c.Servico().ListarMensagensAsync(c.Ana.Id, "geral", null, CancellationToken.None);
        var paraBeto = await c.Servico().ListarMensagensAsync(c.Beto.Id, "geral", null, CancellationToken.None);

        Assert.True(Assert.Single(paraAna.Mensagens.Single().Reacoes!).Reagi);
        Assert.False(Assert.Single(paraBeto.Mensagens.Single().Reacoes!).Reagi);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("a/b")]
    [InlineData("👍 ")]
    [InlineData("../../x")]
    [InlineData("nome:12")]
    public async Task Reagir_ComEmojiInvalido_Falha_ENaoChegaAoDiscord(string emoji)
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        Mensagem(c, c.CanalGeral, "100001", "Bom dia");

        var erro = await Assert.ThrowsAsync<CrmBusinessException>(() => c.Servico().AlternarReacaoAsync(c.Ana.Id, "geral", "100001", emoji, CancellationToken.None));

        Assert.Equal("emoji_invalido", erro.Codigo);
        Assert.Empty(c.Servidor.ReacoesDoBot);
    }

    [Fact]
    public async Task Reagir_ComEmojiPersonalizado_Funciona_ENoDiscordNaoGravaSeOBotNaoPuder()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        Mensagem(c, c.CanalGeral, "100001", "Bom dia");

        var reacoes = await c.Servico().AlternarReacaoAsync(c.Ana.Id, "geral", "100001", "parabens:1234567", CancellationToken.None);
        Assert.Equal(":parabens:", Assert.Single(reacoes).Texto);

        c.Servidor.SemPermissao = true;
        await Assert.ThrowsAsync<CrmBusinessException>(() => c.Servico().AlternarReacaoAsync(c.Beto.Id, "geral", "100001", "🔥", CancellationToken.None));
        Assert.Equal(1, await c.Db.CrmDiscordReacoes.CountAsync()); // a do Beto não ficou gravada
    }

    [Fact]
    public async Task Reagir_EmConversaQueAPessoaNaoPodeAbrir_EhRecusado()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var mg134 = await c.Db.CrmDiscordCanais.AsNoTracking().SingleAsync(x => x.DiscordCanalId == c.CanalMg134);
        Mensagem(c, c.CanalMg134, "100001", "Só da MG134");

        await Assert.ThrowsAsync<CrmForbiddenException>(() => c.Servico().AlternarReacaoAsync(c.Ana.Id, mg134.Chave, "100001", "👍", CancellationToken.None));
    }

    // ---------- fixadas ----------

    [Fact]
    public async Task Fixar_SoGestorEAdministrador_EAMensagemAparecePosteriormenteNasFixadas()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        Mensagem(c, c.CanalGeral, "100001", "Regra importante");
        var servico = c.Servico();

        var antes = c.Servidor.Fixadas.Count; // a sincronização já fixa a mensagem de boas-vindas no Geral

        await Assert.ThrowsAsync<CrmForbiddenException>(() => servico.FixarMensagemAsync(c.Ana.Id, "geral", "100001", true, CancellationToken.None));
        Assert.Equal(antes, c.Servidor.Fixadas.Count);

        await servico.FixarMensagemAsync(c.Gestor.Id, "geral", "100001", true, CancellationToken.None);
        var fixadas = await servico.ListarFixadasAsync(c.Ana.Id, "geral", CancellationToken.None);
        Assert.Contains(fixadas, m => m.Conteudo == "Regra importante");

        await servico.FixarMensagemAsync(c.Gestor.Id, "geral", "100001", false, CancellationToken.None);
        Assert.DoesNotContain(await servico.ListarFixadasAsync(c.Ana.Id, "geral", CancellationToken.None), m => m.Conteudo == "Regra importante");
    }

    // ---------- busca ----------

    [Fact]
    public async Task Buscar_AchaPorTextoOuAutor_SemDiferenciarAcentoNemMaiuscula_DaMaisNovaParaAMaisAntiga()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        Mensagem(c, c.CanalGeral, "100001", "Reunião de METAS amanhã", "Carlos");
        Mensagem(c, c.CanalGeral, "100002", "Almoço de sexta", "Carlos");
        Mensagem(c, c.CanalGeral, "100003", "As metas do mês subiram", "Marta");
        Mensagem(c, c.CanalGeral, "100004", "Oi", "Roberta Metas");

        var achadas = await c.Servico().BuscarMensagensAsync(c.Ana.Id, "geral", "reuniao", CancellationToken.None);
        Assert.Equal(["Reunião de METAS amanhã"], achadas.Select(m => m.Conteudo));

        var metas = await c.Servico().BuscarMensagensAsync(c.Ana.Id, "geral", "metas", CancellationToken.None);
        Assert.Equal(["100004", "100003", "100001"], metas.Select(m => m.Id)); // o autor "Roberta Metas" também conta
    }

    [Fact]
    public async Task Buscar_ComTermoCurto_Falha_EEmConversaAlheia_EhRecusado()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var mg134 = await c.Db.CrmDiscordCanais.AsNoTracking().SingleAsync(x => x.DiscordCanalId == c.CanalMg134);

        var curto = await Assert.ThrowsAsync<CrmBusinessException>(() => c.Servico().BuscarMensagensAsync(c.Ana.Id, "geral", "a", CancellationToken.None));
        Assert.Equal("busca_curta", curto.Codigo);
        await Assert.ThrowsAsync<CrmForbiddenException>(() => c.Servico().BuscarMensagensAsync(c.Ana.Id, mg134.Chave, "oi", CancellationToken.None));
    }

    // ---------- tópicos ----------

    [Fact]
    public async Task Topicos_ListaOsDoGrupo_AbreNoChat_ENaoDeixaAbrirOsDeOutroGrupo()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        c.Servidor.TopicosDoServidor.Add(new DiscordTopico("7001", "Metas de outubro", c.CanalGeral, false));
        c.Servidor.TopicosDoServidor.Add(new DiscordTopico("7002", "Só da MG134", c.CanalMg134, false));
        c.Servidor.TopicosDoServidor.Add(new DiscordTopico("7003", "Antigo", c.CanalGeral, true));
        Mensagem(c, "7001", "100001", "Vamos bater a meta");
        Mensagem(c, "7002", "100002", "Segredo da MG134");
        var servico = c.Servico();

        var lista = await servico.ListarTopicosAsync(c.Ana.Id, "geral", CancellationToken.None);
        var topico = Assert.Single(lista); // o arquivado não entra
        Assert.Equal(("topico:7001", "Metas de outubro"), (topico.Chave, topico.Nome));

        var mensagens = await servico.ListarMensagensAsync(c.Ana.Id, topico.Chave, null, CancellationToken.None);
        Assert.Equal("Vamos bater a meta", Assert.Single(mensagens.Mensagens).Conteudo);

        await Assert.ThrowsAsync<CrmForbiddenException>(() => servico.ListarMensagensAsync(c.Ana.Id, "topico:7002", null, CancellationToken.None));
        await Assert.ThrowsAsync<CrmForbiddenException>(() => servico.ListarMensagensAsync(c.Ana.Id, "topico:99999", null, CancellationToken.None));
        await Assert.ThrowsAsync<CrmBusinessException>(() => servico.ListarMensagensAsync(c.Ana.Id, "topico:abc", null, CancellationToken.None));
    }

    [Fact]
    public async Task Topicos_PodeEnviarMensagemNoTopico_PeloWebhookDoCanalPai()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        c.Servidor.TopicosDoServidor.Add(new DiscordTopico("7001", "Metas de outubro", c.CanalGeral, false));

        await c.Servico().EnviarAsync(c.Ana.Id, "topico:7001", "Bora!", CancellationToken.None);

        Assert.Equal("7001", Assert.Single(c.Servidor.Enviadas, e => e.Texto == "Bora!").CanalId);
    }

    [Fact]
    public async Task Topicos_DentroDeUmTopicoOuConversaDireta_NaoHaTopicos()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        c.Servidor.TopicosDoServidor.Add(new DiscordTopico("7001", "Metas", c.CanalGeral, false));

        Assert.Empty(await c.Servico().ListarTopicosAsync(c.Ana.Id, "topico:7001", CancellationToken.None));
    }

    // ---------- menção ----------

    [Fact]
    public async Task Mencao_AvisaQuemFoiMarcado_ENaoQuemMarcou_NemQuemNaoPodeAbrirAConversa()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var mg134 = await c.Db.CrmRegionais.FirstAsync(r => r.Nome == "MG134");
        var longe = await factory.CriarUsuarioAsync(c.Db, "Longe");
        longe.RegionalId = mg134.Id;
        await c.Db.SaveChangesAsync();
        await factory.AtribuirPapelAsync(c.Db, longe, Roles.Comercial);
        var chaveDaMg134 = (await c.Db.CrmDiscordCanais.AsNoTracking().SingleAsync(x => x.DiscordCanalId == c.CanalMg134)).Chave;

        // No grupo da MG132 (Ana, Beto e o gerente têm acesso): marca Beto, ela mesma e o "Longe" (que não vê esse grupo)... mas o geral é de todos.
        await c.Servico().EnviarAsync(c.Ana.Id, chaveDaMg134.Length > 0 ? "geral" : "geral", "Oi @Beto", CancellationToken.None, [c.Beto.Id, c.Ana.Id]);
        Assert.Equal([c.Beto.Id], c.Aviso.Avisos.Select(a => a.Usuario));
        Assert.Equal(c.Ana.NomeCompleto, c.Aviso.Avisos.Single().De);
    }

    [Fact]
    public async Task Mencao_EmGrupoQueOMarcadoNaoVe_NaoAvisa()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var regional = await c.Db.CrmRegionais.FirstAsync(r => r.Nome == "MG134");
        var longe = await factory.CriarUsuarioAsync(c.Db, "Longe");
        longe.RegionalId = regional.Id;
        await c.Db.SaveChangesAsync();
        await factory.AtribuirPapelAsync(c.Db, longe, Roles.Comercial);
        var gestorMg132Chave = $"regional:{(await c.Db.CrmRegionais.FirstAsync(r => r.Nome == "MG132")).Id}";

        // O gerente (visão total) escreve na regional MG132; "Longe" é da MG134 e não abre esse grupo.
        await c.Servico().EnviarAsync(c.Gestor.Id, gestorMg132Chave, "Oi @Longe e @Ana", CancellationToken.None, [longe.Id, c.Ana.Id]);

        Assert.Equal([c.Ana.Id], c.Aviso.Avisos.Select(a => a.Usuario));
    }
}
