using HrSync.Core.Models;

namespace HrSync.Core.Tests;

public class NewHireMessageTests
{
    [Fact]
    public void Carries_event_correlation_id_and_receipt_time()
    {
        var hire = new NewHireEvent
        {
            EventId = "evt-1",
            EmployeeId = "E-1001",
            OccurredAt = new DateTimeOffset(2026, 11, 2, 8, 0, 0, TimeSpan.Zero),
        };
        var receivedAt = new DateTimeOffset(2026, 11, 2, 8, 0, 5, TimeSpan.Zero);

        var message = new NewHireMessage(hire, "corr-1", receivedAt);

        Assert.Same(hire, message.Event);
        Assert.Equal("corr-1", message.CorrelationId);
        Assert.Equal(receivedAt, message.ReceivedAt);
        Assert.Equal(message, new NewHireMessage(hire with { }, "corr-1", receivedAt));
    }
}
