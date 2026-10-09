using System.Text;
using System.Text.Json;
using CssVision.Web.Api.Controllers;
using CssVision.Web.Authorization;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Domain.Identity;
using CssVision.Web.Services.Discord;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Comandos de barra do Discord (/vendas, /meta): assinatura, endereço de interações e respostas.</summary>
public class DiscordComandosTests
{
    private sealed class RelogioFalso(DateTimeOffset agora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => agora;
    }

    // 7/10/2026 às 15:00 em Brasília.
    private static readonly DateTimeOffset Agora = new(2026, 10, 7, 18, 0, 0, TimeSpan.Zero);

    private static (string ChavePublicaHex, Func<string, string, string> Assinar) NovoParDeChaves()
    {
        // Chave privada de 32 bytes aleatórios (sem o gerador/SecureRandom: o projeto carrega duas edições do BouncyCastle e o nome é ambíguo).
        var privada = new Ed25519PrivateKeyParameters(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32), 0);
        var publica = privada.GeneratePublicKey().GetEncoded();
        string Assinar(string timestamp, string corpo)
        {
            var assinador = new Ed25519Signer();
            assinador.Init(true, privada);
            var mensagem = Encoding.UTF8.GetBytes(timestamp + corpo);
            assinador.BlockUpdate(mensagem, 0, mensagem.Length);
            return Convert.ToHexString(assinador.GenerateSignature()).ToLowerInvariant();
        }

