using HrSync.Core.Errors;
using HrSync.Core.Mapping;
using HrSync.Core.Mappings;
using HrSync.Core.Models;
using HrSync.Core.Ports;
using HrSync.Core.Processing;

namespace HrSync.Core.Tests;

public class NewHireProcessorTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    private static readonly Employee Alice = new()
    {
        Id = "E-1001",
        FirstName = "Alice",
        LastName = "Smith",
        WorkEmail = "alice@contoso.test",
        MobilePhone = "+1-555-0100",
        JobTitle = "Caregiver",
        HireDate = new DateOnly(2026, 11, 2),
    };

    // Sparse record: no work email, no phone.
    private static readonly Employee Bob = new()
    {
        Id = "E-1002",
        FirstName = "Bob",
        LastName = "Jones",
        WorkEmail = "",
        MobilePhone = null,
        JobTitle = "Scheduler",
        HireDate = new DateOnly(2026, 11, 9),
    };

    private static readonly FieldMapper Mapper = new(new FieldMappingOptions
    {
        Fields = new()
        {
            ["GivenName"] = "FirstName",
            ["FamilyName"] = "LastName",
            ["Email"] = "WorkEmail",
            ["Phone"] = "MobilePhone",
            ["Role"] = "JobTitle",
            ["StartDate"] = "HireDate",
        }
    });

    private readonly FakeHris _hris = new();
    private readonly FakeCare _care = new();
    private readonly FakeIdempotencyStore _store = new();

    private NewHireProcessor Processor() => new(_hris, _care, _store, Mapper, new FixedTimeProvider(Now));

    private static NewHireMessage MessageFor(string employeeId, string eventId = "evt-1") => new(
        new NewHireEvent { EventId = eventId, EmployeeId = employeeId, OccurredAt = Now.AddMinutes(-1) },
        "corr-1",
        Now);

    [Fact]
    public async Task New_worker_is_created_from_mapped_employee()
    {
        _hris.Employees[Alice.Id] = Alice;

        var result = await Processor().ProcessAsync(MessageFor("E-1001"), CancellationToken.None);

        Assert.Equal(ProcessResult.Synced, result);
        var upserted = Assert.Single(_care.Upserts);
        Assert.Equal(new CareWorker
        {
            ExternalId = "E-1001",
            GivenName = "Alice",
            FamilyName = "Smith",
            Email = "alice@contoso.test",
            Phone = "+1-555-0100",
            Role = "Caregiver",
            StartDate = new DateOnly(2026, 11, 2),
        }, upserted);
        Assert.Equal(["evt-1"], _store.Completed);
        Assert.Empty(_store.Failed);
        Assert.Empty(_store.Released);
    }

    [Fact]
    public async Task Existing_worker_is_merged_so_sparse_employee_keeps_phone_and_email()
    {
        _hris.Employees[Bob.Id] = Bob;
        _care.Workers[Bob.Id] = new CareWorker
        {
            ExternalId = "E-1002",
            GivenName = "Robert",
            FamilyName = "Jones",
            Email = "bob@care.test",
            Phone = "+1-555-0199",
            Role = "Trainee",
        };

        var result = await Processor().ProcessAsync(MessageFor("E-1002"), CancellationToken.None);

        Assert.Equal(ProcessResult.Synced, result);
        var upserted = Assert.Single(_care.Upserts);
        Assert.Equal("Bob", upserted.GivenName);
        Assert.Equal("Scheduler", upserted.Role);
        Assert.Equal(new DateOnly(2026, 11, 9), upserted.StartDate);
        Assert.Equal("+1-555-0199", upserted.Phone);
        Assert.Equal("bob@care.test", upserted.Email);
    }

    [Fact]
    public async Task Claim_uses_event_id_and_current_time()
    {
        _hris.Employees[Alice.Id] = Alice;

        await Processor().ProcessAsync(MessageFor("E-1001", eventId: "evt-42"), CancellationToken.None);

        Assert.Equal([("evt-42", Now)], _store.Claims);
    }

    [Fact]
    public async Task Already_completed_event_is_skipped_with_zero_client_calls()
    {
        _store.NextClaim = ClaimResult.AlreadyCompleted;
        _hris.Employees[Alice.Id] = Alice;

        var result = await Processor().ProcessAsync(MessageFor("E-1001"), CancellationToken.None);

        Assert.Equal(ProcessResult.Skipped, result);
        Assert.Equal(0, _hris.Calls);
        Assert.Equal(0, _care.Calls);
        Assert.Empty(_store.Completed);
    }

    [Fact]
    public async Task In_progress_event_throws_transient_without_client_calls()
    {
        _store.NextClaim = ClaimResult.InProgress;
        _hris.Employees[Alice.Id] = Alice;

        await Assert.ThrowsAsync<TransientIntegrationException>(
            () => Processor().ProcessAsync(MessageFor("E-1001"), CancellationToken.None));

        Assert.Equal(0, _hris.Calls);
        Assert.Equal(0, _care.Calls);
        // Someone else holds the claim; it isn't ours to release.
        Assert.Empty(_store.Released);
    }

    [Fact]
    public async Task Employee_not_found_is_permanent_and_marked_failed()
    {
        var ex = await Assert.ThrowsAsync<PermanentIntegrationException>(
            () => Processor().ProcessAsync(MessageFor("E-9999"), CancellationToken.None));

        Assert.Contains("not found", ex.Reason);
        Assert.Empty(_care.Upserts);
        var (eventId, reason) = Assert.Single(_store.Failed);
        Assert.Equal("evt-1", eventId);
        Assert.Equal(ex.Reason, reason);
        Assert.Empty(_store.Completed);
    }

    [Fact]
    public async Task Upsert_permanent_failure_is_marked_failed_and_rethrown()
    {
        _hris.Employees[Alice.Id] = Alice;
        _care.UpsertError = new PermanentIntegrationException("care API rejected worker: 400 given_name is required");

        var ex = await Assert.ThrowsAsync<PermanentIntegrationException>(
            () => Processor().ProcessAsync(MessageFor("E-1001"), CancellationToken.None));

        Assert.Same(_care.UpsertError, ex);
        Assert.Equal([("evt-1", ex.Reason)], _store.Failed);
        Assert.Empty(_store.Completed);
        Assert.Empty(_store.Released);
    }

    [Fact]
    public async Task Transient_failure_propagates_without_marking_failed_and_releases_claim()
    {
        _hris.Employees[Alice.Id] = Alice;
        _care.UpsertError = new TransientIntegrationException("care API returned 503");

        var ex = await Assert.ThrowsAsync<TransientIntegrationException>(
            () => Processor().ProcessAsync(MessageFor("E-1001"), CancellationToken.None));

        Assert.Same(_care.UpsertError, ex);
        Assert.Empty(_store.Failed);
        Assert.Empty(_store.Completed);
        // Released so the queue's next retry can claim it instead of seeing InProgress.
        Assert.Equal(["evt-1"], _store.Released);
    }

    [Fact]
    public async Task Unexpected_exception_propagates_and_releases_claim()
    {
        _hris.Error = new InvalidOperationException("boom");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Processor().ProcessAsync(MessageFor("E-1001"), CancellationToken.None));

        Assert.Empty(_store.Failed);
        Assert.Equal(["evt-1"], _store.Released);
    }

    [Fact]
    public async Task Completed_is_marked_only_after_upsert()
    {
        _hris.Employees[Alice.Id] = Alice;
        _care.OnUpsert = () => Assert.Empty(_store.Completed);

        await Processor().ProcessAsync(MessageFor("E-1001"), CancellationToken.None);

        Assert.Single(_store.Completed);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeHris : IHrisClient
    {
        public Dictionary<string, Employee> Employees { get; } = new();
        public Exception? Error { get; set; }
        public int Calls { get; private set; }

        public Task<Employee?> GetEmployeeAsync(string id, CancellationToken ct)
        {
            Calls++;
            if (Error is not null)
                throw Error;
            return Task.FromResult(Employees.GetValueOrDefault(id));
        }
    }

    private sealed class FakeCare : ICareClient
    {
        public Dictionary<string, CareWorker> Workers { get; } = new();
        public List<CareWorker> Upserts { get; } = new();
        public Exception? UpsertError { get; set; }
        public Action? OnUpsert { get; set; }
        public int Calls { get; private set; }

        public Task<CareWorker?> GetWorkerAsync(string externalId, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(Workers.GetValueOrDefault(externalId));
        }

        public Task UpsertWorkerAsync(CareWorker worker, CancellationToken ct)
        {
            Calls++;
            OnUpsert?.Invoke();
            if (UpsertError is not null)
                throw UpsertError;
            Upserts.Add(worker);
            Workers[worker.ExternalId!] = worker;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeIdempotencyStore : IIdempotencyStore
    {
        public ClaimResult NextClaim { get; set; } = ClaimResult.Claimed;
        public List<(string EventId, DateTimeOffset Now)> Claims { get; } = new();
        public List<string> Completed { get; } = new();
        public List<(string EventId, string Reason)> Failed { get; } = new();
        public List<string> Released { get; } = new();

        public Task<ClaimResult> TryClaimAsync(string eventId, DateTimeOffset now, CancellationToken ct)
        {
            Claims.Add((eventId, now));
            return Task.FromResult(NextClaim);
        }

        public Task MarkCompletedAsync(string eventId, DateTimeOffset now, CancellationToken ct)
        {
            Completed.Add(eventId);
            return Task.CompletedTask;
        }

        public Task MarkFailedAsync(string eventId, string reason, DateTimeOffset now, CancellationToken ct)
        {
            Failed.Add((eventId, reason));
            return Task.CompletedTask;
        }

        public Task ReleaseAsync(string eventId, CancellationToken ct)
        {
            Released.Add(eventId);
            return Task.CompletedTask;
        }
    }
}
