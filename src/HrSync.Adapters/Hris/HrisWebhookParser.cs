using System.Text.Json;
using HrSync.Adapters.Enums;
using HrSync.Adapters.Http;
using HrSync.Core.Models;

namespace HrSync.Adapters.Hris;

// Vendor webhook JSON (snake_case) -> NewHireEvent.
public static class HrisWebhookParser
{
    public const string HireEventType = "employee.hired";

    private sealed record Payload(string? EventId, string? EventType, string? EmployeeId, DateTimeOffset? OccurredAt);

    public static WebhookParseResult Parse(string body)
    {
        Payload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<Payload>(body, VendorJson.Options);
        }
        catch (JsonException ex)
        {
            return WebhookParseResult.Invalid($"Malformed JSON: {ex.Message}");
        }

        if (payload is null)
            return WebhookParseResult.Invalid("Payload must be a JSON object.");
        if (string.IsNullOrWhiteSpace(payload.EventId))
            return WebhookParseResult.Invalid("event_id is required.");
        if (string.IsNullOrWhiteSpace(payload.EventType))
            return WebhookParseResult.Invalid("event_type is required.", payload.EventId);

        if (!string.Equals(payload.EventType, HireEventType, StringComparison.Ordinal))
            return WebhookParseResult.Ignored(payload.EventId, payload.EventType);

        if (string.IsNullOrWhiteSpace(payload.EmployeeId))
            return WebhookParseResult.Invalid("employee_id is required.", payload.EventId);
        if (payload.OccurredAt is not { } occurredAt)
            return WebhookParseResult.Invalid("occurred_at is required.", payload.EventId);

        return WebhookParseResult.Hire(
            new NewHireEvent { EventId = payload.EventId, EmployeeId = payload.EmployeeId, OccurredAt = occurredAt },
            payload.EventType);
    }
}
