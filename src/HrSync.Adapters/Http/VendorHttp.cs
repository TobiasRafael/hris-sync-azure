using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HrSync.Core.Errors;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace HrSync.Adapters.Http;

// Turns HTTP outcomes into the Core error model:
// 4xx (except 408/429) => Permanent; 5xx, 408, 429, network errors, timeouts, open circuit => Transient.
internal static class VendorHttp
{
    private const int BodyExcerptLength = 500;

    // Sends the request. Returns the response for 2xx, and for 404 when allowNotFound; otherwise throws.
    public static async Task<HttpResponseMessage> SendAsync(
        HttpClient http, HttpRequestMessage request, string system, bool allowNotFound, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct);
        }
        catch (Exception ex) when (IsTransientFailure(ex, ct))
        {
            throw new TransientIntegrationException(
                $"{system} {request.Method} {request.RequestUri} failed: {ex.Message}", ex);
        }

        if (response.IsSuccessStatusCode || (allowNotFound && response.StatusCode == HttpStatusCode.NotFound))
            return response;

        using (response)
        {
            var status = (int)response.StatusCode;
            var excerpt = await ReadExcerptAsync(response, ct);
            var detail = $"{system} {request.Method} {request.RequestUri} returned {status}: {excerpt}";

            if (IsTransientStatus(response.StatusCode))
                throw new TransientIntegrationException(detail);
            throw new PermanentIntegrationException(detail);
        }
    }

    public static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response, string system, CancellationToken ct)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(VendorJson.Options, ct)
                ?? throw new PermanentIntegrationException($"{system} returned an empty body.");
        }
        catch (JsonException ex)
        {
            throw new PermanentIntegrationException($"{system} returned malformed JSON: {ex.Message}", ex);
        }
    }

    public static bool IsTransientStatus(HttpStatusCode status) =>
        status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || (int)status >= 500;

    // A TaskCanceledException that isn't the caller's own cancellation is HttpClient's timeout.
    private static bool IsTransientFailure(Exception ex, CancellationToken ct) =>
        ex is HttpRequestException or TimeoutRejectedException or BrokenCircuitException
        || (ex is TaskCanceledException && !ct.IsCancellationRequested);

    private static async Task<string> ReadExcerptAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            if (string.IsNullOrWhiteSpace(body))
                return "(empty body)";
            return body.Length <= BodyExcerptLength ? body : body[..BodyExcerptLength] + "...";
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return "(body unreadable)";
        }
    }
}
