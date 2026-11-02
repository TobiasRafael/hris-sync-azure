using Refit;

namespace HrSync.Adapters.Hris;

// Refit contract for the HRIS vendor API. Non-2xx responses throw ApiException; HrisHttpClient classifies them.
public interface IHrisApi
{
    [Get("/hris/employees/{id}")]
    Task<HrisEmployeeDto> GetEmployeeAsync(string id, CancellationToken ct);
}
