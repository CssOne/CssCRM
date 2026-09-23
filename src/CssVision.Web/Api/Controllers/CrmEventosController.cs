using System.Text.Json;
using CssVision.Web.Authorization;
using CssVision.Web.Services.Crm;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CssVision.Web.Api.Controllers;

[ApiController]
[Route("api/crm/eventos")]
[Authorize(Policy = PolicyNames.AreaComercial)]
public class CrmEventosController(ICrmEventHub eventos) : ControllerBase
{
    private static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Stream Server-Sent Events: emite "quadro-atualizado" sempre que o quadro de leads muda
    /// (movido por alguém no CRM ou pela sincronização com o Notion). O evento não carrega dados —
    /// a tela recarrega pela API, que já aplica o escopo de carteira do usuário. O navegador
    /// reconecta sozinho se a conexão cair.
    /// </summary>
    [HttpGet]
    public async Task Eventos(CancellationToken ct)
    {
        Response.Headers.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";

        await Response.WriteAsync(": conectado\n\n", ct);
        await Response.Body.FlushAsync(ct);

        await using var enumerador = eventos.AssinarAsync(ct).GetAsyncEnumerator(ct);
        var proximo = enumerador.MoveNextAsync().AsTask();

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var concluida = await Task.WhenAny(proximo, Task.Delay(Heartbeat, ct));
                if (concluida != proximo)
                {
                    // Comentário SSE: mantém a conexão viva através de proxies (Caddy/ALB).
                    await Response.WriteAsync(": ping\n\n", ct);
                    await Response.Body.FlushAsync(ct);
                    continue;
                }

                if (!await proximo) break;

                var evento = enumerador.Current;
                var dados = JsonSerializer.Serialize(new { evento.Origem, evento.OcorridoEm }, JsonSerializerOptions.Web);
                await Response.WriteAsync($"event: {evento.Tipo}\ndata: {dados}\n\n", ct);
                await Response.Body.FlushAsync(ct);

                proximo = enumerador.MoveNextAsync().AsTask();
            }
        }
        catch (OperationCanceledException)
        {
            // Aba fechada / navegação: fim normal do stream.
        }
    }
}
