using System.Net;
using System.Text.Json;
using HrSync.Core.Errors;
using Polly.CircuitBreaker;
using Polly.Timeout;
using Refit;

namespace HrSync.Adapters.Http;

// Turns failures of a vendor call into the Core error model:
// 4xx (except 408/429) => Permanent; 5xx, 408, 429, network errors, timeouts, open circuit => Transient;
// a 2xx whose body can't be read => Permanent (retrying returns the same body).
internal static class VendorErrors
{
    private const int BodyExcerptLength = 500;

    public static bool IsVendorFailure(Exception ex, CancellationToken ct) =>
        ex is ApiException or JsonException or HttpRequestException or TimeoutRejectedException or BrokenCircuitException
        // Refit wraps send failures (connection refused, timeouts, Polly rejections) in ApiRequestException.
        // Either way, a cancellation that isn't the caller's own is a timeout.
        || (ex is ApiRequestException or TaskCanceledException && !ct.IsCancellationRequested);

    public static Exception ToIntegrationException(Exception ex, string system) => ex switch
    {
        ApiRequestException req =>
            new TransientIntegrationException($"{system} call failed: {req.InnerException?.Message ?? req.Message}", ex),
        ApiException api when IsSuccess(api.StatusCode) =>
            new PermanentIntegrationException($"{system} {api.HttpMethod} {api.Uri} returned an unreadable body: {Inner(api)}", ex),
        ApiException api => FromStatus(api, system),
        JsonException => new PermanentIntegrationException($"{system} returned malformed JSON: {ex.Message}", ex),
        _ => new TransientIntegrationException($"{system} call failed: {ex.Message}", ex),
    };

    public static bool IsTransientStatus(HttpStatusCode status) =>
        status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || (int)status >= 500;

    private static Exception FromStatus(ApiException api, string system)
    {
        var detail = $"{system} {api.HttpMethod} {api.Uri} returned {(int)api.StatusCode}: {Excerpt(api.Content)}";
        return IsTransientStatus(api.StatusCode)
            ? new TransientIntegrationException(detail, api)
            : new PermanentIntegrationException(detail, api);
    }

    private static bool IsSuccess(HttpStatusCode status) => (int)status is >= 200 and < 300;

    private static string Inner(ApiException api) => api.InnerException?.Message ?? api.Message;

    private static string Excerpt(string? body) =>
        string.IsNullOrWhiteSpace(body) ? "(empty body)"
        : body.Length <= BodyExcerptLength ? body
        : body[..BodyExcerptLength] + "...";
}
