using System.Text.Json;
using CssVision.Web.Services.Marketing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CssVision.Web.Api.Controllers;

/// <summary>
/// Recebe o webhook de Lead Ads da Meta: GET faz o handshake de verificação, POST recebe os
/// eventos de leadgen — porta de notion-lead-automation/src/index.ts. Autenticado pela própria
/// assinatura X-Hub-Signature-256 (não pelo cookie de sessão), por isso [AllowAnonymous].
/// </summary>
[ApiController]
[Route("api/webhooks/meta")]
[AllowAnonymous]
public class MetaWebhookController(
    IOptions<MetaLeadAdsOptions> options,
    MetaLeadIngestionService ingestion,
    ILogger<MetaWebhookController> logger) : ControllerBase
{
    [HttpGet]
    public IActionResult Verify(
        [FromQuery(Name = "hub.mode")] string? mode,
        [FromQuery(Name = "hub.verify_token")] string? token,
        [FromQuery(Name = "hub.challenge")] string? challenge)
    {
        if (mode == "subscribe" && !string.IsNullOrEmpty(challenge) && token == options.Value.VerifyToken)
        {
            logger.LogInformation("Handshake do webhook da Meta validado");
            return Content(challenge, "text/plain");
        }

        logger.LogWarning("Handshake do webhook da Meta rejeitado");
        return StatusCode(StatusCodes.Status403Forbidden);
    }

    [HttpPost]
    public async Task<IActionResult> Receive(CancellationToken ct)
    {
        Request.EnableBuffering();
        string rawBody;
        using (var reader = new StreamReader(Request.Body, leaveOpen: true))
        {
            rawBody = await reader.ReadToEndAsync(ct);
        }
        Request.Body.Position = 0;

        var signatureHeader = Request.Headers["X-Hub-Signature-256"].FirstOrDefault();
        if (!MetaWebhookSignature.IsValid(rawBody, signatureHeader, options.Value.AppSecret))
        {
            logger.LogWarning("Assinatura X-Hub-Signature-256 inválida no webhook da Meta");
            return StatusCode(StatusCodes.Status403Forbidden, new { ok = false, error = "invalid_signature" });
        }

        MetaWebhookPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<MetaWebhookPayload>(rawBody);
        }
        catch (JsonException)
        {
            logger.LogWarning("Corpo da requisição não é um JSON válido");
            return BadRequest(new { ok = false, error = "invalid_json" });
        }

        var eventos = ExtractLeadEvents(payload);
        logger.LogInformation("{Count} evento(s) de leadgen extraído(s) do webhook da Meta", eventos.Count);

        // Diferente do Worker (que usa ctx.waitUntil), aqui processamos antes de responder: o
        // DbContext é escopado por requisição, então "background" real exigiria um serviço à
        // parte (fila/BackgroundService). O timeout do webhook da Meta é generoso o bastante pra
        // isso não ser um problema na prática.
        foreach (var evento in eventos)
        {
            try
            {
                await ingestion.ProcessLeadEventAsync(evento.LeadgenId, evento.FormId, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Falha ao processar lead {LeadgenId} do webhook da Meta", evento.LeadgenId);
            }
        }

        return Ok(new { ok = true, received = eventos.Count });
    }

    private static List<MetaLeadEvent> ExtractLeadEvents(MetaWebhookPayload? payload)
    {
        var eventos = new List<MetaLeadEvent>();
        if (payload?.Entry is null) return eventos;

        foreach (var entry in payload.Entry)
        {
            if (entry.Changes is null) continue;
            foreach (var change in entry.Changes)
            {
                if (change.Field != "leadgen") continue;
                var leadgenId = change.Value?.LeadgenId;
                if (string.IsNullOrEmpty(leadgenId)) continue;
                eventos.Add(new MetaLeadEvent(leadgenId, change.Value?.FormId));
            }
        }
        return eventos;
    }
}
