using HrSync.Adapters.Enums;
using HrSync.Core.Models;

namespace HrSync.Adapters.Hris;

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
