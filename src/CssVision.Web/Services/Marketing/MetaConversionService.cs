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
}

public sealed class MetaConversionService(
    HttpClient http,
    IOptions<MetaCapiOptions> options,
    ILogger<MetaConversionService> logger) : IMetaConversionService
{
    public async Task<bool> EnviarConversaoVendaAsync(CrmLead lead, CrmOpportunity opportunity, CancellationToken ct)
    {
        var opts = options.Value;
        if (string.IsNullOrWhiteSpace(opts.PixelId) || string.IsNullOrWhiteSpace(opts.AccessToken))
        {
            logger.LogWarning(
                "CAPI não configurado (PixelId/AccessToken vazios) — evento de conversão da oportunidade {OpportunityId} não enviado",
                opportunity.Id);
            return false;
        }

        var payload = MetaConversionEventBuilder.BuildVendaGanhaPayload(lead, opportunity, opts);
        var url = $"https://graph.facebook.com/{opts.GraphApiVersion}/{opts.PixelId}/events?access_token={Uri.EscapeDataString(opts.AccessToken)}";

        try
        {
            using var response = await http.PostAsJsonAsync(url, payload, ct);
            if (response.IsSuccessStatusCode)
            {
                logger.LogInformation("Conversão offline enviada ao Facebook pra oportunidade {OpportunityId}", opportunity.Id);
                return true;
            }

            var corpo = await response.Content.ReadAsStringAsync(ct);
            logger.LogError(
                "Facebook rejeitou o evento de conversão da oportunidade {OpportunityId} (status {Status}): {Corpo}",
                opportunity.Id, (int)response.StatusCode, corpo);
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Falha ao enviar conversão offline da oportunidade {OpportunityId}", opportunity.Id);
            return false;
        }
    }
}
