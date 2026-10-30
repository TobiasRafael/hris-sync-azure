using HrSync.Core.Models;

namespace HrSync.Core.Ports;

public interface ICareClient
{
    // Null when the care system has no worker with this ExternalId.
    Task<CareWorker?> GetWorkerAsync(string externalId, CancellationToken ct);

    // Full replace, keyed by ExternalId: send the already-merged record.
    Task UpsertWorkerAsync(CareWorker worker, CancellationToken ct);
}
