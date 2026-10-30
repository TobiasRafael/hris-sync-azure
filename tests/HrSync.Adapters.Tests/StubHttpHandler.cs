using System.Net;
using System.Text;

namespace HrSync.Adapters.Tests;

// Records every request (with its body read eagerly) and answers with the given function.
internal sealed class StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public sealed record Recorded(HttpMethod Method, Uri Uri, string? CorrelationId, string? Body);

    public List<Recorded> Requests { get; } = new();

    public static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
        var correlationId = request.Headers.TryGetValues("X-Correlation-Id", out var values) ? values.Single() : null;
        Requests.Add(new Recorded(request.Method, request.RequestUri!, correlationId, body));
        return respond(request);
    }
}
