using CssVision.Web.Authorization;
using CssVision.Web.Data;
using CssVision.Web.Services.Crm;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CssVision.Web.Api.Controllers;

/// <summary>
/// Link "Cancelar inscrição" do rodapé dos e-mails do disparo (e cabeçalho List-Unsubscribe, que o Gmail usa no botão dele). Sem login: o token do link
/// já identifica o cliente. O GET só mostra a confirmação (programas de segurança abrem links de e-mail sozinhos); quem cancela é o POST — do botão da
/// página ou do "um clique" do Gmail.
/// </summary>
[ApiController]
[Route("api/publico/descadastro")]
[AllowAnonymous]
[EnableRateLimiting(RateLimitPolicies.FormularioPublico)]
public class PublicDescadastroController(IDescadastroEmailTokens tokens, ApplicationDbContext db) : ControllerBase
{
    [HttpGet]
    public ContentResult Confirmar([FromQuery] string? t)
    {
        if (tokens.Ler(t) is null) return Pagina(400, "Link inválido", "Este link de cancelamento não é válido ou foi alterado.");
        return Pagina(200, "Cancelar inscrição",
            "Quer parar de receber e-mails do seu consultor da CSS Brasil?",
            $"<form method=\"post\" action=\"/api/publico/descadastro?t={Uri.EscapeDataString(t!)}\"><button type=\"submit\">Sim, cancelar inscrição</button></form>");
    }

    [HttpPost]
    public async Task<ContentResult> Cancelar([FromQuery] string? t, CancellationToken ct)
    {
        if (tokens.Ler(t) is not { } leadId) return Pagina(400, "Link inválido", "Este link de cancelamento não é válido ou foi alterado.");
        await DescadastroEmail.DescadastrarAsync(db, leadId, ct);
        return Pagina(200, "Inscrição cancelada", "Pronto. Você não vai mais receber esses e-mails.");
    }

    private ContentResult Pagina(int status, string titulo, string texto, string extra = "") => new()
    {
        StatusCode = status,
        ContentType = "text/html; charset=utf-8",
        Content = $$"""
            <!doctype html>
            <html lang="pt-BR"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><meta name="robots" content="noindex">
            <title>{{titulo}}</title>
            <style>body{font-family:Arial,Helvetica,sans-serif;background:#f4f6f8;margin:0;display:flex;min-height:100vh;align-items:center;justify-content:center}
            .c{background:#fff;border-radius:12px;padding:32px;max-width:420px;box-shadow:0 8px 30px rgba(0,0,0,.08);text-align:center}
            h1{font-size:20px;margin:0 0 12px;color:#222}p{color:#555;line-height:1.5}
            button{background:#0b5ed7;color:#fff;border:0;border-radius:8px;padding:12px 20px;font-size:15px;cursor:pointer;margin-top:8px}</style></head>
            <body><div class="c"><h1>{{titulo}}</h1><p>{{texto}}</p>{{extra}}</div></body></html>
            """,
    };
}
