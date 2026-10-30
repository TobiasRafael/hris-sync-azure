using Microsoft.AspNetCore.Http.HttpResults;

namespace HrSync.MockApis.Hris;

public static class HrisEndpoints
{
    public static IEndpointRouteBuilder MapHris(this IEndpointRouteBuilder app)
    {
        var hris = app.MapGroup("/hris").WithTags("Mock HRIS");

        hris.MapGet("/employees/{id}", Results<Ok<HrisEmployee>, NotFound> (string id, MockState state) =>
            state.Employees.TryGetValue(id, out var employee)
                ? TypedResults.Ok(employee)
                : TypedResults.NotFound());

        return app;
    }
}
