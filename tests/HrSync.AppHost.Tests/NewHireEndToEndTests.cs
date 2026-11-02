using System.Net;
using System.Net.Http.Json;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace HrSync.AppHost.Tests;

// Starts the whole AppHost (Azurite container, MockApis, Functions host) and drives the happy path:
// create employee in the mock HRIS -> signed webhook -> queue -> ProcessNewHire -> worker in the care API.
[Trait("Category", "E2E")]
public class NewHireEndToEndTests
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan SyncTimeout = TimeSpan.FromSeconds(60);

    [E2EFact]
    public async Task Created_employee_appears_as_care_worker()
    {
        using var cts = new CancellationTokenSource(StartupTimeout + SyncTimeout);
        var ct = cts.Token;

        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.HrSync_AppHost>(
            ["Parameters:webhook-secret=e2e-test-secret"], ct);
        await using var app = await builder.BuildAsync(ct);
        await app.StartAsync(ct);

        await app.ResourceNotifications.WaitForResourceHealthyAsync("mockapis", ct);
        await app.ResourceNotifications.WaitForResourceAsync("functions", KnownResourceStates.Running, ct);
        await WaitForReceiverAsync(app.CreateHttpClient("functions"), ct);

        using var mock = app.CreateHttpClient("mockapis");
        var employeeId = $"E-{Random.Shared.Next(90000, 99999)}";
        using var created = await mock.PostAsJsonAsync("/admin/hris/employees", new
        {
            employee_id = employeeId,
            first_name = "Test",
            last_name = "Person",
            work_email = "test.person@example.com",
            job_title = "Caregiver",
            hire_date = "2026-12-01",
        }, ct);
        created.EnsureSuccessStatusCode();
        var delivery = await created.Content.ReadFromJsonAsync<CreatedResponse>(ct);
        Assert.Equal(202, delivery?.webhook.status_code);

        var worker = await PollForWorkerAsync(mock, employeeId, ct);

        Assert.Equal(employeeId, worker.external_id);
        Assert.Equal("Test", worker.given_name);
        Assert.Equal("Person", worker.family_name);
        Assert.Equal("Caregiver", worker.role);
    }

    // "Running" means the process started, not that func is listening yet. An unsigned POST answers 401
    // once the receiver is up; a webhook sent earlier would be lost, because the mock doesn't retry.
    private static async Task WaitForReceiverAsync(HttpClient functions, CancellationToken ct)
    {
        while (true)
        {
            try
            {
                using var probe = await functions.PostAsync("/api/webhooks/hris", new StringContent("{}"), ct);
                if (probe.StatusCode == HttpStatusCode.Unauthorized)
                    return;
            }
            catch (HttpRequestException)
            {
                // Not listening yet.
            }

            await Task.Delay(TimeSpan.FromSeconds(1), ct);
        }
    }

    private static async Task<WorkerResponse> PollForWorkerAsync(HttpClient mock, string employeeId, CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow + SyncTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            using var response = await mock.GetAsync($"/care/workers/{employeeId}", ct);
            if (response.StatusCode == HttpStatusCode.OK)
                return (await response.Content.ReadFromJsonAsync<WorkerResponse>(ct))!;

            await Task.Delay(TimeSpan.FromSeconds(1), ct);
        }

        Assert.Fail($"Worker {employeeId} did not appear in /care/workers within {SyncTimeout.TotalSeconds}s.");
        return null!;
    }

    // Snake_case shapes of the mock's JSON (the tests don't reference the mock's DTOs).
    private sealed record CreatedResponse(Delivery webhook);

    private sealed record Delivery(int? status_code, string? error);

    private sealed record WorkerResponse(string external_id, string given_name, string family_name, string? role);
}
