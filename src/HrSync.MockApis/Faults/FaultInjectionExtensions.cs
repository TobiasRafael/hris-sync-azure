namespace HrSync.MockApis.Faults;

public static class FaultInjectionExtensions
{
    public static IApplicationBuilder UseFaultInjection(this IApplicationBuilder app) =>
        app.UseMiddleware<FaultInjectionMiddleware>();
}
