using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace HrSync.MockApis.Webhooks;

public sealed class WebhookSettings
{
    public const string SectionName = "Webhook";

    public string Url { get; set; } = "http://localhost:7071/api/webhooks/hris";
    public string Secret { get; set; } = "";
}

public sealed record WebhookEvent(string EventId, string EventType, string EmployeeId, DateTimeOffset OccurredAt);

// Outcome of one delivery attempt. StatusCode is null when the receiver couldn't be reached.
public sealed record WebhookDelivery(
    string EventId, string Url, string CorrelationId, bool Tampered, int? StatusCode, string? Error);

// Signs and POSTs HRIS events. Deliberately its own HMAC code (no HrSync.Core reference), same algorithm:
// "sha256=" + lowercase hex HMAC-SHA256(secret, "{unix seconds}.{body}").
public sealed class WebhookSender(HttpClient http, IOptions<WebhookSettings> settings, MockState state)
{
    public const string HireEventType = "employee.hired";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public async Task<WebhookDelivery> SendAsync(WebhookEvent evt, bool tamper, CancellationToken ct)
    {
        var url = settings.Value.Url;
        var correlationId = Guid.NewGuid().ToString();

        var delivery = await DeliverAsync(evt, url, correlationId, tamper, ct);
        state.Log(delivery.StatusCode is { } status
            ? $"WEBHOOK {evt.EventType} {evt.EventId} -> {status}{(tamper ? " (tampered)" : "")}"
            : $"WEBHOOK {evt.EventType} {evt.EventId} -> failed: {delivery.Error}");
        return delivery;
    }

    private async Task<WebhookDelivery> DeliverAsync(
        WebhookEvent evt, string url, string correlationId, bool tamper, CancellationToken ct)
    {
        var secret = settings.Value.Secret;
        if (string.IsNullOrEmpty(secret))
            return new WebhookDelivery(evt.EventId, url, correlationId, tamper, null, "Webhook:Secret is not configured.");

        var body = JsonSerializer.Serialize(evt, Json);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        // Tampering signs with the wrong key, so the receiver must answer 401.
        var signature = Sign(tamper ? secret + "-tampered" : secret, timestamp, body);

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-HrSync-Timestamp", timestamp.ToString(CultureInfo.InvariantCulture));
        request.Headers.Add("X-HrSync-Signature", signature);
        request.Headers.Add("X-Correlation-Id", correlationId);

        try
        {
            using var response = await http.SendAsync(request, ct);
            return new WebhookDelivery(evt.EventId, url, correlationId, tamper, (int)response.StatusCode, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return new WebhookDelivery(evt.EventId, url, correlationId, tamper, null, ex.Message);
        }
    }

    private static string Sign(string secret, long timestamp, string body)
    {
        var hash = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(secret),
            Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture, $"{timestamp}.{body}")));
        return "sha256=" + Convert.ToHexStringLower(hash);
    }
}
