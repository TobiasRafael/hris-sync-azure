using HrSync.MockApis.Faults;
using HrSync.MockApis.Hris;
using Microsoft.AspNetCore.Http.HttpResults;

namespace HrSync.MockApis;

public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdmin(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/admin").WithTags("Admin");

        admin.MapPut("/faults/{target}", Results<Ok<FaultStatus>, ValidationProblem> (
            string target, FaultRule rule, FaultInjector faults) =>
        {
            var errors = new Dictionary<string, string[]>();
            if (!FaultInjector.IsValidTarget(target))
                errors["target"] = [$"target must be one of: {string.Join(", ", FaultInjector.Targets)}."];
            if (rule.FailNext < 0)
                errors["fail_next"] = ["fail_next must be >= 0."];
            if (rule.StatusCode is < 100 or > 599)
                errors["status_code"] = ["status_code must be a valid HTTP status code."];
            if (rule.RetryAfterSeconds is < 0)
                errors["retry_after_seconds"] = ["retry_after_seconds must be >= 0."];
            if (rule.DelayMs < 0)
                errors["delay_ms"] = ["delay_ms must be >= 0."];
            if (errors.Count > 0)
                return TypedResults.ValidationProblem(errors);

            faults.Set(target, rule);
            return TypedResults.Ok(faults.Snapshot()[target]);
        });

        admin.MapGet("/faults", (FaultInjector faults) => TypedResults.Ok(faults.Snapshot()));

        admin.MapGet("/requests", (MockState state) => TypedResults.Ok(state.RequestLog));

        admin.MapPost("/hris/employees", Results<Ok<HrisEmployee>, ValidationProblem> (
            HrisEmployee employee, MockState state) =>
        {
            if (string.IsNullOrWhiteSpace(employee.EmployeeId))
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["employee_id"] = ["employee_id is required."]
                });

            state.Employees[employee.EmployeeId] = employee;
            return TypedResults.Ok(employee);
        });

        admin.MapPost("/reset", (MockState state, FaultInjector faults) =>
        {
            state.Reset();
            faults.Reset();
            return TypedResults.NoContent();
        });

        return app;
    }
}
