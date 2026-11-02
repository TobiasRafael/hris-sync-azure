namespace HrSync.Core.Models
{
    // What the webhook receiver puts on the "new-hires" queue. TraceParent is the receiver's W3C trace
    // context, so the queue consumer can continue the same trace; null for messages from older senders.
    public sealed record NewHireMessage(
        NewHireEvent Event, string CorrelationId, DateTimeOffset ReceivedAt, string? TraceParent = null);
}
