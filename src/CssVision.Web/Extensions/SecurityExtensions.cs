using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using System.Threading.RateLimiting;
using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Authorization;
using Microsoft.AspNetCore.RateLimiting;

namespace CssVision.Web.Extensions;

/// <summary>Endurecimento de segurança da API: limite de requisições e cabeçalhos HTTP de proteção.</summary>
public static class SecurityExtensions
{
    /// <summary>
    /// Limites por IP (rotas sem login) e por usuário (rotas logadas). O IP vem de UseForwardedHeaders, que roda antes —
    /// sem isso todo mundo pareceria vir do load balancer e dividiria o mesmo limite.
    /// </summary>
    public static IServiceCollection AddCrmRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            static string Ip(HttpContext c) => c.Connection.RemoteIpAddress?.ToString() ?? "desconhecido";
            static string Usuario(HttpContext c) => c.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? Ip(c);

            static RateLimitPartition<string> PorJanela(string chave, int limite, TimeSpan janela) =>
                RateLimitPartition.GetFixedWindowLimiter(chave, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = limite,
                    Window = janela,
                    QueueLimit = 0,
                });

            options.AddPolicy(RateLimitPolicies.Login, c => PorJanela($"login:{Ip(c)}", 10, TimeSpan.FromMinutes(1)));
            // Folga para várias pessoas atrás do mesmo IP (escritório, rede da empresa) pedindo ou usando o link na mesma janela. O abuso contra uma
            // conta específica (encher a caixa de alguém) é barrado por conta em AccountController.EsqueciSenha, não por IP.
            options.AddPolicy(RateLimitPolicies.RecuperarSenha, c => PorJanela($"senha:{Ip(c)}", 30, TimeSpan.FromMinutes(10)));
            options.AddPolicy(RateLimitPolicies.FormularioPublico, c => PorJanela($"form:{Ip(c)}", 30, TimeSpan.FromMinutes(1)));
            options.AddPolicy(RateLimitPolicies.FinanceiroEscrita, c => PorJanela($"fin:{Usuario(c)}", 120, TimeSpan.FromMinutes(1)));

            options.OnRejected = async (contexto, ct) =>
            {
                if (contexto.Lease.TryGetMetadata(MetadataName.RetryAfter, out var espera))
                {
                    contexto.HttpContext.Response.Headers.RetryAfter = ((int)espera.TotalSeconds).ToString();
                }
                contexto.HttpContext.Response.ContentType = "application/json";
                await contexto.HttpContext.Response.WriteAsJsonAsync(
                    new ApiError("Muitas tentativas em pouco tempo. Aguarde um instante e tente de novo.", "muitas_requisicoes"), ct);
            };
        });

        return services;
    }

    /// <summary>Política de conteúdo (CSP) enviada em modo observação — ver UseCrmSecurityHeaders.</summary>
    private const string PoliticaDeConteudo =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; " +
        "font-src 'self' data: https://fonts.gstatic.com; img-src 'self' data: blob: https:; media-src 'self' blob: https:; " +
        "connect-src 'self' https: wss:; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'self'";

    /// <summary>
    /// Termos, comprovantes e demais anexos das vendas (têm CPF e dados de pagamento) só abrem para quem está logado; sem isso
    /// qualquer pessoa com o endereço do arquivo conseguia baixá-lo. Fotos de perfil continuam abertas (a página pública
    /// de "obrigado" mostra a foto do consultor). Vale para o armazenamento em disco; com S3 o acesso é regido pela política do bucket.
    /// </summary>
    public static IApplicationBuilder UseUploadsProtegidos(this IApplicationBuilder app) =>
        app.Use(async (contexto, proximo) =>
        {
            if (contexto.Request.Path.StartsWithSegments("/uploads/opportunities", StringComparison.OrdinalIgnoreCase))
            {
                var resultado = await contexto.AuthenticateAsync(IdentityConstants.ApplicationScheme);
                if (!resultado.Succeeded)
                {
                    contexto.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
                }
                contexto.User = resultado.Principal!;
                contexto.Response.Headers.CacheControl = "private, no-store";
            }
            await proximo();
        });

    /// <summary>
    /// Cabeçalhos que fecham brechas comuns do navegador: não adivinhar tipo de arquivo (MIME sniffing), não deixar a página
    /// ser embutida em site de terceiros (clickjacking), não vazar a URL completa para outros sites e desligar recursos do
    /// aparelho que o CRM não usa. A Content-Security-Policy vai em modo observação (só avisa no console, não bloqueia): o site
    /// carrega fontes e imagens externas e uma política errada derrubaria telas, então ela só passa a valer depois de um período sem avisos.
    /// </summary>
    public static IApplicationBuilder UseCrmSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(async (contexto, proximo) =>
        {
            contexto.Response.OnStarting(() =>
            {
                var h = contexto.Response.Headers;
                h["X-Content-Type-Options"] = "nosniff";
                h["X-Frame-Options"] = "SAMEORIGIN";
                h["Referrer-Policy"] = "strict-origin-when-cross-origin";
                h["Permissions-Policy"] = "geolocation=(), payment=(), usb=(), interest-cohort=()";
                // Respostas da API (dados de clientes) nunca ficam guardadas em cache do navegador/proxy.
                if (contexto.Request.Path.StartsWithSegments("/api"))
                {
                    if (!h.ContainsKey("Cache-Control")) h["Cache-Control"] = "no-store";
                }
                else
                {
                    // Política de conteúdo em modo OBSERVAÇÃO: o navegador só avisa no console o que ela bloquearia, sem bloquear
                    // nada. Depois de um tempo sem avisos, troque o nome do cabeçalho para Content-Security-Policy para valer de fato.
                    h["Content-Security-Policy-Report-Only"] = PoliticaDeConteudo;
                }
                return Task.CompletedTask;
            });
            await proximo();
        });
}
