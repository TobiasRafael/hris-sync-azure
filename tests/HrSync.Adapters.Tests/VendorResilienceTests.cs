using System.Diagnostics;
using HrSync.Adapters.Correlation;
using HrSync.Adapters.DependencyInjection;
using HrSync.Core.Errors;
using HrSync.Core.Models;
using HrSync.Core.Ports;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WireMock.Server;
using Request = WireMock.RequestBuilders.Request;
using Response = WireMock.ResponseBuilders.Response;

namespace HrSync.Adapters.Tests;

// The real pipeline over real HTTP: Refit -> CorrelationIdHandler -> standard resilience handler -> WireMock.
// The base retry delay is shortened to 50 ms so the tests are fast and a Retry-After wait stands out.
public sealed class VendorResilienceTests : IDisposable
{
    private const string EmployeePath = "/hris/employees/E-1001";
    private const string WorkerPath = "/care/workers/E-1001";

    private readonly WireMockServer _server = WireMockServer.Start();
    private readonly ServiceProvider _services;

    public VendorResilienceTests()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["HrisApi:BaseUrl"] = _server.Url!,
            ["CareApi:BaseUrl"] = _server.Url!,
        }).Build();

        _services = new ServiceCollection()
            .AddHrSyncAdapters(configuration, o => o.Retry.Delay = TimeSpan.FromMilliseconds(50))
            .BuildServiceProvider();
    }

    public void Dispose()
    {
        _services.Dispose();
        _server.Stop();
        _server.Dispose();
    }

    private IHrisClient Hris => _services.GetRequiredService<IHrisClient>();
    private ICareClient Care => _services.GetRequiredService<ICareClient>();

    private int Calls(string path) => _server.LogEntries.Count(e => e.RequestMessage!.Path == path);

    private const string AliceJson =
        """{"employee_id":"E-1001","first_name":"Alice","last_name":"Smith","job_title":"Caregiver","hire_date":"2026-11-02"}""";

    // Responds with each status in turn (one per request), then keeps answering with the last one.
    private void Sequence(string path, string method, params (int Status, string? Body, string? RetryAfter)[] steps)
    {
        for (var i = 0; i < steps.Length; i++)
        {
            var response = Response.Create().WithStatusCode(steps[i].Status);
            if (steps[i].Body is { } body)
                response = response.WithHeader("Content-Type", "application/json").WithBody(body);
            if (steps[i].RetryAfter is { } retryAfter)
                response = response.WithHeader("Retry-After", retryAfter);

            var rule = _server.Given(Request.Create().WithPath(path).UsingMethod(method)).InScenario(path);
            rule = i == 0 ? rule : rule.WhenStateIs($"step{i}");
            if (i < steps.Length - 1)
                rule = rule.WillSetStateTo($"step{i + 1}");
            rule.RespondWith(response);
        }
    }

    [Fact]
    public async Task Host_wide_default_resilience_does_not_stack_on_vendor_retries()
    {
        // Aspire ServiceDefaults adds a standard resilience handler to every HttpClient. If it also wrapped
        // the vendor clients, each of our 4 attempts would be retried 4 times (16 calls).
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["HrisApi:BaseUrl"] = _server.Url!,
            ["CareApi:BaseUrl"] = _server.Url!,
        }).Build();
        var services = new ServiceCollection();
        services.ConfigureHttpClientDefaults(http =>
            http.AddStandardResilienceHandler(o => o.Retry.Delay = TimeSpan.FromMilliseconds(10)));
        await using var provider = services
            .AddHrSyncAdapters(configuration, o => o.Retry.Delay = TimeSpan.FromMilliseconds(10))
            .BuildServiceProvider();
        Sequence(EmployeePath, "GET", (503, null, null));

        await Assert.ThrowsAsync<TransientIntegrationException>(
            () => provider.GetRequiredService<IHrisClient>().GetEmployeeAsync("E-1001", CancellationToken.None));

        Assert.Equal(4, Calls(EmployeePath));
    }

    [Fact]
    public async Task Two_503s_then_200_succeeds_after_retries()
    {
        Sequence(EmployeePath, "GET", (503, null, null), (503, null, null), (200, AliceJson, null));

        var employee = await Hris.GetEmployeeAsync("E-1001", CancellationToken.None);

        Assert.Equal("Alice", employee?.FirstName);
        Assert.Equal(new DateOnly(2026, 11, 2), employee?.HireDate);
        Assert.Equal(3, Calls(EmployeePath));
    }

    [Fact]
    public async Task Retry_after_header_is_honored()
    {
        Sequence(EmployeePath, "GET", (429, null, "2"), (200, AliceJson, null));

        var watch = Stopwatch.StartNew();
        var employee = await Hris.GetEmployeeAsync("E-1001", CancellationToken.None);
        watch.Stop();

        Assert.NotNull(employee);
        Assert.Equal(2, Calls(EmployeePath));
        // Base delay is 50 ms; only Retry-After explains a ~2 s wait.
        Assert.True(watch.Elapsed >= TimeSpan.FromSeconds(1.9), $"elapsed {watch.Elapsed}");
    }

    [Fact]
    public async Task Bad_request_is_permanent_and_not_retried()
    {
        Sequence(WorkerPath, "PUT", (400, """{"errors":{"family_name":["family_name is required."]}}""", null));

        var ex = await Assert.ThrowsAsync<PermanentIntegrationException>(() => Care.UpsertWorkerAsync(
            new CareWorker { ExternalId = "E-1001", GivenName = "Alice", FamilyName = "" }, CancellationToken.None));

        Assert.Contains("400", ex.Reason);
        Assert.Contains("family_name is required", ex.Reason);
        Assert.Equal(1, Calls(WorkerPath));
    }

    [Fact]
    public async Task Persistent_503_gives_up_after_three_retries_as_transient()
    {
        Sequence(WorkerPath, "GET", (503, null, null));

        await Assert.ThrowsAsync<TransientIntegrationException>(
            () => Care.GetWorkerAsync("E-1001", CancellationToken.None));

        Assert.Equal(4, Calls(WorkerPath));
    }

    [Fact]
    public async Task Not_found_returns_null_without_retries()
    {
        Sequence(WorkerPath, "GET", (404, null, null));

        Assert.Null(await Care.GetWorkerAsync("E-1001", CancellationToken.None));
        Assert.Equal(1, Calls(WorkerPath));
    }

    [Fact]
    public async Task Put_sends_snake_case_body_and_correlation_header()
    {
        Sequence(WorkerPath, "PUT", (201, null, null));
        _services.GetRequiredService<ICorrelationContext>().CorrelationId = "corr-wire";

        await Care.UpsertWorkerAsync(new CareWorker
        {
            ExternalId = "E-1001",
            GivenName = "Alice",
            FamilyName = "Smith",
            StartDate = new DateOnly(2026, 11, 2),
        }, CancellationToken.None);

        var request = Assert.Single(_server.LogEntries).RequestMessage!;
        Assert.Contains("\"given_name\":\"Alice\"", request.Body);
        Assert.Contains("\"start_date\":\"2026-11-02\"", request.Body);
        var header = request.Headers!.First(h => h.Key.Equals("X-Correlation-Id", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("corr-wire", header.Value.Single());
    }
}
