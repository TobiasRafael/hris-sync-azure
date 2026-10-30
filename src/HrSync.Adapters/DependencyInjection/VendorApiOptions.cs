namespace HrSync.Adapters.DependencyInjection;

// Bound from HrisApi / CareApi config sections (HrisApi__BaseUrl, CareApi__BaseUrl).
public sealed class VendorApiOptions
{
    public const string HrisSection = "HrisApi";
    public const string CareSection = "CareApi";

    public string BaseUrl { get; set; } = "";
}
