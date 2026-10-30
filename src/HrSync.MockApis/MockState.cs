using System.Collections.Concurrent;
using HrSync.MockApis.Care;
using HrSync.MockApis.Hris;
using HrSync.MockApis.Webhooks;

namespace HrSync.MockApis;

// In-memory stand-in for both vendor databases, plus a capped log of vendor calls.
public sealed class MockState
{
    private const int MaxLogEntries = 200;

    private readonly ConcurrentQueue<RequestLogEntry> _log = new();

    public ConcurrentDictionary<string, HrisEmployee> Employees { get; } = new();
    public ConcurrentDictionary<string, CareWorkerDto> CareWorkers { get; } = new();

    // Every webhook event sent, keyed by event_id, so it can be resent unchanged.
    public ConcurrentDictionary<string, WebhookEvent> SentEvents { get; } = new();

    public MockState() => Reset();

    public IReadOnlyList<RequestLogEntry> RequestLog => _log.ToArray();

    public void Log(string entry)
    {
        _log.Enqueue(new RequestLogEntry(DateTimeOffset.UtcNow, entry));
        while (_log.Count > MaxLogEntries)
            _log.TryDequeue(out _);
    }

    public void Reset()
    {
        Employees.Clear();
        CareWorkers.Clear();
        SentEvents.Clear();
        _log.Clear();

        Employees["E-1001"] = new HrisEmployee(
            EmployeeId: "E-1001",
            FirstName: "Alice",
            LastName: "Smith",
            WorkEmail: "alice.smith@example.com",
            PersonalEmail: "alice.personal@example.org",
            MobilePhone: "+1-555-0101",
            JobTitle: "Caregiver",
            HireDate: new DateOnly(2026, 11, 2));

        // Sparse on purpose: exercises the "blank never overwrites" merge rule.
        Employees["E-1002"] = new HrisEmployee(
            EmployeeId: "E-1002",
            FirstName: "Bob",
            LastName: "Jones",
            WorkEmail: "",
            PersonalEmail: null,
            MobilePhone: null,
            JobTitle: "Scheduler",
            HireDate: new DateOnly(2026, 11, 9));
    }
}
