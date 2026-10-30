using System.Text.Json;
using HrSync.Core.Models;

namespace HrSync.Adapters.Hris;

public enum WebhookParseStatus
{
    Hire,
    Ignored,
    Invalid,
}

// Hire: Event is set. Ignored: a well-formed event of a type we don't handle. Invalid: Error says why.
public sealed record WebhookParseResult(
    WebhookParseStatus Status, NewHireEvent? Event, string? EventId, string? EventType, string? Error)
{
    public static WebhookParseResult Hire(NewHireEvent hire, string eventType) =>
        new(WebhookParseStatus.Hire, hire, hire.EventId, eventType, null);

    public static WebhookParseResult Ignored(string eventId, string eventType) =>
        new(WebhookParseStatus.Ignored, null, eventId, eventType, null);

    public static WebhookParseResult Invalid(string error, string? eventId = null) =>
        new(WebhookParseStatus.Invalid, null, eventId, null, error);
}

// Vendor webhook JSON (snake_case) -> NewHireEvent.
public static class HrisWebhookParser
{
    public const string HireEventType = "employee.hired";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private sealed record Payload(string? EventId, string? EventType, string? EmployeeId, DateTimeOffset? OccurredAt);

    public static WebhookParseResult Parse(string body)
    {
        Payload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<Payload>(body, Json);
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
