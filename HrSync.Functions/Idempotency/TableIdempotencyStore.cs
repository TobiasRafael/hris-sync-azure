using Azure;
using Azure.Data.Tables;
using HrSync.Core.Enums;
using HrSync.Core.Ports;

namespace HrSync.Functions.Idempotency;

// One row per webhook event in table "ProcessedEvents": PK "hris", RK = event id.
// Status: Processing -> Completed | Failed. A Processing row older than StaleAfter belongs to a
// crashed worker and can be taken over; a Failed row can be reclaimed (manual replay / vendor resend).
public sealed class TableIdempotencyStore(TableClient table) : IIdempotencyStore
{
    public const string TableName = "ProcessedEvents";
    public const string PartitionKey = "hris";
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(5);

    public const string StatusField = "Status";
    public const string ClaimedAtField = "ClaimedAt";
    public const string CompletedAtField = "CompletedAt";
    public const string FailureReasonField = "FailureReason";

    public const string Processing = "Processing";
    public const string Completed = "Completed";
    public const string Failed = "Failed";

    private const int MaxClaimAttempts = 3;
    private const int MaxReasonLength = 4000;

    private readonly SemaphoreSlim _createGate = new(1, 1);
    private volatile bool _tableReady;

    public async Task<ClaimResult> TryClaimAsync(string eventId, DateTimeOffset now, CancellationToken ct)
    {
        await EnsureTableAsync(ct);
        var rowKey = RowKey(eventId);

        for (var attempt = 0; attempt < MaxClaimAttempts; attempt++)
        {
            try
            {
                await table.AddEntityAsync(
                    new TableEntity(PartitionKey, rowKey) { [StatusField] = Processing, [ClaimedAtField] = now }, ct);
                return ClaimResult.Claimed;
            }
            catch (RequestFailedException ex) when (ex.Status == 409)
            {
                // Row exists: decide from its state below.
            }

            var existing = await table.GetEntityIfExistsAsync<TableEntity>(PartitionKey, rowKey, cancellationToken: ct);
            if (!existing.HasValue)
                continue; // Released between our add and read; try to add again.

            var entity = existing.Value!;
            var status = entity.GetString(StatusField);
            if (status == Completed)
                return ClaimResult.AlreadyCompleted;
            if (status == Processing && entity.GetDateTimeOffset(ClaimedAtField) is { } claimedAt && now - claimedAt < StaleAfter)
                return ClaimResult.InProgress;

            // Stale Processing or Failed: take it over. The ETag makes sure only one worker wins.
            entity[StatusField] = Processing;
            entity[ClaimedAtField] = now;
            entity.Remove(CompletedAtField);
            entity.Remove(FailureReasonField);
            try
            {
                await table.UpdateEntityAsync(entity, entity.ETag, TableUpdateMode.Replace, ct);
                return ClaimResult.Claimed;
            }
            catch (RequestFailedException ex) when (ex.Status is 412 or 404)
            {
                return ClaimResult.InProgress;
            }
        }

        return ClaimResult.InProgress;
    }

    public Task MarkCompletedAsync(string eventId, DateTimeOffset now, CancellationToken ct) =>
        table.UpsertEntityAsync(
            new TableEntity(PartitionKey, RowKey(eventId)) { [StatusField] = Completed, [CompletedAtField] = now },
            TableUpdateMode.Merge, ct);

    public Task MarkFailedAsync(string eventId, string reason, DateTimeOffset now, CancellationToken ct) =>
        table.UpsertEntityAsync(
            new TableEntity(PartitionKey, RowKey(eventId))
            {
                [StatusField] = Failed,
                [FailureReasonField] = reason.Length <= MaxReasonLength ? reason : reason[..MaxReasonLength],
            },
            TableUpdateMode.Merge, ct);

    // Deletes the row only while it is still Processing, using its ETag, so a row that someone
    // else completed or took over in the meantime is left alone.
    public async Task ReleaseAsync(string eventId, CancellationToken ct)
    {
        var rowKey = RowKey(eventId);
        var existing = await table.GetEntityIfExistsAsync<TableEntity>(PartitionKey, rowKey, cancellationToken: ct);
        if (!existing.HasValue || existing.Value!.GetString(StatusField) != Processing)
            return;

        try
        {
            await table.DeleteEntityAsync(PartitionKey, rowKey, existing.Value.ETag, ct);
        }
        catch (RequestFailedException ex) when (ex.Status is 404 or 412)
        {
            // Already gone or changed by someone else.
        }
    }

    // RowKey can't contain '/', '\', '#', '?' or control characters; GUIDs pass through unchanged.
    private static string RowKey(string eventId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);
        return Uri.EscapeDataString(eventId);
    }

    private async Task EnsureTableAsync(CancellationToken ct)
    {
        if (_tableReady)
            return;

        await _createGate.WaitAsync(ct);
        try
        {
            if (!_tableReady)
            {
                await table.CreateIfNotExistsAsync(ct);
                _tableReady = true;
            }
        }
        finally
        {
            _createGate.Release();
        }
    }
}