        return (Convert.ToHexString(publica).ToLowerInvariant(), Assinar);
    }

    // ---------- assinatura ----------

    [Fact]
    public void Assinatura_Valida_AceitaSoSeCorpoTimestampEChaveBaterem()
    {
        var (chave, assinar) = NovoParDeChaves();
        var ts = Agora.ToUnixTimeSeconds().ToString();
        var corpo = """{"type":1}""";
        var assinatura = assinar(ts, corpo);

        Assert.True(DiscordAssinatura.EhValida(chave, ts, corpo, assinatura, Agora));
        Assert.False(DiscordAssinatura.EhValida(chave, ts, corpo + " ", assinatura, Agora)); // corpo adulterado
        Assert.False(DiscordAssinatura.EhValida(chave, (Agora.ToUnixTimeSeconds() + 1).ToString(), corpo, assinatura, Agora)); // timestamp trocado
        Assert.False(DiscordAssinatura.EhValida(NovoParDeChaves().ChavePublicaHex, ts, corpo, assinatura, Agora)); // outra chave
    }

    [Theory]
    [InlineData(null, "ab")]
    [InlineData("123", null)]
    [InlineData("não-é-número", "ab")]
    [InlineData("123", "xyz")] // não é hexadecimal
    [InlineData("123", "abcd")] // tamanho errado
    public void Assinatura_ComCabecalhosInvalidos_Recusa_SemLancarErro(string? timestamp, string? assinatura)
    {
        var (chave, _) = NovoParDeChaves();
        Assert.False(DiscordAssinatura.EhValida(chave, timestamp, "{}", assinatura, Agora));
    }

    [Fact]
    public void Assinatura_Velha_EhRecusada_MesmoSendoDeVerdade()
    {
        var (chave, assinar) = NovoParDeChaves();
        var ts = Agora.AddMinutes(-10).ToUnixTimeSeconds().ToString();
        Assert.False(DiscordAssinatura.EhValida(chave, ts, "{}", assinar(ts, "{}"), Agora));
    }

    // ---------- endereço de interações ----------

    private static DiscordInteracoesController Controlador(DiscordOptions opcoes, IDiscordComandosService comandos, string corpo, string? ts, string? assinatura)
    {
        var contexto = new DefaultHttpContext();
        contexto.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(corpo));
        if (ts is not null) contexto.Request.Headers["X-Signature-Timestamp"] = ts;
        if (assinatura is not null) contexto.Request.Headers["X-Signature-Ed25519"] = assinatura;
        return new DiscordInteracoesController(Options.Create(opcoes), comandos, NullLogger<DiscordInteracoesController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = contexto },
        };
    }

    private static DiscordOptions Opcoes(string chave) => new()
    {
        BotToken = "bot", ClientId = "app-1", ClientSecret = "segredo", GuildId = "servidor-1", UrlPublica = "https://crm.exemplo.com/", PublicKey = chave,
    };

    private sealed class ComandosGravador : IDiscordComandosService
    {
        public int Chamadas { get; private set; }

        public Task<object> ExecutarAsync(JsonElement interacao, CancellationToken ct)
        {
            Chamadas++;
            return Task.FromResult<object>(new { type = 4, data = new { content = "ok" } });
        }
    }

    [Fact]
    public async Task Endereco_ResponderPingComAssinaturaValida_ERecusarSemAssinatura()
    {
        var (chave, assinar) = NovoParDeChaves();
        var comandos = new ComandosGravador();
        var corpo = """{"type":1}""";
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

        var ok = await Controlador(Opcoes(chave), comandos, corpo, ts, assinar(ts, corpo)).Receber(CancellationToken.None);
        var recusado = await Controlador(Opcoes(chave), comandos, corpo, ts, null).Receber(CancellationToken.None);
        var falsa = await Controlador(Opcoes(chave), comandos, corpo, ts, new string('0', 128)).Receber(CancellationToken.None);

        Assert.Equal(1, JsonSerializer.SerializeToDocument(Assert.IsType<OkObjectResult>(ok).Value).RootElement.GetProperty("type").GetInt32());
        Assert.IsType<UnauthorizedResult>(recusado);
        Assert.IsType<UnauthorizedResult>(falsa);
        Assert.Equal(0, comandos.Chamadas);
    }

    [Fact]
    public async Task Endereco_ComandoAssinadoVaiParaOServico_ESemChavePublicaOEnderecoNaoExiste()
    {
        var (chave, assinar) = NovoParDeChaves();
        var comandos = new ComandosGravador();
        var corpo = """{"type":2,"data":{"name":"meta"},"member":{"user":{"id":"d-ana"}}}""";
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

        var resposta = await Controlador(Opcoes(chave), comandos, corpo, ts, assinar(ts, corpo)).Receber(CancellationToken.None);
        var semChave = await Controlador(Opcoes(""), comandos, corpo, ts, assinar(ts, corpo)).Receber(CancellationToken.None);

        Assert.IsType<OkObjectResult>(resposta);
        Assert.Equal(1, comandos.Chamadas);
        Assert.IsType<NotFoundResult>(semChave);
    }

    // ---------- comandos ----------

    private sealed class Cenario
    {
        public required ApplicationDbContext Db { get; init; }
        public required TestDbContextFactory Factory { get; init; }
        public required CrmRegional Mg132 { get; init; }
        public required CrmRegional Mg134 { get; init; }
        public required CrmPipelineStage Ganho { get; init; }
        public DiscordComandosService Servico() => new(Db, new RelogioFalso(Agora));
    }

    private static async Task<Cenario> MontarAsync(TestDbContextFactory factory)
    {
        var db = factory.CreateContext();
        var mg132 = await factory.CriarRegionalAsync(db, "MG132");
        var mg134 = await factory.CriarRegionalAsync(db, "MG134");
        var ganho = await factory.CriarEtapaAsync(db, "Ganho", 9, TipoEtapaPipeline.Ganho);
        return new Cenario { Db = db, Factory = factory, Mg132 = mg132, Mg134 = mg134, Ganho = ganho };
    }

    private static async Task<ApplicationUser> PessoaAsync(Cenario c, string nome, CrmRegional regional, bool vinculada = true, bool ativa = true)
    {
        var u = await c.Factory.CriarUsuarioAsync(c.Db, nome);
        u.RegionalId = regional.Id;
        u.Ativo = ativa;
        await c.Db.SaveChangesAsync();
        await c.Factory.AtribuirPapelAsync(c.Db, u, Roles.Comercial);
        if (vinculada)
        {
            c.Db.CrmDiscordVinculos.Add(new CrmDiscordVinculo { UsuarioId = u.Id, DiscordUserId = $"d-{nome.ToLowerInvariant()}", DiscordNome = nome, NoServidor = true, VinculadoEm = DateTimeOffset.UtcNow });
            await c.Db.SaveChangesAsync();
        }

        return u;
    }

    private static async Task VendaAsync(Cenario c, ApplicationUser consultor, decimal adesao, DateTimeOffset fechamento)
    {
        var lead = new CrmLead { NomeOuRazaoSocial = "Cliente Secreto", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = consultor.Id };
        c.Db.CrmLeads.Add(lead);
        await c.Db.SaveChangesAsync();
        c.Db.CrmOpportunities.Add(new CrmOpportunity { LeadId = lead.Id, Titulo = "V", ResponsavelId = consultor.Id, EtapaId = c.Ganho.Id, PagamentoAdesao = adesao, DataEfetivaFechamento = fechamento });
        await c.Db.SaveChangesAsync();
    }

    private static JsonElement Comando(string nome, string quem, string? periodo = null)
    {
        object data = periodo is null
            ? new { name = nome }
            : new { name = nome, options = new[] { new { name = "periodo", type = 3, value = periodo } } };
        return JsonSerializer.SerializeToElement(new { type = 2, data, member = new { user = new { id = quem } } });
    }

    private static string Texto(object resposta)
    {
        var json = JsonSerializer.SerializeToElement(resposta);
        Assert.Equal(4, json.GetProperty("type").GetInt32());
        Assert.Equal(64, json.GetProperty("data").GetProperty("flags").GetInt32()); // só quem pediu vê
        return json.GetProperty("data").GetProperty("content").GetString()!;
    }

    [Fact]
    public async Task Vendas_MostraAsDaPessoaEAsDaRegional_DeHoje_SemDadosDeCliente()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var ana = await PessoaAsync(c, "Ana", c.Mg132);
        var beto = await PessoaAsync(c, "Beto", c.Mg132);
        var outra = await PessoaAsync(c, "Carla", c.Mg134);
        await VendaAsync(c, ana, 100m, Agora);
        await VendaAsync(c, ana, 200m, Agora);
        await VendaAsync(c, beto, 50m, Agora);
        await VendaAsync(c, outra, 999m, Agora);
        await VendaAsync(c, ana, 777m, Agora.AddDays(-1)); // ontem

        var texto = Texto(await c.Servico().ExecutarAsync(Comando("vendas", "d-ana"), CancellationToken.None));

        Assert.Contains("2 vendas", texto);
        Assert.Contains("R$", texto);
        Assert.Contains("Regional MG132 hoje:** 3 vendas", texto);
        Assert.DoesNotContain("999", texto);
        Assert.DoesNotContain("Secreto", texto);
    }

    [Fact]
    public async Task Vendas_DoMes_SomaOMesTodo()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var ana = await PessoaAsync(c, "Ana", c.Mg132);
        await VendaAsync(c, ana, 100m, Agora);
        await VendaAsync(c, ana, 100m, Agora.AddDays(-3));
        await VendaAsync(c, ana, 100m, Agora.AddMonths(-1)); // mês passado

        var texto = Texto(await c.Servico().ExecutarAsync(Comando("vendas", "d-ana", "mes"), CancellationToken.None));

        Assert.Contains("Suas vendas no mês:** 2 vendas", texto);
    }

    [Fact]
    public async Task Meta_MostraOAndamentoDaRegionalDaPessoa()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var ana = await PessoaAsync(c, "Ana", c.Mg132);
        await VendaAsync(c, ana, 250m, Agora);
        c.Db.CrmRegionalGoals.Add(new CrmRegionalGoal { RegionalId = c.Mg132.Id, MesReferencia = new DateOnly(2026, 10, 1), MetaQuantidadeVendas = 4, MetaValor = 1000m });
        await c.Db.SaveChangesAsync();

        var texto = Texto(await c.Servico().ExecutarAsync(Comando("meta", "d-ana"), CancellationToken.None));

        Assert.Contains("MG132", texto);
        Assert.Contains("25%", texto); // 250 de 1.000 e 1 de 4
    }

    [Fact]
    public async Task Meta_SemMetaCadastrada_Avisa()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        await PessoaAsync(c, "Ana", c.Mg132);

        var texto = Texto(await c.Servico().ExecutarAsync(Comando("meta", "d-ana"), CancellationToken.None));

        Assert.Contains("não tem meta cadastrada", texto);
    }

    [Fact]
    public async Task Comando_DeQuemNaoVinculouOuFoiInativado_NaoMostraNumeroNenhum()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var ana = await PessoaAsync(c, "Ana", c.Mg132);
        await PessoaAsync(c, "Inativa", c.Mg132, ativa: false);
        await VendaAsync(c, ana, 100m, Agora);

        var desconhecido = Texto(await c.Servico().ExecutarAsync(Comando("vendas", "d-ninguem"), CancellationToken.None));
        var inativa = Texto(await c.Servico().ExecutarAsync(Comando("vendas", "d-inativa"), CancellationToken.None));

        Assert.Contains("Vincule o seu Discord", desconhecido);
        Assert.Contains("Vincule o seu Discord", inativa);
        Assert.DoesNotContain("R$", desconhecido + inativa);
    }

    [Fact]
    public async Task Comando_Desconhecido_OuSemQuemPediu_NaoQuebra()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        await PessoaAsync(c, "Ana", c.Mg132);

        Assert.Contains("não existe", Texto(await c.Servico().ExecutarAsync(Comando("xpto", "d-ana"), CancellationToken.None)));
        Assert.Contains("Não consegui entender", Texto(await c.Servico().ExecutarAsync(JsonDocument.Parse("""{"type":2}""").RootElement, CancellationToken.None)));
    }

    [Fact]
    public async Task Sincronizar_RegistraOsComandosSoComAChavePublicaConfigurada()
    {
        using var factory = new TestDbContextFactory();
        var db = factory.CreateContext();
        await factory.CriarRegionalAsync(db, "MG132");

        var sem = new DiscordServidorFalso();
        await new DiscordGruposService(db, sem, Options.Create(Opcoes("")), NullLogger<DiscordGruposService>.Instance).SincronizarAsync(CancellationToken.None);
        var com = new DiscordServidorFalso();
        await new DiscordGruposService(db, com, Options.Create(Opcoes(new string('a', 64))), NullLogger<DiscordGruposService>.Instance).SincronizarAsync(CancellationToken.None);

        Assert.Equal(0, sem.ComandosRegistrados);
        Assert.Equal(1, com.ComandosRegistrados);
    }
}
