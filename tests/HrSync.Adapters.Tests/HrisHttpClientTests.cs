using System.Net;
using HrSync.Adapters.Hris;
using HrSync.Core.Errors;
using HrSync.Core.Models;

namespace HrSync.Adapters.Tests;

// Clients are built without the resilience handler, so each test sees exactly one attempt.
public class HrisHttpClientTests
{
    private static (HrisHttpClient Client, StubHttpHandler Stub) Create(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var stub = new StubHttpHandler(respond);
        var http = new HttpClient(stub) { BaseAddress = new Uri("http://hris.test/") };
        return (new HrisHttpClient(http), stub);
    }

    [Fact]
    public async Task Get_maps_snake_case_employee()
    {
        var (client, stub) = Create(_ => StubHttpHandler.Json(HttpStatusCode.OK, """
            {
              "employee_id": "E-1001",
              "first_name": "Alice",
              "last_name": "Smith",
              "work_email": "alice@example.com",
              "personal_email": "alice@home.test",
              "mobile_phone": "+1-555-0101",
              "job_title": "Caregiver",
              "hire_date": "2026-11-02"
            }
            """));

        var employee = await client.GetEmployeeAsync("E-1001", CancellationToken.None);

        Assert.Equal(new Employee
        {
            Id = "E-1001",
            FirstName = "Alice",
            LastName = "Smith",
            WorkEmail = "alice@example.com",
            PersonalEmail = "alice@home.test",
            MobilePhone = "+1-555-0101",
            JobTitle = "Caregiver",
            HireDate = new DateOnly(2026, 11, 2),
        }, employee);

        var request = Assert.Single(stub.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("http://hris.test/hris/employees/E-1001", request.Uri.ToString());
    }

    [Fact]
    public async Task Get_keeps_nulls_from_sparse_record()
    {
        var (client, _) = Create(_ => StubHttpHandler.Json(HttpStatusCode.OK,
            """{"employee_id":"E-1002","first_name":"Bob","last_name":"Jones","work_email":"","personal_email":null,"mobile_phone":null,"job_title":"Scheduler","hire_date":null}"""));

        var employee = await client.GetEmployeeAsync("E-1002", CancellationToken.None);

        Assert.NotNull(employee);
        Assert.Equal("", employee.WorkEmail);
        Assert.Null(employee.MobilePhone);
        Assert.Null(employee.HireDate);
    }

    [Fact]
    public async Task Id_is_url_escaped()
    {
        var (client, stub) = Create(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        await client.GetEmployeeAsync("E/1 2", CancellationToken.None);

        Assert.Equal("/hris/employees/E%2F1%202", Assert.Single(stub.Requests).Uri.AbsolutePath);
    }

    [Fact]
    public async Task Not_found_returns_null()
    {
        var (client, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        Assert.Null(await client.GetEmployeeAsync("E-9999", CancellationToken.None));
    }

    [Fact]
    public async Task Bad_request_is_permanent_with_status_and_body_excerpt()
    {
        var (client, _) = Create(_ => StubHttpHandler.Json(HttpStatusCode.BadRequest, """{"error":"bad id format"}"""));

        var ex = await Assert.ThrowsAsync<PermanentIntegrationException>(
            () => client.GetEmployeeAsync("E-1001", CancellationToken.None));

        Assert.Contains("400", ex.Reason);
        Assert.Contains("bad id format", ex.Reason);
    }

    [Fact]
    public async Task Long_error_body_is_truncated()
    {
        var (client, _) = Create(_ => StubHttpHandler.Json(HttpStatusCode.BadRequest, new string('x', 5000)));

        var ex = await Assert.ThrowsAsync<PermanentIntegrationException>(
            () => client.GetEmployeeAsync("E-1001", CancellationToken.None));

        Assert.True(ex.Reason.Length < 1000, $"reason was {ex.Reason.Length} chars");
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public async Task Client_errors_are_permanent(HttpStatusCode status)
    {
        var (client, _) = Create(_ => new HttpResponseMessage(status));
        await Assert.ThrowsAsync<PermanentIntegrationException>(
            () => client.GetEmployeeAsync("E-1001", CancellationToken.None));
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task Server_errors_408_and_429_are_transient(HttpStatusCode status)
    {
        var (client, _) = Create(_ => new HttpResponseMessage(status));

        var ex = await Assert.ThrowsAsync<TransientIntegrationException>(
            () => client.GetEmployeeAsync("E-1001", CancellationToken.None));

        Assert.Contains(((int)status).ToString(), ex.Message);
    }

    [Fact]
    public async Task Network_error_is_transient()
    {
        var (client, _) = Create(_ => throw new HttpRequestException("connection refused"));

        var ex = await Assert.ThrowsAsync<TransientIntegrationException>(
            () => client.GetEmployeeAsync("E-1001", CancellationToken.None));

        Assert.IsType<HttpRequestException>(ex.InnerException);
    }

    [Fact]
    public async Task Timeout_is_transient()
    {
        var (client, _) = Create(_ => throw new TaskCanceledException("timed out"));

        await Assert.ThrowsAsync<TransientIntegrationException>(
            () => client.GetEmployeeAsync("E-1001", CancellationToken.None));
    }

    [Fact]
    public async Task Caller_cancellation_is_not_wrapped()
    {
        using var cts = new CancellationTokenSource();
        var (client, _) = Create(_ =>
        {
            cts.Cancel();
            throw new TaskCanceledException();
        });

        var ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.GetEmployeeAsync("E-1001", cts.Token));

        Assert.IsNotType<TransientIntegrationException>(ex);
    }

    [Fact]
    public async Task Malformed_success_body_is_permanent()
    {
        var (client, _) = Create(_ => StubHttpHandler.Json(HttpStatusCode.OK, "{ not json"));

        await Assert.ThrowsAsync<PermanentIntegrationException>(
            () => client.GetEmployeeAsync("E-1001", CancellationToken.None));
    }
}
