namespace HrSync.Core.Models
{
    public sealed record NewHireEvent
    {
        // Vendor-assigned; stays the same when the vendor resends, so it is the idempotency key.
        public required string EventId { get; init; }
        public required string EmployeeId { get; init; }
        public required DateTimeOffset OccurredAt { get; init; }
    }
}
