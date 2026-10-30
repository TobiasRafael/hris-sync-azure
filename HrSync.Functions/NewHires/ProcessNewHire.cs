using HrSync.Adapters.Correlation;
using HrSync.Core.Errors;
using HrSync.Core.Models;
using HrSync.Core.Processing;
using HrSync.Functions.Webhooks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace HrSync.Functions.NewHires;

// Thin trigger: sets the correlation context, delegates to NewHireProcessor, and decides the message's fate.
// Permanent failure => envelope on the poison queue, message completed (retrying won't help).
// Anything else => rethrow, so the queue redelivers; the runtime poisons it after maxDequeueCount (host.json).
public sealed class ProcessNewHire(
    NewHireProcessor processor,
    ICorrelationContext correlation,
    IPoisonQueue poison,
    TimeProvider time,
    ILogger<ProcessNewHire> logger)
{
    [Function(nameof(ProcessNewHire))]
    public async Task Run([QueueTrigger(WebhookReceiver.QueueName)] NewHireMessage message, CancellationToken ct)
    {
        var eventId = message.Event.EventId;
        correlation.CorrelationId = message.CorrelationId;
        using var scope = logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = message.CorrelationId,
            ["EventId"] = eventId,
        });

        try
        {
            var result = await processor.ProcessAsync(message, ct);
            logger.LogInformation("New hire {CorrelationId} event {EventId} for employee {EmployeeId}: {Result}",
                message.CorrelationId, eventId, message.Event.EmployeeId, result);
        }
        catch (PermanentIntegrationException ex)
        {
            await poison.SendAsync(new PoisonEnvelope(message, ex.Reason, time.GetUtcNow()), ct);
            logger.LogError(ex, "New hire {CorrelationId} event {EventId} failed permanently, sent to poison: {Reason}",
                message.CorrelationId, eventId, ex.Reason);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "New hire {CorrelationId} event {EventId} failed, will be retried: {Error}",
                message.CorrelationId, eventId, ex.Message);
            throw;
        }
    }
}
