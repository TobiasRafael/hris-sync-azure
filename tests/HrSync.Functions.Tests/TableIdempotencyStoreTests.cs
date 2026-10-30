using Azure.Data.Tables;
using HrSync.Core.Enums;
using HrSync.Functions.Idempotency;

namespace HrSync.Functions.Tests;

public class TableIdempotencyStoreTests
{
    private const string Pk = TableIdempotencyStore.PartitionKey;

    private static readonly DateTimeOffset Now = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeTableClient _table = new();
    private readonly TableIdempotencyStore _store;

    public TableIdempotencyStoreTests() => _store = new TableIdempotencyStore(_table);

    private Task<ClaimResult> Claim(string eventId = "evt-1", DateTimeOffset? at = null) =>
        _store.TryClaimAsync(eventId, at ?? Now, CancellationToken.None);

    [Fact]
    public async Task First_claim_wins_and_records_processing()
    {
        Assert.Equal(ClaimResult.Claimed, await Claim());

        var row = _table.Row(Pk, "evt-1")!;
        Assert.Equal("Processing", row.GetString("Status"));
        Assert.Equal(Now, row.GetDateTimeOffset("ClaimedAt"));
    }

    [Fact]
    public async Task Second_claim_within_five_minutes_is_in_progress()
    {
        await Claim();
        Assert.Equal(ClaimResult.InProgress, await Claim(at: Now.AddMinutes(4)));
    }

    [Fact]
    public async Task Completed_event_is_already_completed()
    {
        await Claim();
        await _store.MarkCompletedAsync("evt-1", Now.AddSeconds(2), CancellationToken.None);

        Assert.Equal(ClaimResult.AlreadyCompleted, await Claim(at: Now.AddDays(1)));

        var row = _table.Row(Pk, "evt-1")!;
        Assert.Equal("Completed", row.GetString("Status"));
        Assert.Equal(Now, row.GetDateTimeOffset("ClaimedAt"));
        Assert.Equal(Now.AddSeconds(2), row.GetDateTimeOffset("CompletedAt"));
    }

    [Fact]
    public async Task Stale_processing_claim_is_taken_over()
    {
        await Claim();

        Assert.Equal(ClaimResult.Claimed, await Claim(at: Now.AddMinutes(6)));
        Assert.Equal(Now.AddMinutes(6), _table.Row(Pk, "evt-1")!.GetDateTimeOffset("ClaimedAt"));
    }

    [Fact]
    public async Task Losing_the_takeover_race_is_in_progress()
    {
        await Claim();
        _table.BeforeUpdate = () => _table.Touch(Pk, "evt-1");

        Assert.Equal(ClaimResult.InProgress, await Claim(at: Now.AddMinutes(6)));
    }

    [Fact]
    public async Task Failed_event_can_be_reclaimed_and_reason_is_cleared()
    {
        await Claim();
        await _store.MarkFailedAsync("evt-1", "employee not found", Now, CancellationToken.None);
        Assert.Equal("employee not found", _table.Row(Pk, "evt-1")!.GetString("FailureReason"));

        Assert.Equal(ClaimResult.Claimed, await Claim(at: Now.AddMinutes(1)));

        var row = _table.Row(Pk, "evt-1")!;
        Assert.Equal("Processing", row.GetString("Status"));
        Assert.False(row.ContainsKey("FailureReason"));
    }

    [Fact]
    public async Task Long_failure_reason_is_truncated()
    {
        await Claim();
        await _store.MarkFailedAsync("evt-1", new string('x', 10_000), Now, CancellationToken.None);

        Assert.Equal(4000, _table.Row(Pk, "evt-1")!.GetString("FailureReason").Length);
    }

    [Fact]
    public async Task Released_claim_can_be_claimed_again_immediately()
    {
        await Claim();
        await _store.ReleaseAsync("evt-1", CancellationToken.None);

        Assert.Null(_table.Row(Pk, "evt-1"));
        Assert.Equal(ClaimResult.Claimed, await Claim(at: Now.AddSeconds(30)));
    }

    [Fact]
    public async Task Release_leaves_completed_rows_alone()
    {
        await Claim();
        await _store.MarkCompletedAsync("evt-1", Now, CancellationToken.None);

        await _store.ReleaseAsync("evt-1", CancellationToken.None);

        Assert.Equal("Completed", _table.Row(Pk, "evt-1")!.GetString("Status"));
    }

    [Fact]
    public async Task Release_of_missing_row_is_a_no_op()
    {
        await _store.ReleaseAsync("evt-unknown", CancellationToken.None);
    }

    [Fact]
    public async Task Event_ids_with_reserved_characters_are_escaped()
    {
        Assert.Equal(ClaimResult.Claimed, await Claim("a/b#c?d"));
        Assert.NotNull(_table.Row(Pk, Uri.EscapeDataString("a/b#c?d")));
        Assert.Equal(ClaimResult.InProgress, await Claim("a/b#c?d"));
    }

    [Fact]
    public async Task Table_is_created_once()
    {
        await Claim("evt-1");
        await Claim("evt-2");
        await Claim("evt-3");

        Assert.Equal(1, _table.CreateCalls);
    }

    [Fact]
    public async Task Processing_row_without_claimed_at_is_treated_as_stale()
    {
        _table.Put(new TableEntity(Pk, "evt-1") { ["Status"] = "Processing" });

        Assert.Equal(ClaimResult.Claimed, await Claim());
    }
}
