using System.Text.Json.Serialization;

namespace HrSync.Functions.Webhooks;

public sealed record WebhookAccepted([property: JsonPropertyName("correlation_id")] string CorrelationId);
