using System.Net;
using HrSync.Adapters.Http;
using HrSync.Core.Errors;
using HrSync.Core.Models;
using HrSync.Core.Ports;
using Refit;

namespace HrSync.Adapters.Hris;

// IHrisClient over the Refit IHrisApi: 404 => null, other failures classified by VendorErrors.
public sealed class HrisHttpClient(IHrisApi api) : IHrisClient
{
    private const string SystemName = "HRIS";

    public async Task<Employee?> GetEmployeeAsync(string id, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        HrisEmployeeDto? dto;
        try
        {
            dto = await api.GetEmployeeAsync(id, ct);
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
            throw new PermanentIntegrationException($"{SystemName} returned an empty body for employee {id}.");

        return HrisMapper.ToEmployee(string.IsNullOrWhiteSpace(dto.EmployeeId) ? dto with { EmployeeId = id } : dto);
    }
}
