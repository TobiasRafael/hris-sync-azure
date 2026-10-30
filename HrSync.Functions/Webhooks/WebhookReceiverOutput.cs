using HrSync.Core.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace HrSync.Functions.Webhooks;

public sealed class WebhookReceiverOutput
{
    [HttpResult]
    public required IActionResult Result { get; init; }

    // Null for every response except 202, so nothing is queued.
    [QueueOutput(WebhookReceiver.QueueName)]
    public NewHireMessage? Message { get; init; }
}
