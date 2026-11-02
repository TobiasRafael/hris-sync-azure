using System.Diagnostics;
using HrSync.Core.Diagnostics;
using HrSync.Core.Models;

namespace HrSync.Core.Tests;

public class HrSyncTelemetryTests : IDisposable
{
    private const string TraceParent = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01";

    private readonly ActivityListener _listener = new()
    {
        ShouldListenTo = s => s.Name == HrSyncTelemetry.SourceName,
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
    };

    public HrSyncTelemetryTests() => ActivitySource.AddActivityListener(_listener);

    public void Dispose() => _listener.Dispose();

    private static NewHireMessage Message(string? traceParent) => new(
        new NewHireEvent { EventId = "evt-1", EmployeeId = "E-1001", OccurredAt = DateTimeOffset.UnixEpoch },
        "corr-1",
        DateTimeOffset.UnixEpoch,
        traceParent);

    [Fact]
    public void Processing_span_continues_the_trace_carried_by_the_message()
    {
        using var span = HrSyncTelemetry.StartProcessing("process", Message(TraceParent), link: default);

        Assert.NotNull(span);
        Assert.Equal("0af7651916cd43dd8448eb211c80319c", span.TraceId.ToHexString());
        Assert.Equal("b7ad6b7169203331", span.ParentSpanId.ToHexString());
        Assert.Equal(ActivityKind.Consumer, span.Kind);
        Assert.Equal("evt-1", span.GetTagItem(HrSyncTelemetry.EventIdTag));
        Assert.Equal("corr-1", span.GetTagItem(HrSyncTelemetry.CorrelationIdTag));
    }

    [Fact]
    public void Processing_span_links_the_trigger_invocation()
    {
        var invocation = new ActivityContext(
            ActivityTraceId.CreateRandom(), ActivitySpanId.CreateRandom(), ActivityTraceFlags.Recorded);

        using var span = HrSyncTelemetry.StartProcessing("process", Message(TraceParent), invocation);

        var link = Assert.Single(span!.Links);
        Assert.Equal(invocation.TraceId, link.Context.TraceId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-traceparent")]
    public void Missing_or_invalid_trace_parent_starts_a_new_trace(string? traceParent)
    {
        using var span = HrSyncTelemetry.StartProcessing("process", Message(traceParent), link: default);

        Assert.NotNull(span);
        Assert.Equal(default, span.ParentSpanId);
    }

    [Fact]
    public void Old_messages_without_trace_parent_still_deserialize()
    {
        var json = """{"Event":{"EventId":"evt-1","EmployeeId":"E-1001","OccurredAt":"2026-11-02T08:00:00+00:00"},"CorrelationId":"corr-1","ReceivedAt":"2026-11-02T08:00:05+00:00"}""";

        var message = System.Text.Json.JsonSerializer.Deserialize<NewHireMessage>(json)!;

        Assert.Equal("corr-1", message.CorrelationId);
        Assert.Null(message.TraceParent);
    }
}
