namespace HrSync.MockApis.Faults;

public sealed record FaultError(string Error, string Target, int Status);

// Applies configured faults to /hris and /care only, and logs every vendor call.
public sealed class FaultInjectionMiddleware(RequestDelegate next, FaultInjector faults, MockState state)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var target = GetTarget(context.Request.Path);
        if (target is null)
        {
            await next(context);
            return;
        }

        var decision = faults.Evaluate(target);
        if (decision.DelayMs > 0)
            await Task.Delay(decision.DelayMs, context.RequestAborted);

        var line = $"{context.Request.Method} {context.Request.Path}";

        if (decision.Failure is { } rule)
        {
            context.Response.StatusCode = rule.StatusCode;
            if (rule.RetryAfterSeconds is { } seconds)
                context.Response.Headers.RetryAfter = seconds.ToString();
            await context.Response.WriteAsJsonAsync(new FaultError("injected_fault", target, rule.StatusCode));
            state.Log($"{line} -> {rule.StatusCode} (injected)");
            return;
        }

        await next(context);
        state.Log($"{line} -> {context.Response.StatusCode}");
    }

    private static string? GetTarget(PathString path) =>
        path.StartsWithSegments("/hris") ? "hris"
        : path.StartsWithSegments("/care") ? "care"
        : null;
}

public static class FaultInjectionExtensions
{
    public static IApplicationBuilder UseFaultInjection(this IApplicationBuilder app) =>
        app.UseMiddleware<FaultInjectionMiddleware>();
}
