using System.Net;
using System.Text.Json;
using HrSync.Adapters.Care;
using HrSync.Core.Errors;
using HrSync.Core.Models;

namespace HrSync.Adapters.Tests;

// Clients are built without the resilience handler, so each test sees exactly one attempt.
public class CareHttpClientTests
{
    private static readonly CareWorker Alice = new()
    {
        ExternalId = "E-1001",
        GivenName = "Alice",
        FamilyName = "Smith",
        Email = "alice@example.com",
        Phone = null,
        Role = "Caregiver",
        StartDate = new DateOnly(2026, 11, 2),
    };

    private static (CareHttpClient Client, StubHttpHandler Stub) Create(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var stub = new StubHttpHandler(respond);
        var http = new HttpClient(stub) { BaseAddress = new Uri("http://care.test/") };
        return (new CareHttpClient(http), stub);
    }

    [Fact]
    public async Task Get_maps_snake_case_worker()
    {
        var (client, stub) = Create(_ => StubHttpHandler.Json(HttpStatusCode.OK,
            """{"external_id":"E-1001","given_name":"Alice","family_name":"Smith","email":"alice@example.com","phone":null,"role":"Caregiver","start_date":"2026-11-02"}"""));

        var worker = await client.GetWorkerAsync("E-1001", CancellationToken.None);

        Assert.Equal(Alice, worker);
        Assert.Equal("http://care.test/care/workers/E-1001", Assert.Single(stub.Requests).Uri.ToString());
    }

    [Fact]
    public async Task Get_not_found_returns_null()
    {
        var (client, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        Assert.Null(await client.GetWorkerAsync("E-1001", CancellationToken.None));
    }

    [Fact]
    public async Task Upsert_puts_full_snake_case_record()
    {
        var (client, stub) = Create(_ => new HttpResponseMessage(HttpStatusCode.Created));

        await client.UpsertWorkerAsync(Alice, CancellationToken.None);

        var request = Assert.Single(stub.Requests);
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal("http://care.test/care/workers/E-1001", request.Uri.ToString());

        using var body = JsonDocument.Parse(request.Body!);
        var root = body.RootElement;
        Assert.Equal("E-1001", root.GetProperty("external_id").GetString());
        Assert.Equal("Alice", root.GetProperty("given_name").GetString());
        Assert.Equal("Smith", root.GetProperty("family_name").GetString());
        Assert.Equal("alice@example.com", root.GetProperty("email").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("phone").ValueKind);
        Assert.Equal("Caregiver", root.GetProperty("role").GetString());
        Assert.Equal("2026-11-02", root.GetProperty("start_date").GetString());
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.Created)]
    public async Task Upsert_accepts_created_and_updated(HttpStatusCode status)
    {
        var (client, _) = Create(_ => new HttpResponseMessage(status));
        await client.UpsertWorkerAsync(Alice, CancellationToken.None);
    }

    [Fact]
    public async Task Upsert_validation_error_is_permanent_with_details()
    {
        var (client, _) = Create(_ => StubHttpHandler.Json(HttpStatusCode.BadRequest,
            """{"title":"One or more validation errors occurred.","errors":{"family_name":["family_name is required."]}}"""));

        var ex = await Assert.ThrowsAsync<PermanentIntegrationException>(
            () => client.UpsertWorkerAsync(Alice with { FamilyName = "" }, CancellationToken.None));

        Assert.Contains("400", ex.Reason);
        Assert.Contains("family_name is required", ex.Reason);
    }

    [Fact]
    public async Task Upsert_503_is_transient()
    {
        var (client, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        await Assert.ThrowsAsync<TransientIntegrationException>(
            () => client.UpsertWorkerAsync(Alice, CancellationToken.None));
    }

    [Fact]
    public async Task Upsert_without_external_id_is_rejected_before_any_call()
    {
        var (client, stub) = Create(_ => new HttpResponseMessage(HttpStatusCode.OK));

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => client.UpsertWorkerAsync(Alice with { ExternalId = null }, CancellationToken.None));

        Assert.Empty(stub.Requests);
    }
}
