using System.Text.Json.Serialization;

namespace CssVision.Web.Services.Marketing;

// --- Payload do webhook (POST recebido da Meta) ---

public record MetaWebhookPayload(
    [property: JsonPropertyName("object")] string? Object,
    [property: JsonPropertyName("entry")] List<MetaWebhookEntry>? Entry);

public record MetaWebhookEntry(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("time")] long? Time,
    [property: JsonPropertyName("changes")] List<MetaWebhookChange>? Changes);

public record MetaWebhookChange(
    [property: JsonPropertyName("field")] string? Field,
    [property: JsonPropertyName("value")] MetaLeadgenValue? Value);

public record MetaLeadgenValue(
    [property: JsonPropertyName("leadgen_id")] string? LeadgenId,
    [property: JsonPropertyName("page_id")] string? PageId,
    [property: JsonPropertyName("form_id")] string? FormId,
    [property: JsonPropertyName("ad_id")] string? AdId,
    [property: JsonPropertyName("created_time")] long? CreatedTime);

/// <summary>Um evento de leadgen já extraído do payload (leadgen_id garantido presente).</summary>
public record MetaLeadEvent(string LeadgenId, string? FormId);

// --- Resposta da Graph API (GET /{leadgen_id}) ---

public record MetaLeadResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("form_id")] string? FormId,
    [property: JsonPropertyName("ad_id")] string? AdId,
    [property: JsonPropertyName("campaign_id")] string? CampaignId,
    [property: JsonPropertyName("created_time")] string? CreatedTime,
    [property: JsonPropertyName("field_data")] List<MetaFieldDatum>? FieldData);

public record MetaFieldDatum(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("values")] List<string>? Values);

/// <summary>Erro ao chamar a Graph API. <see cref="Permanent"/> indica se não adianta reenviar.</summary>
public class MetaGraphException(string message, bool permanent = false) : Exception(message)
{
    public bool Permanent { get; } = permanent;
}
