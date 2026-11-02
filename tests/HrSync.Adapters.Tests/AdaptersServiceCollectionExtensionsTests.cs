using System.Net;
using HrSync.Adapters.Correlation;
using HrSync.Adapters.DependencyInjection;
using HrSync.Core.Ports;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WireMock.Server;
using Request = WireMock.RequestBuilders.Request;
using Response = WireMock.ResponseBuilders.Response;

namespace HrSync.Adapters.Tests;

// DI wiring and config validation. Retry behaviour over real HTTP is in VendorResilienceTests.
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

    // Over real HTTP: Refit's generated clients don't pick up ConfigureHttpClientDefaults, so a stub can't be injected.
    [Fact]
    public async Task Registered_clients_use_configured_base_urls_and_correlation_header()
    {
        using var server = WireMockServer.Start();
        server.Given(Request.Create().UsingGet()).RespondWith(Response.Create().WithStatusCode(404));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["HrisApi:BaseUrl"] = server.Url,
            ["CareApi:BaseUrl"] = server.Url + "/",
        }).Build();
        using var sp = new ServiceCollection().AddHrSyncAdapters(configuration).BuildServiceProvider();
        sp.GetRequiredService<ICorrelationContext>().CorrelationId = "corr-di";

        await sp.GetRequiredService<IHrisClient>().GetEmployeeAsync("E-1001", CancellationToken.None);
        await sp.GetRequiredService<ICareClient>().GetWorkerAsync("E-1001", CancellationToken.None);

        var requests = server.LogEntries.Select(e => e.RequestMessage!).ToList();
        Assert.Equal(["/hris/employees/E-1001", "/care/workers/E-1001"], requests.Select(r => r.Path));
        Assert.All(requests, r => Assert.Equal("corr-di",
            r.Headers!.First(h => h.Key.Equals("X-Correlation-Id", StringComparison.OrdinalIgnoreCase)).Value.Single()));
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
