namespace HrSync.MockApis.Webhooks;

public sealed class WebhookSettings
{
    public const string SectionName = "Webhook";

    public string Url { get; set; } = "http://localhost:7071/api/webhooks/hris";
    public string Secret { get; set; } = "";
}
