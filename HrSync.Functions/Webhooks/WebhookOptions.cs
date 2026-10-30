namespace HrSync.Functions.Webhooks;

// Bound from the "Webhook" config section (Webhook__Secret in local.settings.json / app settings).
public sealed class WebhookOptions
{
    public const string SectionName = "Webhook";

    public string Secret { get; set; } = "";
}
