using System.Text.Json;

namespace HrSync.Adapters.Http;

// Both vendors speak snake_case JSON.
internal static class VendorJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };
}
