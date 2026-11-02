namespace HrSync.AppHost.Tests;

// Looks for Azure Functions Core Tools on PATH; the AppHost starts the Functions project with func.
internal static class FuncCliAvailability
{
    public static bool IsAvailable { get; } = Detect();

    private static bool Detect()
    {
        string[] names = OperatingSystem.IsWindows() ? ["func.exe", "func.cmd"] : ["func"];
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        return path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(dir => names.Any(n => File.Exists(Path.Combine(dir, n))));
    }
}
