using System.Net;
using HrSync.Adapters.Http;
using HrSync.Core.Errors;
using HrSync.Core.Models;
using HrSync.Core.Ports;
using Refit;

namespace HrSync.Adapters.Care;

// ICareClient over the Refit ICareApi: 404 on GET => null, other failures classified by VendorErrors.
public sealed class CareHttpClient(ICareApi api) : ICareClient
{
    private const string SystemName = "Care API";

    public async Task<CareWorker?> GetWorkerAsync(string externalId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(externalId);

        CareWorkerDto? dto;
        try
        {
            dto = await api.GetWorkerAsync(externalId, ct);
        }
        catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (Exception ex) when (VendorErrors.IsVendorFailure(ex, ct))
        {
            throw VendorErrors.ToIntegrationException(ex, SystemName);
        }

        if (dto is null)
            throw new PermanentIntegrationException($"{SystemName} returned an empty body for worker {externalId}.");

        return CareMapper.ToCareWorker(string.IsNullOrWhiteSpace(dto.ExternalId) ? dto with { ExternalId = externalId } : dto);
    }

    // PUT is a full replace on the care side, so send the complete (already merged) record.
    public async Task UpsertWorkerAsync(CareWorker worker, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(worker);
        ArgumentException.ThrowIfNullOrWhiteSpace(worker.ExternalId);

        try
        {
            await api.PutWorkerAsync(worker.ExternalId, CareMapper.ToDto(worker), ct);
        }
        catch (Exception ex) when (VendorErrors.IsVendorFailure(ex, ct))
        {
            throw VendorErrors.ToIntegrationException(ex, SystemName);
        }
    }
}
