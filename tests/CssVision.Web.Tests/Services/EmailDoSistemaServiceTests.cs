using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Services.Crm;
using CssVision.Web.Services.Email;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>E-mail do sistema configurável pelo painel: prioridade sobre o servidor, senha protegida e sem vazar de volta.</summary>
public class EmailDoSistemaServiceTests : IDisposable
{
    private readonly TestDbContextFactory _factory = new();
    private readonly CssVision.Web.Data.ApplicationDbContext _db;
    private readonly IDataProtectionProvider _protecao = new EphemeralDataProtectionProvider();

    public EmailDoSistemaServiceTests() => _db = _factory.CreateContext();

    public void Dispose()
    {
        _db.Dispose();
        _factory.Dispose();
    }

    private EmailDoSistemaService Servico(SmtpOptions? servidor = null) =>
        new(_db, Options.Create(servidor ?? new SmtpOptions()), _protecao, Mock.Of<IAuditSink>(), NullLogger<EmailDoSistemaService>.Instance);

    private static EmailSistemaRequest Pedido(string? senha = "senha-de-app") =>
        new("smtp.exemplo.com", 587, "crm@exemplo.com", senha, "crm@exemplo.com", "CRM Exemplo");

    [Fact]
    public async Task SemNadaConfigurado_AvisaQueNaoEstaConfigurado()
    {
        var estado = await Servico().ObterAsync(CancellationToken.None);

        Assert.False(estado.Configurado);
        Assert.Equal("nenhuma", estado.Origem);
        Assert.True(string.IsNullOrWhiteSpace((await Servico().ResolverAsync(CancellationToken.None)).Host));
    }

    [Fact]
    public async Task ConfiguradoNoServidor_Vale_ESemSenhaNaResposta()
    {
        var env = new SmtpOptions { Host = "smtp.servidor.com", Port = 2525, UserName = "u", Password = "segredo", FromEmail = "de@servidor.com" };

        var estado = await Servico(env).ObterAsync(CancellationToken.None);

        Assert.True(estado.Configurado);
        Assert.Equal("servidor", estado.Origem);
        Assert.Equal("smtp.servidor.com", estado.Host);
        Assert.Equal("smtp.servidor.com", (await Servico(env).ResolverAsync(CancellationToken.None)).Host);
    }

    [Fact]
    public async Task Salvar_GuardaProtegido_ValeSobreOServidor_ENaoDevolveASenha()
    {
        var env = new SmtpOptions { Host = "smtp.servidor.com", FromEmail = "de@servidor.com" };
        var s = Servico(env);

        var salvo = await s.SalvarAsync(Pedido(), CancellationToken.None);

        Assert.Equal("painel", salvo.Origem);
        Assert.DoesNotContain("senha-de-app", System.Text.Json.JsonSerializer.Serialize(salvo));
        var bruto = (await _db.CrmParametros.AsNoTracking().SingleAsync(p => p.Chave == "email-sistema")).Valor;
        Assert.DoesNotContain("senha-de-app", bruto); // no banco a senha está protegida, não em texto
        var usada = await s.ResolverAsync(CancellationToken.None);
        Assert.Equal("smtp.exemplo.com", usada.Host);
        Assert.Equal("senha-de-app", usada.Password);
        Assert.Equal("CRM Exemplo", usada.FromName);
    }

    [Fact]
    public async Task SalvarSemSenha_MantemASenhaAnterior_ESemSenhaNenhuma_Recusa()
    {
        var s = Servico();
        var ex = await Assert.ThrowsAsync<CrmBusinessException>(() => s.SalvarAsync(Pedido(senha: null), CancellationToken.None));
        Assert.Equal("senha_obrigatoria", ex.Codigo);

        await s.SalvarAsync(Pedido(), CancellationToken.None);
        await s.SalvarAsync(Pedido(senha: "") with { Host = "smtp.outro.com" }, CancellationToken.None);

        var usada = await s.ResolverAsync(CancellationToken.None);
        Assert.Equal("smtp.outro.com", usada.Host);
        Assert.Equal("senha-de-app", usada.Password); // em branco = manter
    }

    [Theory]
    [InlineData("", 587, "u", "crm@x.com", "servidor_obrigatorio")]
    [InlineData("smtp.x.com", 0, "u", "crm@x.com", "porta_invalida")]
    [InlineData("smtp.x.com", 587, "", "crm@x.com", "usuario_obrigatorio")]
    [InlineData("smtp.x.com", 587, "u", "sem-arroba", "remetente_invalido")]
    public async Task DadosInvalidos_SaoRecusadosComMensagemClara(string host, int porta, string usuario, string de, string codigo)
    {
        var ex = await Assert.ThrowsAsync<CrmBusinessException>(() => Servico().SalvarAsync(new EmailSistemaRequest(host, porta, usuario, "s", de, null), CancellationToken.None));
        Assert.Equal(codigo, ex.Codigo);
    }

    [Fact]
    public async Task Remover_VoltaAUsarOServidor()
    {
        var env = new SmtpOptions { Host = "smtp.servidor.com", FromEmail = "de@servidor.com" };
        var s = Servico(env);
        await s.SalvarAsync(Pedido(), CancellationToken.None);

        await s.RemoverAsync(CancellationToken.None);

        Assert.Equal("servidor", (await s.ObterAsync(CancellationToken.None)).Origem);
        Assert.Equal("smtp.servidor.com", (await s.ResolverAsync(CancellationToken.None)).Host);
    }

    [Fact]
    public async Task SenhaQueNaoAbreMais_CaiNoServidor_SemDerrubarOEnvio()
    {
        var env = new SmtpOptions { Host = "smtp.servidor.com", FromEmail = "de@servidor.com" };
        await Servico(env).SalvarAsync(Pedido(), CancellationToken.None);

        // Outro conjunto de chaves de proteção (chaves perdidas): a senha guardada não abre.
        var outro = new EmailDoSistemaService(_db, Options.Create(env), new EphemeralDataProtectionProvider(), Mock.Of<IAuditSink>(), NullLogger<EmailDoSistemaService>.Instance);

        Assert.Equal("smtp.servidor.com", (await outro.ResolverAsync(CancellationToken.None)).Host);
    }
}
