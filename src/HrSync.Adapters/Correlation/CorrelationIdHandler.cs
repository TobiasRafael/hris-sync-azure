namespace HrSync.Adapters.Correlation;

// Stamps outgoing vendor calls with the ambient correlation id so both sides' logs can be joined.
public sealed class CorrelationIdHandler(ICorrelationContext correlation) : DelegatingHandler
{
    public const string HeaderName = "X-Correlation-Id";

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var correlationId = correlation.CorrelationId;
        if (!string.IsNullOrWhiteSpace(correlationId) && !request.Headers.Contains(HeaderName))
            request.Headers.Add(HeaderName, correlationId);

        return base.SendAsync(request, ct);
    }
}
