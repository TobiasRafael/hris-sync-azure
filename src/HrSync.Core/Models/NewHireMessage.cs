namespace HrSync.Core.Models
{
    // What the webhook receiver puts on the "new-hires" queue.
    public sealed record NewHireMessage(NewHireEvent Event, string CorrelationId, DateTimeOffset ReceivedAt);
}
