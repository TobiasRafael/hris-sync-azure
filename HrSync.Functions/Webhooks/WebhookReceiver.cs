using System.Text;
using HrSync.Adapters.Hris;
using HrSync.Core.Models;
using HrSync.Core.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HrSync.Functions.Webhooks;

// Fast ack: verify, parse, enqueue, return. No outbound calls; the queue consumer does the work.
public sealed class WebhookReceiver(IOptions<WebhookOptions> options, TimeProvider time, ILogger<WebhookReceiver> logger)
{
    public const string QueueName = "new-hires";
    public const string TimestampHeader = "X-HrSync-Timestamp";
    public const string SignatureHeader = "X-HrSync-Signature";
    public const string CorrelationHeader = "X-Correlation-Id";

    [Function(nameof(WebhookReceiver))]
    public async Task<WebhookReceiverOutput> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "webhooks/hris")] HttpRequest req)
    {
        var correlationId = req.Headers[CorrelationHeader].ToString();
        if (string.IsNullOrWhiteSpace(correlationId))
            correlationId = Guid.NewGuid().ToString();
        req.HttpContext.Response.Headers[CorrelationHeader] = correlationId;

        using var scope = logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId });

        // The signature covers the exact bytes sent, so read the raw body before any parsing.
        using var reader = new StreamReader(req.Body, Encoding.UTF8);
        var body = await reader.ReadToEndAsync(req.HttpContext.RequestAborted);

        var now = time.GetUtcNow();
        if (!WebhookSignature.Verify(
                options.Value.Secret, req.Headers[TimestampHeader], req.Headers[SignatureHeader], body, now))
        {
            logger.LogWarning("Rejected HRIS webhook {CorrelationId}: invalid or expired signature", correlationId);
            return new WebhookReceiverOutput { Result = new UnauthorizedResult() };
        }

        var parsed = HrisWebhookParser.Parse(body);
        switch (parsed.Status)
        {
            case WebhookParseStatus.Invalid:
                logger.LogWarning("Rejected HRIS webhook {CorrelationId} event {EventId}: {Error}",
                    correlationId, parsed.EventId, parsed.Error);
                return new WebhookReceiverOutput { Result = new BadRequestObjectResult(new { error = parsed.Error }) };

            case WebhookParseStatus.Ignored:
                logger.LogInformation("Ignored HRIS webhook {CorrelationId} event {EventId} of type {EventType}",
                    correlationId, parsed.EventId, parsed.EventType);
                return new WebhookReceiverOutput { Result = new NoContentResult() };
        }

        var hire = parsed.Event!;
        logger.LogInformation("Queued HRIS webhook {CorrelationId} event {EventId} for employee {EmployeeId}",
            correlationId, hire.EventId, hire.EmployeeId);

        return new WebhookReceiverOutput
        {
            Result = new AcceptedResult((string?)null, new WebhookAccepted(correlationId)),
            Message = new NewHireMessage(hire, correlationId, now),
        };
    }
}
