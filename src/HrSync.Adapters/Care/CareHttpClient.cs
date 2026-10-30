using System.Net;
using System.Net.Http.Json;
using HrSync.Adapters.Http;
using HrSync.Core.Models;
using HrSync.Core.Ports;

namespace HrSync.Adapters.Care;

// Typed HttpClient; BaseAddress comes from CareApi:BaseUrl.
public sealed class CareHttpClient(HttpClient http) : ICareClient
{
    private const string SystemName = "Care API";

    public async Task<CareWorker?> GetWorkerAsync(string externalId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(externalId);

        using var request = new HttpRequestMessage(HttpMethod.Get, WorkerPath(externalId));
        using var response = await VendorHttp.SendAsync(http, request, SystemName, allowNotFound: true, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        var dto = await VendorHttp.ReadJsonAsync<CareWorkerDto>(response, SystemName, ct);
        return new CareWorker
        {
            ExternalId = string.IsNullOrWhiteSpace(dto.ExternalId) ? externalId : dto.ExternalId,
            GivenName = dto.GivenName,
            FamilyName = dto.FamilyName,
            Email = dto.Email,
            Phone = dto.Phone,
            Role = dto.Role,
            StartDate = dto.StartDate,
        };
    }

    // PUT is a full replace on the care side, so send the complete (already merged) record.
    public async Task UpsertWorkerAsync(CareWorker worker, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(worker);
        ArgumentException.ThrowIfNullOrWhiteSpace(worker.ExternalId);

        var dto = new CareWorkerDto(
            worker.ExternalId, worker.GivenName, worker.FamilyName, worker.Email, worker.Phone, worker.Role, worker.StartDate);

        using var request = new HttpRequestMessage(HttpMethod.Put, WorkerPath(worker.ExternalId))
        {
            Content = JsonContent.Create(dto, options: VendorJson.Options),
        };
        using var response = await VendorHttp.SendAsync(http, request, SystemName, allowNotFound: false, ct);
    }

    private static string WorkerPath(string externalId) => $"care/workers/{Uri.EscapeDataString(externalId)}";
}
