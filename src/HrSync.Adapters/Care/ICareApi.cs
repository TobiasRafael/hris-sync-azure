using Refit;

namespace HrSync.Adapters.Care;

// Refit contract for the care vendor API. Non-2xx responses throw ApiException; CareHttpClient classifies them.
public interface ICareApi
{
    [Get("/care/workers/{externalId}")]
    Task<CareWorkerDto> GetWorkerAsync(string externalId, CancellationToken ct);

    // Full-replace upsert: 201 created, 200 updated.
    [Put("/care/workers/{externalId}")]
    Task PutWorkerAsync(string externalId, [Body] CareWorkerDto worker, CancellationToken ct);
}
