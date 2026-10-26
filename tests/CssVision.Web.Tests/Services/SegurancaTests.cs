using System.Net;
using CssVision.Web.Authorization;
using CssVision.Web.Extensions;
using CssVision.Web.Services.Storage;
using CssVision.Web.Services.Crm;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CssVision.Web.Tests.Services;

public class AssinaturaDeArquivoTests
{
    private static readonly byte[] Pdf = "%PDF-1.7 conteudo"u8.ToArray();
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, 0x4A, 0x46, 0x49, 0x46, 0, 1];
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];
    private static readonly byte[] Webp = "RIFF\0\0\0\0WEBPVP8 "u8.ToArray();

    [Theory]
    [InlineData(".pdf")]
    [InlineData(".jpg")]
    [InlineData(".jpeg")]
    [InlineData(".png")]
    [InlineData(".webp")]
    public void ConteudoCerto_Passa(string extensao)
    {
        var conteudo = extensao switch { ".pdf" => Pdf, ".jpg" or ".jpeg" => Jpeg, ".png" => Png, _ => Webp };
        Assert.True(AssinaturaDeArquivo.Confere(conteudo, extensao));
    }

    [Fact]
    public void ExecutavelOuHtmlRenomeado_NaoPassa()
    {
        var html = "<html><script>alert(1)</script>"u8.ToArray();
        var exe = new byte[] { 0x4D, 0x5A, 0x90, 0x00, 0x03, 0, 0, 0, 4, 0, 0, 0 };
        foreach (var ext in new[] { ".pdf", ".jpg", ".png", ".webp" })
        {
            Assert.False(AssinaturaDeArquivo.Confere(html, ext));
            Assert.False(AssinaturaDeArquivo.Confere(exe, ext));
        }
    }

    [Fact]
    public void FormatoTrocado_NaoPassa() => Assert.False(AssinaturaDeArquivo.Confere(Png, ".pdf"));

    [Fact]
    public void ExtensaoDesconhecida_NaoPassa() => Assert.False(AssinaturaDeArquivo.Confere(Pdf, ".exe"));

    [Fact]
    public async Task LeituraDeStream_VoltaAoInicio_ParaOSalvamentoLerTudo()
    {
        await using var stream = new MemoryStream(Pdf);
        Assert.True(await AssinaturaDeArquivo.ConfereAsync(stream, ".pdf", CancellationToken.None));
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public async Task ArquivoVazioOuCurto_NaoPassa()
    {
        await using var stream = new MemoryStream([0x25]);
        Assert.False(await AssinaturaDeArquivo.ConfereAsync(stream, ".pdf", CancellationToken.None));
    }
}

public class SegurancaDaApiTests
{
    private static TestServer Servidor(Action<IApplicationBuilder> pipeline, Action<IServiceCollection>? servicos = null) =>
        new(new WebHostBuilder()
            .ConfigureServices(s => { s.AddRouting(); servicos?.Invoke(s); })
            .Configure(pipeline));

