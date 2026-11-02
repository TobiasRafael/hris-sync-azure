using System.Diagnostics;
using HrSync.Core.Models;

namespace HrSync.Core.Diagnostics;

// Spans for the sync pipeline. Hosts export them by adding SourceName to their tracer provider.
public static class HrSyncTelemetry
{
    public const string SourceName = "HrSync";
    public const string EventIdTag = "event.id";
    public const string CorrelationIdTag = "correlation.id";

    public static readonly ActivitySource Source = new(SourceName);

    // Root span for one queue message. It continues the webhook's trace (W3C traceparent carried in the
    // message), so webhook -> queue -> processor -> vendor calls read as one trace. `link` points at the
    // trigger's own invocation span, which lives in a separate trace.
    public static Activity? StartProcessing(string name, NewHireMessage message, ActivityContext link)
    {
        ActivityContext.TryParse(message.TraceParent, null, isRemote: true, out var parent);
        var links = link == default ? null : new[] { new ActivityLink(link) };

        return Tag(Source.StartActivity(name, ActivityKind.Consumer, parent, links: links), message);
    }

    // Child span for one pipeline step (claim, get-employee, merge, upsert).
    public static Activity? StartStep(string name, NewHireMessage message) =>
        Tag(Source.StartActivity(name), message);

    public static void RecordError(this Activity? activity, Exception ex)
    {
        if (activity is null)
            return;

        activity.SetStatus(ActivityStatusCode.Error, ex.Message);
        activity.AddException(ex);
    }

    private static Activity? Tag(Activity? activity, NewHireMessage message) =>
        activity?
            .SetTag(EventIdTag, message.Event.EventId)
            .SetTag(CorrelationIdTag, message.CorrelationId);
}
