using CssVision.Web.Api.Contracts;
using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Api.Controllers;
using CssVision.Web.Domain.Identity;
using CssVision.Web.Services.Crm;
using CssVision.Web.Services.Email;
using CssVision.Web.Services.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Login com a senha inicial antiga e link de redefinição de senha (ver AccountController).</summary>
public class LoginSegurancaTests
{
    private readonly Mock<UserManager<ApplicationUser>> _users = new(
        Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);
    private readonly Mock<IEmailSender> _email = new();
    private readonly ApplicationUser _usuario = new() { Id = Guid.NewGuid(), Email = "ana@teste.com", NomeCompleto = "Ana <b>", Ativo = true };
    private Mock<SignInManager<ApplicationUser>> _sign = null!;
    private string? _corpoEnviado;

    private AccountController Controller(string ambiente, string? baseUrl, string hostDaRequisicao = "app.exemplo.com")
    {
        _sign = new Mock<SignInManager<ApplicationUser>>(
            _users.Object, Mock.Of<IHttpContextAccessor>(), Mock.Of<IUserClaimsPrincipalFactory<ApplicationUser>>(), null!, null!, null!, null!);
        _users.Setup(u => u.FindByEmailAsync("ana@teste.com")).ReturnsAsync(_usuario);
        _users.Setup(u => u.GeneratePasswordResetTokenAsync(_usuario)).ReturnsAsync("token-secreto");
        _sign.Setup(s => s.PasswordSignInAsync(_usuario, It.IsAny<string>(), It.IsAny<bool>(), true)).ReturnsAsync(Microsoft.AspNetCore.Identity.SignInResult.Success);
        _email.Setup(e => e.EnviarAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string, string, CancellationToken>((_, _, _, corpo, _) => _corpoEnviado = corpo)
            .ReturnsAsync(true);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(baseUrl is null ? [] : new Dictionary<string, string?> { ["App:PublicBaseUrl"] = baseUrl })
            .Build();
        var env = new Mock<IWebHostEnvironment>();
        env.SetupGet(e => e.EnvironmentName).Returns(ambiente);

        var controller = new AccountController(_sign.Object, _users.Object, null!, Mock.Of<IFileStorageService>(), _email.Object, config, env.Object,
            NullLogger<AccountController>.Instance);
        var http = new DefaultHttpContext();
        http.Request.Scheme = "https";
        http.Request.Host = new HostString(hostDaRequisicao);
        controller.ControllerContext = new ControllerContext { HttpContext = http };
        return controller;
    }

    [Fact]
    public async Task Producao_SenhaInicialAntiga_NaoEntra_EnviaLinkPorEmail()
    {
        var c = Controller(Environments.Production, "https://app.exemplo.com");

        var resposta = await c.Login(new LoginRequest("ana@teste.com", SenhaSegura.SenhaInicialLegada), CancellationToken.None);

        var barrado = Assert.IsType<UnauthorizedObjectResult>(resposta.Result);
        Assert.Equal("senha_inicial", Assert.IsType<ApiError>(barrado.Value).Codigo);
        _sign.Verify(s => s.SignOutAsync(), Times.Once);
        _email.Verify(e => e.EnviarAsync("ana@teste.com", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Contains("token-secreto", _corpoEnviado);
    }

    [Fact]
    public async Task Producao_SenhaInicialAntiga_ComDoisFatores_TambemNaoPassa_NaoPedeOCodigo()
    {
        var c = Controller(Environments.Production, "https://app.exemplo.com");
        _sign.Setup(s => s.PasswordSignInAsync(_usuario, It.IsAny<string>(), It.IsAny<bool>(), true)).ReturnsAsync(Microsoft.AspNetCore.Identity.SignInResult.TwoFactorRequired);

        var resposta = await c.Login(new LoginRequest("ana@teste.com", SenhaSegura.SenhaInicialLegada), CancellationToken.None);

        var barrado = Assert.IsType<UnauthorizedObjectResult>(resposta.Result);
        Assert.Equal("senha_inicial", Assert.IsType<ApiError>(barrado.Value).Codigo);
        _email.Verify(e => e.EnviarAsync("ana@teste.com", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SenhaNormal_ComDoisFatores_SegueParaOCodigo()
    {
        var c = Controller(Environments.Production, "https://app.exemplo.com");
        _sign.Setup(s => s.PasswordSignInAsync(_usuario, It.IsAny<string>(), It.IsAny<bool>(), true)).ReturnsAsync(Microsoft.AspNetCore.Identity.SignInResult.TwoFactorRequired);

        var resposta = await c.Login(new LoginRequest("ana@teste.com", "Uma-Senha-Forte-1"), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(resposta.Result);
        Assert.True(Assert.IsType<LoginResultDto>(ok.Value).RequerDoisFatores);
    }

    [Fact]
    public async Task Desenvolvimento_ContasDeDemonstracaoContinuamEntrandoComASenhaInicial()
    {
        var c = Controller(Environments.Development, null);
        _users.Setup(u => u.GetRolesAsync(It.IsAny<ApplicationUser>())).ReturnsAsync([]);

        // O fluxo normal segue até montar a sessão (que precisa do banco, ausente aqui): o importante é que NÃO foi barrado como senha inicial.
        await Record.ExceptionAsync(() => c.Login(new LoginRequest("ana@teste.com", SenhaSegura.SenhaInicialLegada), CancellationToken.None));

        _sign.Verify(s => s.SignOutAsync(), Times.Never);
        _email.Verify(e => e.EnviarAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LinkDeRedefinicao_UsaOEnderecoConfigurado_NuncaOHostEnviadoPeloCliente()
    {
        // Atacante chama "esqueci a senha" da vítima com Host forjado: o link do e-mail não pode apontar para o domínio dele.
        var c = Controller(Environments.Production, "https://app.exemplo.com", hostDaRequisicao: "atacante.evil.com");

        await c.EsqueciSenha(new ForgotPasswordRequest("ana@teste.com"), CancellationToken.None);

        Assert.NotNull(_corpoEnviado);
        Assert.Contains("https://app.exemplo.com/reset-password?", _corpoEnviado);
        Assert.DoesNotContain("evil.com", _corpoEnviado);
        Assert.Contains("Ana &lt;b&gt;", _corpoEnviado); // nome com HTML não vira marcação no e-mail
    }
}

/// <summary>Esqueci a senha de ponta a ponta com o Identity de verdade: pede o link, usa o token do e-mail e confere a senha nova.</summary>
public class EsqueciSenhaFluxoTests : IDisposable
{
    private readonly Infrastructure.TestDbContextFactory _factory = new();
    private readonly Data.ApplicationDbContext _db;
    private readonly ServiceProvider _servicos;
    private readonly UserManager<ApplicationUser> _users;
    private readonly List<(string Para, string Corpo)> _emails = [];
    private readonly AccountController _c;

    public EsqueciSenhaFluxoTests()
    {
        _db = _factory.CreateContext();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddSingleton(_db);
        services.AddIdentityCore<ApplicationUser>(o => { o.User.RequireUniqueEmail = true; o.Password.RequiredLength = 8; })
            .AddRoles<ApplicationRole>().AddEntityFrameworkStores<Data.ApplicationDbContext>().AddDefaultTokenProviders();
        _servicos = services.BuildServiceProvider();
        _users = _servicos.GetRequiredService<UserManager<ApplicationUser>>();

        var email = new Mock<IEmailSender>();
        email.Setup(e => e.EnviarAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string, string, CancellationToken>((para, _, _, corpo, _) => _emails.Add((para, corpo)))
            .ReturnsAsync(true);
        var sign = new Mock<SignInManager<ApplicationUser>>(_users, Mock.Of<IHttpContextAccessor>(), Mock.Of<IUserClaimsPrincipalFactory<ApplicationUser>>(), null!, null!, null!, null!);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["App:PublicBaseUrl"] = "https://app.exemplo.com" }).Build();
        var env = new Mock<IWebHostEnvironment>();
        env.SetupGet(e => e.EnvironmentName).Returns(Environments.Production);
        _c = new AccountController(sign.Object, _users, null!, Mock.Of<IFileStorageService>(), email.Object, config, env.Object, NullLogger<AccountController>.Instance);
        _c.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
    }

    public void Dispose()
    {
        _servicos.Dispose();
        _db.Dispose();
        _factory.Dispose();
    }

    private async Task<ApplicationUser> CriarAsync(string email, string senha, bool ativo = true)
    {
        var u = new ApplicationUser { Id = Guid.NewGuid(), UserName = email, Email = email, NomeCompleto = "Ana Teste", Ativo = ativo };
        var r = await _users.CreateAsync(u, senha);
        Assert.True(r.Succeeded, string.Join(";", r.Errors.Select(e => e.Description)));
        return u;
    }

    /// <summary>Lê e-mail e token do link enviado, como o navegador faria ao abrir a página de redefinição.</summary>
    private (string Email, string Token) LinkEnviado()
    {
        var corpo = Assert.Single(_emails).Corpo;
        var url = new Uri(System.Text.RegularExpressions.Regex.Match(corpo, "href=\"([^\"]+)\"").Groups[1].Value);
        var query = System.Web.HttpUtility.ParseQueryString(url.Query);
        return (query["email"]!, query["token"]!);
    }

    [Fact]
    public async Task PedirLink_UsarOToken_TrocaASenha_EOLinkNaoServeDeNovo()
    {
        await CriarAsync("ana@teste.com", "SenhaAntiga@1");

        Assert.IsType<NoContentResult>(await _c.EsqueciSenha(new ForgotPasswordRequest("ana@teste.com"), CancellationToken.None));
        var (email, token) = LinkEnviado();
        Assert.Equal("ana@teste.com", email);

        Assert.IsType<NoContentResult>(await _c.RedefinirSenhaComToken(new ResetPasswordConfirmRequest(email, token, "NovaSenha@2"), CancellationToken.None));

        var ana = (await _users.FindByEmailAsync("ana@teste.com"))!;
        Assert.True(await _users.CheckPasswordAsync(ana, "NovaSenha@2"));
        Assert.False(await _users.CheckPasswordAsync(ana, "SenhaAntiga@1"));

        // O mesmo link usado outra vez (ou por outra pessoa) é recusado e a senha continua a nova.
        var ex = await Assert.ThrowsAsync<CrmBusinessException>(() => _c.RedefinirSenhaComToken(new ResetPasswordConfirmRequest(email, token, "Outra@Senha3"), CancellationToken.None));
        Assert.Equal("token_invalido", ex.Codigo);
        Assert.True(await _users.CheckPasswordAsync(ana, "NovaSenha@2"));
    }

    [Fact]
    public async Task TokenAdulterado_SenhaFraca_EEmailInexistente_SaoRecusados()
    {
        await CriarAsync("ana@teste.com", "SenhaAntiga@1");
        await _c.EsqueciSenha(new ForgotPasswordRequest("ana@teste.com"), CancellationToken.None);
        var (email, token) = LinkEnviado();

        await Assert.ThrowsAsync<CrmBusinessException>(() => _c.RedefinirSenhaComToken(new ResetPasswordConfirmRequest(email, token + "x", "NovaSenha@2"), CancellationToken.None));
        await Assert.ThrowsAsync<CrmBusinessException>(() => _c.RedefinirSenhaComToken(new ResetPasswordConfirmRequest(email, token, "curta"), CancellationToken.None));
        await Assert.ThrowsAsync<CrmBusinessException>(() => _c.RedefinirSenhaComToken(new ResetPasswordConfirmRequest("ninguem@teste.com", token, "NovaSenha@2"), CancellationToken.None));

        var ana = (await _users.FindByEmailAsync("ana@teste.com"))!;
        Assert.True(await _users.CheckPasswordAsync(ana, "SenhaAntiga@1")); // nada disso trocou a senha
        // A senha fraca não gastou o token: com uma senha boa o mesmo link ainda funciona.
        Assert.IsType<NoContentResult>(await _c.RedefinirSenhaComToken(new ResetPasswordConfirmRequest(email, token, "NovaSenha@2"), CancellationToken.None));
    }

    [Fact]
    public async Task EmailInexistenteOuContaInativa_RespondemIgual_ESemEnviarEmail()
    {
        await CriarAsync("inativa@teste.com", "SenhaAntiga@1", ativo: false);

        Assert.IsType<NoContentResult>(await _c.EsqueciSenha(new ForgotPasswordRequest("ninguem@teste.com"), CancellationToken.None));
        Assert.IsType<NoContentResult>(await _c.EsqueciSenha(new ForgotPasswordRequest("inativa@teste.com"), CancellationToken.None));

        Assert.Empty(_emails);
    }

    [Fact]
    public async Task PedirDeNovoLogoEmSeguida_NaoEnviaOutroEmail_MasResponde204()
    {
        await CriarAsync("ana@teste.com", "SenhaAntiga@1");

        Assert.IsType<NoContentResult>(await _c.EsqueciSenha(new ForgotPasswordRequest("ana@teste.com"), CancellationToken.None));
        Assert.IsType<NoContentResult>(await _c.EsqueciSenha(new ForgotPasswordRequest("ana@teste.com"), CancellationToken.None));
        Assert.IsType<NoContentResult>(await _c.EsqueciSenha(new ForgotPasswordRequest("ANA@teste.com"), CancellationToken.None));

        Assert.Single(_emails); // quem souber o e-mail de alguém não consegue encher a caixa dele
    }

    [Fact]
    public async Task EmailComEspacosEMaiusculas_AindaEncontraAConta()
    {
        await CriarAsync("ana@teste.com", "SenhaAntiga@1");

        await _c.EsqueciSenha(new ForgotPasswordRequest("  Ana@Teste.com "), CancellationToken.None);

        Assert.Equal("ana@teste.com", Assert.Single(_emails).Para);
    }
}
