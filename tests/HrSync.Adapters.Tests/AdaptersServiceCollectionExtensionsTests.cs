using System.Net;
using HrSync.Adapters.Correlation;
using HrSync.Adapters.DependencyInjection;
using HrSync.Core.Errors;
using HrSync.Core.Ports;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HrSync.Adapters.Tests;

public class AdaptersServiceCollectionExtensionsTests
{
    private static ServiceProvider Build(StubHttpHandler stub, Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddHrSyncAdapters(configuration);
        services.ConfigureHttpClientDefaults(b => b.ConfigurePrimaryHttpMessageHandler(() => stub));
        return services.BuildServiceProvider();
    }

    private static readonly Dictionary<string, string?> ValidSettings = new()
    {
        ["HrisApi:BaseUrl"] = "http://localhost:5080",
        ["CareApi:BaseUrl"] = "http://localhost:5080/",
    };

    [Fact]
    public async Task Registered_clients_use_configured_base_urls_and_correlation_header()
    {
        var stub = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        using var sp = Build(stub, ValidSettings);
        sp.GetRequiredService<ICorrelationContext>().CorrelationId = "corr-di";

        await sp.GetRequiredService<IHrisClient>().GetEmployeeAsync("E-1001", CancellationToken.None);
        await sp.GetRequiredService<ICareClient>().GetWorkerAsync("E-1001", CancellationToken.None);

        Assert.Equal(
            ["http://localhost:5080/hris/employees/E-1001", "http://localhost:5080/care/workers/E-1001"],
            stub.Requests.Select(r => r.Uri.ToString()));
        Assert.All(stub.Requests, r => Assert.Equal("corr-di", r.CorrelationId));
    }

    [Fact]
    public async Task Resilience_handler_retries_transient_status_honoring_retry_after()
    {
        var calls = 0;
        var stub = new StubHttpHandler(_ => ++calls < 3
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Headers = { { "Retry-After", "0" } } }
            : new HttpResponseMessage(HttpStatusCode.NotFound));
        using var sp = Build(stub, ValidSettings);

        var employee = await sp.GetRequiredService<IHrisClient>().GetEmployeeAsync("E-1001", CancellationToken.None);

        Assert.Null(employee);
        Assert.Equal(3, stub.Requests.Count);
    }

    [Fact]
    public async Task Persistent_transient_status_gives_up_after_three_retries()
    {
        var stub = new StubHttpHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Headers = { { "Retry-After", "0" } } });
        using var sp = Build(stub, ValidSettings);

        await Assert.ThrowsAsync<TransientIntegrationException>(
            () => sp.GetRequiredService<ICareClient>().GetWorkerAsync("E-1001", CancellationToken.None));

        Assert.Equal(4, stub.Requests.Count);
    }

    [Fact]
    public async Task Permanent_status_is_not_retried()
    {
        var stub = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest));
        using var sp = Build(stub, ValidSettings);

        await Assert.ThrowsAsync<PermanentIntegrationException>(
            () => sp.GetRequiredService<IHrisClient>().GetEmployeeAsync("E-1001", CancellationToken.None));

        Assert.Single(stub.Requests);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("/relative/path")]
    public void Missing_or_invalid_base_url_fails(string? baseUrl)
    {
        var stub = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var sp = Build(stub, new() { ["HrisApi:BaseUrl"] = baseUrl, ["CareApi:BaseUrl"] = "http://localhost:5080" });

        var ex = Assert.Throws<OptionsValidationException>(() => sp.GetRequiredService<IHrisClient>());
        Assert.Contains("HrisApi:BaseUrl", ex.Message);
    }
}
