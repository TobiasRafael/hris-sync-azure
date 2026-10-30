using System.Net;
using HrSync.Adapters.Correlation;
using HrSync.Adapters.Hris;

namespace HrSync.Adapters.Tests;

public class CorrelationIdHandlerTests
{
    private static (HrisHttpClient Client, StubHttpHandler Stub) Create(ICorrelationContext correlation)
    {
        var stub = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var handler = new CorrelationIdHandler(correlation) { InnerHandler = stub };
        return (new HrisHttpClient(new HttpClient(handler) { BaseAddress = new Uri("http://hris.test/") }), stub);
    }

    [Fact]
    public async Task Ambient_correlation_id_is_sent_as_header()
    {
        var correlation = new CorrelationContext { CorrelationId = "corr-123" };
        var (client, stub) = Create(correlation);

        await client.GetEmployeeAsync("E-1001", CancellationToken.None);

        Assert.Equal("corr-123", Assert.Single(stub.Requests).CorrelationId);
    }

    [Fact]
    public async Task No_header_without_a_correlation_id()
    {
        var (client, stub) = Create(new CorrelationContext { CorrelationId = null });

        await client.GetEmployeeAsync("E-1001", CancellationToken.None);

        Assert.Null(Assert.Single(stub.Requests).CorrelationId);
    }

    [Fact]
    public async Task Context_is_isolated_per_async_flow()
    {
        var correlation = new CorrelationContext();

        async Task<string?> Flow(string id)
        {
            correlation.CorrelationId = id;
            await Task.Yield();
            return correlation.CorrelationId;
        }

        var results = await Task.WhenAll(Flow("a"), Flow("b"), Flow("c"));

        Assert.Equal(new string?[] { "a", "b", "c" }, results);
        Assert.Null(correlation.CorrelationId);
    }
}
