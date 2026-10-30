using HrSync.Adapters.Correlation;
using HrSync.Core.Enums;
using HrSync.Core.Errors;
using HrSync.Core.Mapping;
using HrSync.Core.Mappings;
using HrSync.Core.Models;
using HrSync.Core.Ports;
using HrSync.Core.Processing;
using HrSync.Functions.NewHires;
using Microsoft.Extensions.Logging.Abstractions;

namespace HrSync.Functions.Tests;

public class ProcessNewHireTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    private static readonly FieldMapper Mapper = new(new FieldMappingOptions
    {
        Fields = new() { ["GivenName"] = "FirstName", ["FamilyName"] = "LastName", ["Phone"] = "MobilePhone" }
    });

    private static readonly NewHireMessage Message = new(
        new NewHireEvent { EventId = "evt-1", EmployeeId = "E-1001", OccurredAt = Now.AddMinutes(-1) },
        "corr-1",
        Now);

    private readonly FakeHris _hris = new();
    private readonly FakeCare _care = new();
    private readonly FakePoisonQueue _poison = new();
    private readonly CorrelationContext _correlation = new();

    private ProcessNewHire Function()
    {
        var time = new FixedTimeProvider(Now);
        var processor = new NewHireProcessor(_hris, _care, new AlwaysClaimStore(), Mapper, time);
        return new ProcessNewHire(processor, _correlation, _poison, time, NullLogger<ProcessNewHire>.Instance);
    }

    [Fact]
    public async Task Success_upserts_and_writes_nothing_to_poison()
    {
        _hris.Employee = new Employee { Id = "E-1001", FirstName = "Alice", LastName = "Smith" };

        await Function().Run(Message, CancellationToken.None);

        Assert.Equal("Alice", Assert.Single(_care.Upserts).GivenName);
        Assert.Empty(_poison.Sent);
    }

    [Fact]
    public async Task Permanent_failure_writes_envelope_to_poison_and_does_not_throw()
    {
        _hris.Employee = null; // HRIS 404 => "not found" => permanent

        await Function().Run(Message, CancellationToken.None);

        var envelope = Assert.Single(_poison.Sent);
        Assert.Equal(Message, envelope.OriginalMessage);
        Assert.Contains("not found", envelope.Reason);
        Assert.Equal(Now, envelope.FailedAt);
        Assert.Empty(_care.Upserts);
    }

    [Fact]
    public async Task Permanent_failure_from_care_api_goes_to_poison_with_reason()
    {
        _hris.Employee = new Employee { Id = "E-1001", FirstName = "Alice", LastName = "" };
        _care.UpsertError = new PermanentIntegrationException("Care API PUT returned 400: family_name is required.");

        await Function().Run(Message, CancellationToken.None);

        Assert.Equal("Care API PUT returned 400: family_name is required.", Assert.Single(_poison.Sent).Reason);
    }

    [Fact]
    public async Task Transient_failure_is_rethrown_and_not_poisoned()
    {
        _hris.Employee = new Employee { Id = "E-1001", FirstName = "Alice", LastName = "Smith" };
        _care.UpsertError = new TransientIntegrationException("Care API returned 503");

        await Assert.ThrowsAsync<TransientIntegrationException>(() => Function().Run(Message, CancellationToken.None));

        Assert.Empty(_poison.Sent);
    }

    [Fact]
    public async Task Unexpected_failure_is_rethrown_and_not_poisoned()
    {
        _hris.Error = new InvalidOperationException("boom");

        await Assert.ThrowsAsync<InvalidOperationException>(() => Function().Run(Message, CancellationToken.None));

        Assert.Empty(_poison.Sent);
    }

    [Fact]
    public async Task Correlation_id_from_message_is_ambient_during_vendor_calls()
    {
        _hris.Employee = new Employee { Id = "E-1001", FirstName = "Alice", LastName = "Smith" };
        _hris.OnCall = () => _hris.SeenCorrelationId = _correlation.CorrelationId;

        await Function().Run(Message, CancellationToken.None);

        Assert.Equal("corr-1", _hris.SeenCorrelationId);
        // Set inside the async function, so it doesn't leak back to the caller's flow.
        Assert.Null(_correlation.CorrelationId);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeHris : IHrisClient
    {
        public Employee? Employee { get; set; }
        public Exception? Error { get; set; }
        public Action? OnCall { get; set; }
        public string? SeenCorrelationId { get; set; }

        public Task<Employee?> GetEmployeeAsync(string id, CancellationToken ct)
        {
            OnCall?.Invoke();
            if (Error is not null)
                throw Error;
            return Task.FromResult(Employee);
        }
    }

    private sealed class FakeCare : ICareClient
    {
        public List<CareWorker> Upserts { get; } = new();
        public Exception? UpsertError { get; set; }

        public Task<CareWorker?> GetWorkerAsync(string externalId, CancellationToken ct) =>
            Task.FromResult<CareWorker?>(null);

        public Task UpsertWorkerAsync(CareWorker worker, CancellationToken ct)
        {
            if (UpsertError is not null)
                throw UpsertError;
            Upserts.Add(worker);
            return Task.CompletedTask;
        }
    }

    private sealed class AlwaysClaimStore : IIdempotencyStore
    {
        public Task<ClaimResult> TryClaimAsync(string eventId, DateTimeOffset now, CancellationToken ct) =>
            Task.FromResult(ClaimResult.Claimed);

        public Task MarkCompletedAsync(string eventId, DateTimeOffset now, CancellationToken ct) => Task.CompletedTask;

        public Task MarkFailedAsync(string eventId, string reason, DateTimeOffset now, CancellationToken ct) =>
            Task.CompletedTask;

        public Task ReleaseAsync(string eventId, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakePoisonQueue : IPoisonQueue
    {
        public List<PoisonEnvelope> Sent { get; } = new();

        public Task SendAsync(PoisonEnvelope envelope, CancellationToken ct)
        {
            Sent.Add(envelope);
            return Task.CompletedTask;
        }
    }
}
