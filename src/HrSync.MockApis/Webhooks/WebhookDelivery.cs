namespace HrSync.MockApis.Webhooks;

// Outcome of one delivery attempt. StatusCode is null when the receiver couldn't be reached.
public sealed record WebhookDelivery(
    string EventId, string Url, string CorrelationId, bool Tampered, int? StatusCode, string? Error);
