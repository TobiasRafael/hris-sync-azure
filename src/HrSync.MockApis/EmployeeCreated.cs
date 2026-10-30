using HrSync.MockApis.Hris;
using HrSync.MockApis.Webhooks;

namespace HrSync.MockApis;

public sealed record EmployeeCreated(HrisEmployee Employee, WebhookDelivery Webhook);