    [Fact]
    public async Task RespostaTemCabecalhosDeProtecao_ERespostasDaApiNaoSaoGuardadasEmCache()
    {
        using var servidor = Servidor(app =>
        {
            app.UseCrmSecurityHeaders();
            app.Run(ctx => ctx.Response.WriteAsync("ok"));
        });
        using var cliente = servidor.CreateClient();

        var api = await cliente.GetAsync("/api/qualquer");
        Assert.Equal("nosniff", api.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("SAMEORIGIN", api.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("strict-origin-when-cross-origin", api.Headers.GetValues("Referrer-Policy").Single());
        Assert.Equal("no-store", api.Headers.CacheControl!.ToString());

        Assert.False(api.Headers.Contains("Content-Security-Policy-Report-Only")); // a política de conteúdo é das páginas, não da API

        var pagina = await cliente.GetAsync("/");
        Assert.Equal("nosniff", pagina.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Null(pagina.Headers.CacheControl); // páginas/arquivos seguem a regra de cache própria deles
        // CSP em modo observação: avisa no console, não bloqueia nada.
        var csp = pagina.Headers.GetValues("Content-Security-Policy-Report-Only").Single();
        Assert.Contains("frame-ancestors 'self'", csp);
        Assert.Contains("object-src 'none'", csp);
        Assert.False(pagina.Headers.Contains("Content-Security-Policy"));
    }

    [Fact]
    public async Task AnexosDeVenda_SoAbremParaQuemEstaLogado_FotosDePerfilContinuamAbertas()
    {
        using var servidor = Servidor(
            app =>
            {
                app.UseAuthentication();
                app.UseUploadsProtegidos();
                app.Run(ctx => ctx.Response.WriteAsync("arquivo"));
            },
            s => s.AddAuthentication().AddCookie(IdentityConstants.ApplicationScheme));
        using var cliente = servidor.CreateClient();

        var termo = await cliente.GetAsync("/uploads/opportunities/abc/termo-adesao.pdf");
        Assert.Equal(HttpStatusCode.Unauthorized, termo.StatusCode);
        Assert.DoesNotContain("arquivo", await termo.Content.ReadAsStringAsync());

        var foto = await cliente.GetAsync("/uploads/consultores/maria.jpeg");
        Assert.Equal(HttpStatusCode.OK, foto.StatusCode);
    }

    [Fact]
    public async Task Login_DepoisDeMuitasTentativasSeguidas_Recebe429ComMensagem()
    {
        using var servidor = Servidor(
            app =>
            {
                app.UseRouting();
                app.UseRateLimiter();
                app.UseEndpoints(e => e.MapPost("/login", () => "ok").RequireRateLimiting(RateLimitPolicies.Login));
            },
            s => s.AddCrmRateLimiting());
        using var cliente = servidor.CreateClient();

        for (var i = 0; i < 10; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await cliente.PostAsync("/login", null)).StatusCode);
        }

        var barrada = await cliente.PostAsync("/login", null);
        Assert.Equal(HttpStatusCode.TooManyRequests, barrada.StatusCode);
        Assert.Contains("muitas_requisicoes", await barrada.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task RecuperarSenha_TemLimiteProprioPorIp()
    {
        using var servidor = Servidor(
            app =>
            {
                app.UseRouting();
                app.UseRateLimiter();
                app.UseEndpoints(e => e.MapPost("/esqueci", () => "ok").RequireRateLimiting(RateLimitPolicies.RecuperarSenha));
            },
            s => s.AddCrmRateLimiting());
        using var cliente = servidor.CreateClient();

        // 30 por IP em 10 minutos: folga para colegas na mesma rede; o abuso contra uma conta é barrado por conta (AccountController), não por IP.
        for (var i = 0; i < 30; i++) Assert.Equal(HttpStatusCode.OK, (await cliente.PostAsync("/esqueci", null)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await cliente.PostAsync("/esqueci", null)).StatusCode);
    }
}

public class SenhaSeguraTests
{
    [Fact]
    public void Gerar_Tem24Caracteres_ComTodosOsTiposEPassaNaPoliticaDoSistema()
    {
        for (var i = 0; i < 200; i++)
        {
            var senha = SenhaSegura.Gerar();
            Assert.Equal(24, senha.Length);
            Assert.Contains(senha, char.IsLower);
            Assert.Contains(senha, char.IsUpper);
            Assert.Contains(senha, char.IsDigit);
            Assert.Contains(senha, c => !char.IsLetterOrDigit(c));
            Assert.NotEqual(SenhaSegura.SenhaInicialLegada, senha);
        }
    }

    [Fact]
    public void Gerar_NaoRepete()
    {
        var senhas = Enumerable.Range(0, 500).Select(_ => SenhaSegura.Gerar()).ToHashSet();
        Assert.Equal(500, senhas.Count);
    }
}
