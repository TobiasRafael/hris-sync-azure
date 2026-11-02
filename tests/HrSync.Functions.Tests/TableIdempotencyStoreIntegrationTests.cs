using Azure.Data.Tables;
using HrSync.Core.Enums;
using HrSync.Functions.Idempotency;

namespace HrSync.Functions.Tests;

// Against a real Azurite (Testcontainers): real 409/412 semantics and real concurrency.
// Run only these: dotnet test --filter "Category=Integration"; skip them: --filter "Category!=Integration".
// Needs Docker, or HRSYNC_TEST_AZURITE set to a running Azurite (see AzuriteFixture); otherwise they skip.
[Trait("Category", "Integration")]
public sealed class TableIdempotencyStoreIntegrationTests(AzuriteFixture azurite) : IClassFixture<AzuriteFixture>
{
    private const string Pk = TableIdempotencyStore.PartitionKey;

    private static readonly DateTimeOffset Now = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    // Own table, so runs against a shared local Azurite don't mix with the app's ProcessedEvents.
    private const string TableName = "ProcessedEventsTests";

    private TableClient Table() => new(azurite.ConnectionString, TableName);

    // A fresh store (own client, own "table ready" flag) per caller, like separate worker instances.
    private TableIdempotencyStore Store() => new(Table());

    private static string NewEventId() => Guid.NewGuid().ToString();

    private async Task<TableEntity> Row(string eventId) =>
        (await Table().GetEntityAsync<TableEntity>(Pk, eventId)).Value;

    [AzuriteFact]
    public async Task Claim_creates_processing_row()
    {
        var eventId = NewEventId();

        Assert.Equal(ClaimResult.Claimed, await Store().TryClaimAsync(eventId, Now, CancellationToken.None));

        var row = await Row(eventId);
        Assert.Equal("Processing", row.GetString("Status"));
        Assert.Equal(Now, row.GetDateTimeOffset("ClaimedAt"));
    }

    [AzuriteFact]
    public async Task Conflict_on_completed_row_is_already_completed()
    {
        var eventId = NewEventId();
        var store = Store();
        await store.TryClaimAsync(eventId, Now, CancellationToken.None);
        await store.MarkCompletedAsync(eventId, Now.AddSeconds(1), CancellationToken.None);

        Assert.Equal(ClaimResult.AlreadyCompleted, await Store().TryClaimAsync(eventId, Now.AddDays(1), CancellationToken.None));
        Assert.Equal(Now.AddSeconds(1), (await Row(eventId)).GetDateTimeOffset("CompletedAt"));
    }

    [AzuriteFact]
    public async Task Conflict_on_recent_processing_row_is_in_progress()
    {
        var eventId = NewEventId();
        await Store().TryClaimAsync(eventId, Now, CancellationToken.None);

        Assert.Equal(ClaimResult.InProgress, await Store().TryClaimAsync(eventId, Now.AddMinutes(4), CancellationToken.None));
    }

    [AzuriteFact]
    public async Task Stale_processing_row_is_taken_over_with_etag()
    {
        var eventId = NewEventId();
        await Store().TryClaimAsync(eventId, Now, CancellationToken.None);
        var before = await Row(eventId);

        Assert.Equal(ClaimResult.Claimed, await Store().TryClaimAsync(eventId, Now.AddMinutes(6), CancellationToken.None));

        var after = await Row(eventId);
        Assert.Equal(Now.AddMinutes(6), after.GetDateTimeOffset("ClaimedAt"));
        Assert.NotEqual(before.ETag, after.ETag);
    }

    [AzuriteFact]
    public async Task Concurrent_claims_only_one_wins()
    {
        var eventId = NewEventId();
        await Table().CreateIfNotExistsAsync();

        var results = await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(_ => Task.Run(() => Store().TryClaimAsync(eventId, Now, CancellationToken.None))));

        Assert.Equal(1, results.Count(r => r == ClaimResult.Claimed));
        Assert.Equal(19, results.Count(r => r == ClaimResult.InProgress));
    }

    [AzuriteFact]
    public async Task Concurrent_stale_takeovers_only_one_wins()
    {
        var eventId = NewEventId();
        await Store().TryClaimAsync(eventId, Now, CancellationToken.None);

        var results = await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(_ => Task.Run(() => Store().TryClaimAsync(eventId, Now.AddMinutes(10), CancellationToken.None))));

        Assert.Equal(1, results.Count(r => r == ClaimResult.Claimed));
    }

    [AzuriteFact]
    public async Task Failed_row_can_be_reclaimed_and_released_row_can_be_claimed_again()
    {
        var eventId = NewEventId();
        var store = Store();
        await store.TryClaimAsync(eventId, Now, CancellationToken.None);
        await store.MarkFailedAsync(eventId, "employee not found", Now, CancellationToken.None);

        Assert.Equal(ClaimResult.Claimed, await store.TryClaimAsync(eventId, Now.AddMinutes(1), CancellationToken.None));
        Assert.False((await Row(eventId)).ContainsKey("FailureReason"));

        await store.ReleaseAsync(eventId, CancellationToken.None);
        Assert.Equal(ClaimResult.Claimed, await store.TryClaimAsync(eventId, Now.AddMinutes(2), CancellationToken.None));
    }
}
