using HrSync.Core.Enums;

namespace HrSync.Core.Ports;

// Tracks webhook events by EventId so a redelivered or resent event is processed at most once.
public interface IIdempotencyStore
{
    Task<ClaimResult> TryClaimAsync(string eventId, DateTimeOffset now, CancellationToken ct);

    Task MarkCompletedAsync(string eventId, DateTimeOffset now, CancellationToken ct);

    Task MarkFailedAsync(string eventId, string reason, DateTimeOffset now, CancellationToken ct);

    // Drops our claim after a retryable failure, so the next delivery can claim it
    // instead of seeing InProgress until the claim goes stale.
    Task ReleaseAsync(string eventId, CancellationToken ct);
}
