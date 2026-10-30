namespace HrSync.MockApis.Webhooks;

public sealed record WebhookEvent(string EventId, string EventType, string EmployeeId, DateTimeOffset OccurredAt);
