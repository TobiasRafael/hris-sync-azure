using System.Text.Json;
using HrSync.MockApis;
using HrSync.MockApis.Care;
using HrSync.MockApis.Faults;
using HrSync.MockApis.Hris;
using HrSync.MockApis.Webhooks;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower);
builder.Services.AddSingleton<MockState>();
builder.Services.AddSingleton<FaultInjector>();
builder.Services.Configure<WebhookSettings>(builder.Configuration.GetSection(WebhookSettings.SectionName));
builder.Services.AddHttpClient<WebhookSender>(c => c.Timeout = TimeSpan.FromSeconds(10));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseFaultInjection();

app.MapHris();
app.MapCare();
app.MapAdmin();

app.Run();
