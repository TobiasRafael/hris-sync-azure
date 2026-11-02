namespace HrSync.AppHost.Tests;

// A [Fact] that is skipped (not failed) when the AppHost can't run here: it needs a Docker engine for the
// Azurite emulator and Azure Functions Core Tools (func) on PATH for the Functions project.
public sealed class E2EFactAttribute : FactAttribute
{
    public E2EFactAttribute()
    {
        if (!DockerAvailability.IsAvailable)
            Skip = "No Docker engine: the AppHost runs Azurite as a container.";
        else if (!FuncCliAvailability.IsAvailable)
            Skip = "Azure Functions Core Tools (func) not found on PATH.";
    }
}
