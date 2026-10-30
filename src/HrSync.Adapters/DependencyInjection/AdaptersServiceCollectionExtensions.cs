using HrSync.Adapters.Care;
using HrSync.Adapters.Correlation;
using HrSync.Adapters.Hris;
using HrSync.Core.Ports;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;

namespace HrSync.Adapters.DependencyInjection;

public static class AdaptersServiceCollectionExtensions
{
    public static IServiceCollection AddHrSyncAdapters(this IServiceCollection services, IConfiguration configuration)
    {
        AddVendorOptions(services, configuration, VendorApiOptions.HrisSection);
        AddVendorOptions(services, configuration, VendorApiOptions.CareSection);

        services.AddSingleton<ICorrelationContext, CorrelationContext>();
        services.AddTransient<CorrelationIdHandler>();

        // Handler order: CorrelationIdHandler (outer) -> resilience (retries, timeouts) -> network.
        services.AddHttpClient<IHrisClient, HrisHttpClient>((sp, http) =>
                http.BaseAddress = BaseAddress(sp, VendorApiOptions.HrisSection))
            .AddHttpMessageHandler<CorrelationIdHandler>()
            .AddStandardResilienceHandler(ConfigureResilience);

        services.AddHttpClient<ICareClient, CareHttpClient>((sp, http) =>
                http.BaseAddress = BaseAddress(sp, VendorApiOptions.CareSection))
            .AddHttpMessageHandler<CorrelationIdHandler>()
            .AddStandardResilienceHandler(ConfigureResilience);

        return services;
    }

    // In-process retries smooth over short blips; anything longer becomes a TransientIntegrationException
    // and the queue redelivers the message later.
    private static void ConfigureResilience(HttpStandardResilienceOptions o)
    {
        o.Retry.MaxRetryAttempts = 3;
        o.Retry.BackoffType = DelayBackoffType.Exponential;
        o.Retry.UseJitter = true;
        o.Retry.ShouldRetryAfterHeader = true;
        o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(10);
        o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(30);
    }

    private static void AddVendorOptions(IServiceCollection services, IConfiguration configuration, string section) =>
        services.AddOptions<VendorApiOptions>(section)
            .Bind(configuration.GetSection(section))
            .Validate(o => Uri.TryCreate(o.BaseUrl, UriKind.Absolute, out _),
                $"{section}:BaseUrl must be an absolute URL ({section}__BaseUrl).")
            .ValidateOnStart();

    // Trailing slash so relative request paths append to the base path instead of replacing its last segment.
    private static Uri BaseAddress(IServiceProvider sp, string section)
    {
        var baseUrl = sp.GetRequiredService<IOptionsMonitor<VendorApiOptions>>().Get(section).BaseUrl;
        return new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/");
    }
}
