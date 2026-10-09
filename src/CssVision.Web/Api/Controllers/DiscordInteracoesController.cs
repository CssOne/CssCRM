using System.Text.Json;
using CssVision.Web.Services.Discord;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CssVision.Web.Api.Controllers;

/// <summary>
/// Endereço que o Discord chama quando alguém usa um comando de barra (<c>/vendas</c>, <c>/meta</c>) — configurado no Portal do Desenvolvedor em
/// "Interactions Endpoint URL". Não usa o cookie de sessão: é autenticado pela assinatura Ed25519 de cada requisição (<see cref="DiscordAssinatura"/>),
/// por isso [AllowAnonymous]. Sem a chave pública configurada, responde 404 como se não existisse.
/// </summary>
[ApiController]
[Route("api/discord/interacoes")]
[AllowAnonymous]
public class DiscordInteracoesController(IOptions<DiscordOptions> options, IDiscordComandosService comandos, ILogger<DiscordInteracoesController> logger) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Receber(CancellationToken ct)
    {
        if (!options.Value.ComandosAtivos) return NotFound();

        using var leitor = new StreamReader(Request.Body);
        var corpo = await leitor.ReadToEndAsync(ct);
        if (!DiscordAssinatura.EhValida(options.Value.PublicKey.Trim(), Request.Headers["X-Signature-Timestamp"].FirstOrDefault(), corpo,
                Request.Headers["X-Signature-Ed25519"].FirstOrDefault(), DateTimeOffset.UtcNow))
        {
            logger.LogWarning("Interação do Discord com assinatura inválida recusada.");
            return Unauthorized();
        }

        JsonDocument documento;
        try
        {
            documento = JsonDocument.Parse(corpo);
        }
        catch (JsonException)
        {
            return BadRequest();
        }

        using (documento)
        {
            var tipo = documento.RootElement.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.Number ? t.GetInt32() : 0;
            // 1 = PING (o Discord testa o endereço); 2 = comando de barra.
            if (tipo == 1) return Ok(new { type = 1 });
            if (tipo == 2) return Ok(await comandos.ExecutarAsync(documento.RootElement, ct));
        }

        return BadRequest();
    }
}
