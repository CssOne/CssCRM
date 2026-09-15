using System.Text.Json.Serialization;

namespace CssVision.Web.Services.Marketing;

public record MetaCapiPayload([property: JsonPropertyName("data")] List<MetaCapiEvent> Data);

public record MetaCapiEvent(
    [property: JsonPropertyName("event_name")] string EventName,
    [property: JsonPropertyName("event_time")] long EventTime,
    [property: JsonPropertyName("action_source")] string ActionSource,
    [property: JsonPropertyName("event_id")] string EventId,
    [property: JsonPropertyName("user_data")] MetaCapiUserData UserData,
    [property: JsonPropertyName("custom_data")] MetaCapiCustomData CustomData);

public record MetaCapiUserData(
    [property: JsonPropertyName("em")] List<string>? Em,
    [property: JsonPropertyName("ph")] List<string>? Ph,
    [property: JsonPropertyName("lead_id")] long? LeadId,
    [property: JsonPropertyName("fn")] List<string>? Fn,
    [property: JsonPropertyName("ln")] List<string>? Ln);

public record MetaCapiCustomData(
    [property: JsonPropertyName("event_source")] string EventSource,
    [property: JsonPropertyName("lead_event_source")] string LeadEventSource,
    [property: JsonPropertyName("value")] decimal? Value,
    [property: JsonPropertyName("currency")] string? Currency);

/// <summary>Erro ao chamar a Conversions API. Só logado — nunca deve derrubar o fluxo de venda ganha.</summary>
public class MetaCapiException(string message) : Exception(message);
