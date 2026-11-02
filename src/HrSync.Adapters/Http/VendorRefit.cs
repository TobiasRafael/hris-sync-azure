using Refit;

namespace HrSync.Adapters.Http;

internal static class VendorRefit
{
    // Snake_case bodies both ways (see VendorJson).
    public static readonly RefitSettings Settings = new(new SystemTextJsonContentSerializer(VendorJson.Options));
}
