namespace HrSync.Functions.Tests;

// Cheap check for a reachable Docker engine, so container-based tests can be skipped instead of failing.
internal static class DockerAvailability
{
    public static bool IsAvailable { get; } = Detect();

    private static bool Detect()
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DOCKER_HOST")))
            return true;

        try
        {
            return OperatingSystem.IsWindows()
                ? Directory.GetFiles(@"\\.\pipe\").Any(p => p.Contains("docker", StringComparison.OrdinalIgnoreCase))
                : File.Exists("/var/run/docker.sock");
        }
        catch (IOException)
        {
            return false;
        }
    }
}
