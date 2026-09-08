using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace CssVision.Web.Services.Marketing;

public interface IMetaGraphClient
{
    Task<MetaLeadResponse> FetchLeadAsync(string leadgenId, CancellationToken ct);
}

/// <summary>Busca o lead completo na Graph API a partir do leadgen_id (porta de meta.ts::fetchLeadFromMeta).</summary>
public class MetaGraphClient(HttpClient http, IOptions<MetaLeadAdsOptions> options) : IMetaGraphClient
{
    private const string LeadFields = "id,created_time,field_data,ad_id,form_id,campaign_id";

    public async Task<MetaLeadResponse> FetchLeadAsync(string leadgenId, CancellationToken ct)
    {
        var opts = options.Value;
        var url = $"https://graph.facebook.com/{opts.GraphApiVersion}/{Uri.EscapeDataString(leadgenId)}" +
                  $"?fields={LeadFields}&access_token={Uri.EscapeDataString(opts.PageAccessToken)}";

        using var response = await http.GetAsync(url, ct);

        if (response.IsSuccessStatusCode)
        {
            var lead = await response.Content.ReadFromJsonAsync<MetaLeadResponse>(ct);
            return lead ?? throw new MetaGraphException("Resposta vazia da Graph API");
        }

        var status = (int)response.StatusCode;
        if (status is 401 or 403)
        {
            throw new MetaGraphException("Token de acesso da Meta inválido, expirado ou sem permissão", permanent: true);
        }
        if (status == 404)
        {
            throw new MetaGraphException("Lead não encontrado na Graph API", permanent: true);
        }
        if (status == 429 || status >= 500)
        {
            throw new MetaGraphException($"Graph API indisponível (status {status})");
        }
        throw new MetaGraphException($"Erro inesperado da Graph API (status {status})", permanent: true);
    }
}
