using HrSync.Core.Diagnostics;
using HrSync.Core.Enums;
using HrSync.Core.Errors;
using HrSync.Core.Mapping;
using HrSync.Core.Models;
using HrSync.Core.Ports;
using HrSync.Core.Rules;

namespace HrSync.Core.Processing;

// claim -> fetch employee -> map -> merge onto existing worker -> upsert -> mark completed.
// Permanent failures are recorded and rethrown; anything else releases the claim and is rethrown for retry.
public sealed class NewHireProcessor(
    IHrisClient hris, ICareClient care, IIdempotencyStore idempotency, FieldMapper mapper, TimeProvider time)
{
    public async Task<ProcessResult> ProcessAsync(NewHireMessage message, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);
        var eventId = message.Event.EventId;

        var claim = await StepAsync("claim", message, () => idempotency.TryClaimAsync(eventId, time.GetUtcNow(), ct));
        switch (claim)
        {
            case ClaimResult.AlreadyCompleted:
                return ProcessResult.Skipped;
            case ClaimResult.InProgress:
                throw new TransientIntegrationException($"Event {eventId} is already being processed.");
        }

        try
        {
            var employeeId = message.Event.EmployeeId;
            var employee = await StepAsync("get-employee", message, async () =>
                await hris.GetEmployeeAsync(employeeId, ct)
                ?? throw new PermanentIntegrationException($"Employee {employeeId} not found in HRIS."));

            var worker = await StepAsync("merge", message, async () =>
            {
                var incoming = mapper.Map(employee);
                var existing = await care.GetWorkerAsync(incoming.ExternalId!, ct);
                return existing is null ? incoming : CareWorkerMerge.Apply(existing, incoming);
            });

            await StepAsync("upsert", message, async () =>
            {
                await care.UpsertWorkerAsync(worker, ct);
                return true;
            });
            await idempotency.MarkCompletedAsync(eventId, time.GetUtcNow(), ct);
            return ProcessResult.Synced;
        }
        catch (PermanentIntegrationException ex)
        {
            await idempotency.MarkFailedAsync(eventId, ex.Reason, time.GetUtcNow(), ct);
            throw;
        }
        catch
        {
            await ReleaseQuietlyAsync(eventId);
            throw;
        }
    }

    // Runs one pipeline step inside its own span; a failure marks the span as an error and propagates.
    private static async Task<T> StepAsync<T>(string name, NewHireMessage message, Func<Task<T>> step)
    {
        using var span = HrSyncTelemetry.StartStep(name, message);
        try
        {
            return await step();
        }
        catch (Exception ex)
        {
            span.RecordError(ex);
            throw;
        }
    }

    // Best effort: if this fails too, the claim goes stale and is taken over later.
    // Uses CancellationToken.None so a shutdown-triggered cancellation still releases.
    private async Task ReleaseQuietlyAsync(string eventId)
    {
        try
        {
            await idempotency.ReleaseAsync(eventId, CancellationToken.None);
        }
        catch
        {
            // Keep the original exception.
        }
    }
}
