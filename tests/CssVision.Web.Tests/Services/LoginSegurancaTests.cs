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
using Microsoft.Extensions.Configuration;
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
