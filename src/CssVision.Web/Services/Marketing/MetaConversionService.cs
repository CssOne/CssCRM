using System.Net.Http.Json;
using CssVision.Web.Domain.Crm;
using Microsoft.Extensions.Options;

namespace CssVision.Web.Services.Marketing;

public interface IMetaConversionService
{
    /// <summary>
    /// Envia o evento de conversão offline pro Facebook (evento customizado + Purchase em
    /// paralelo). Não lança — falhas são logadas e engolidas, porque isso nunca deve impedir o
    /// registro da venda ganha no CRM. Retorna true só quando o envio foi bem-sucedido (o
    /// chamador usa isso pra decidir se marca ConversaoOfflineEnviadaEm).
    /// </summary>
    Task<bool> EnviarConversaoVendaAsync(CrmLead lead, CrmOpportunity opportunity, CancellationToken ct);

    /// <summary>
    /// Envia um evento customizado (nomeado com a própria etapa) pra qualquer mudança de etapa
    /// do lead no quadro. Não lança, mesma lógica de falha silenciosa da conversão de venda.
    /// </summary>
    Task<bool> EnviarEventoEtapaAsync(CrmLead lead, Guid etapaId, string etapaNome, CancellationToken ct);
}

public sealed class MetaConversionService(
    HttpClient http,
    IOptions<MetaCapiOptions> options,
    ILogger<MetaConversionService> logger) : IMetaConversionService
{
    public async Task<bool> EnviarConversaoVendaAsync(CrmLead lead, CrmOpportunity opportunity, CancellationToken ct)
    {
        var opts = options.Value;
        if (!TemCredenciais(opts, $"oportunidade {opportunity.Id}")) return false;

        var payload = MetaConversionEventBuilder.BuildVendaGanhaPayload(lead, opportunity, opts);
        return await EnviarAsync(payload, opts, $"conversão de venda (oportunidade {opportunity.Id})", ct);
    }

    public async Task<bool> EnviarEventoEtapaAsync(CrmLead lead, Guid etapaId, string etapaNome, CancellationToken ct)
    {
        var opts = options.Value;
        if (!TemCredenciais(opts, $"lead {lead.Id} -> etapa \"{etapaNome}\"")) return false;

        var payload = MetaConversionEventBuilder.BuildEtapaEventPayload(lead, etapaId, etapaNome, opts);
        return await EnviarAsync(payload, opts, $"etapa \"{etapaNome}\" (lead {lead.Id})", ct);
    }

    private bool TemCredenciais(MetaCapiOptions opts, string contexto)
    {
        if (!string.IsNullOrWhiteSpace(opts.PixelId) && !string.IsNullOrWhiteSpace(opts.AccessToken)) return true;

        logger.LogWarning("CAPI não configurado (PixelId/AccessToken vazios) — evento de {Contexto} não enviado", contexto);
        return false;
    }

    private async Task<bool> EnviarAsync(MetaCapiPayload payload, MetaCapiOptions opts, string contexto, CancellationToken ct)
    {
        var url = $"https://graph.facebook.com/{opts.GraphApiVersion}/{opts.PixelId}/events?access_token={Uri.EscapeDataString(opts.AccessToken)}";

        try
        {
            using var response = await http.PostAsJsonAsync(url, payload, ct);
            if (response.IsSuccessStatusCode)
            {
                logger.LogInformation("Evento de {Contexto} enviado ao Facebook", contexto);
                return true;
            }

            var corpo = await response.Content.ReadAsStringAsync(ct);
            logger.LogError("Facebook rejeitou o evento de {Contexto} (status {Status}): {Corpo}", contexto, (int)response.StatusCode, corpo);
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Falha ao enviar evento de {Contexto}", contexto);
            return false;
        }
    }
}
