using System.Text;
using HrSync.Core.Security;
using HrSync.Functions.Webhooks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HrSync.Functions.Tests;

public class WebhookReceiverTests
{
    private const string Secret = "test-secret";

    private static readonly DateTimeOffset Now = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    private const string HireBody =
        """{"event_id":"evt-1","event_type":"employee.hired","employee_id":"E-1001","occurred_at":"2026-11-02T08:59:00Z"}""";

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static WebhookReceiver Receiver() => new(
        Options.Create(new WebhookOptions { Secret = Secret }),
        new FixedTimeProvider(Now),
        NullLogger<WebhookReceiver>.Instance);

    private static HttpRequest Request(string body, bool sign = true, string? signature = null, string? correlationId = null)
    {
        var context = new DefaultHttpContext();
        var request = context.Request;
        request.Method = "POST";
        request.ContentType = "application/json";
        request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));

        var timestamp = Now.ToUnixTimeSeconds();
        if (sign)
        {
            request.Headers[WebhookReceiver.TimestampHeader] = timestamp.ToString();
            request.Headers[WebhookReceiver.SignatureHeader] = signature ?? WebhookSignature.Compute(Secret, timestamp, body);
        }
        if (correlationId is not null)
            request.Headers[WebhookReceiver.CorrelationHeader] = correlationId;

        return request;
    }

    private static int? StatusOf(IActionResult result) => result switch
    {
        IStatusCodeActionResult r => r.StatusCode,
        _ => null,
    };

    [Fact]
    public async Task Missing_signature_headers_return_401_and_queue_nothing()
    {
        var output = await Receiver().Run(Request(HireBody, sign: false));

        Assert.Equal(401, StatusOf(output.Result));
        Assert.Null(output.Message);
    }

    [Fact]
    public async Task Wrong_signature_returns_401()
    {
        var bad = WebhookSignature.Compute("wrong-secret", Now.ToUnixTimeSeconds(), HireBody);
        var output = await Receiver().Run(Request(HireBody, signature: bad));

        Assert.Equal(401, StatusOf(output.Result));
        Assert.Null(output.Message);
    }

    [Fact]
    public async Task Signature_is_checked_before_payload_is_parsed()
    {
        var output = await Receiver().Run(Request("not json", signature: "sha256=bad"));
        Assert.Equal(401, StatusOf(output.Result));
    }

    [Fact]
    public async Task Signed_but_invalid_payload_returns_400()
    {
        var output = await Receiver().Run(Request("""{"event_id":"evt-1","event_type":"employee.hired"}"""));

        Assert.Equal(400, StatusOf(output.Result));
        Assert.Null(output.Message);
    }

    [Fact]
    public async Task Non_hire_event_returns_204_and_queues_nothing()
    {
        var output = await Receiver().Run(Request(HireBody.Replace("employee.hired", "employee.updated")));

        Assert.Equal(204, StatusOf(output.Result));
        Assert.Null(output.Message);
    }

    [Fact]
    public async Task Hire_event_returns_202_and_queues_message()
    {
        var output = await Receiver().Run(Request(HireBody, correlationId: "corr-123"));

        Assert.Equal(202, StatusOf(output.Result));
        var accepted = Assert.IsType<AcceptedResult>(output.Result);
        Assert.Equal(new WebhookAccepted("corr-123"), accepted.Value);

        Assert.NotNull(output.Message);
        Assert.Equal("evt-1", output.Message.Event.EventId);
        Assert.Equal("E-1001", output.Message.Event.EmployeeId);
        Assert.Equal(new DateTimeOffset(2026, 11, 2, 8, 59, 0, TimeSpan.Zero), output.Message.Event.OccurredAt);
        Assert.Equal(Now, output.Message.ReceivedAt);
    }

    [Fact]
    public async Task Incoming_correlation_id_is_propagated_to_message_and_response_header()
    {
        var request = Request(HireBody, correlationId: "corr-abc");
        var output = await Receiver().Run(request);

        Assert.Equal("corr-abc", output.Message!.CorrelationId);
        Assert.Equal("corr-abc", request.HttpContext.Response.Headers[WebhookReceiver.CorrelationHeader].ToString());
    }

    [Fact]
    public async Task Correlation_id_is_generated_when_missing()
    {
        var request = Request(HireBody);
        var output = await Receiver().Run(request);

        var echoed = request.HttpContext.Response.Headers[WebhookReceiver.CorrelationHeader].ToString();
        Assert.False(string.IsNullOrWhiteSpace(echoed));
        Assert.Equal(echoed, output.Message!.CorrelationId);
        Assert.Equal(new WebhookAccepted(echoed), Assert.IsType<AcceptedResult>(output.Result).Value);
    }

    [Fact]
    public async Task Correlation_id_is_echoed_on_rejections_too()
    {
        var request = Request(HireBody, sign: false, correlationId: "corr-401");
        await Receiver().Run(request);

        Assert.Equal("corr-401", request.HttpContext.Response.Headers[WebhookReceiver.CorrelationHeader].ToString());
    }
}
