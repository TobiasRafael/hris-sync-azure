namespace HrSync.Adapters.Correlation;

// AsyncLocal-backed, so a value set by the function flows into every awaited call below it,
// including HttpClient handlers, without leaking into other concurrent invocations.
public sealed class CorrelationContext : ICorrelationContext
{
    private static readonly AsyncLocal<string?> Current = new();

    public string? CorrelationId
    {
        get => Current.Value;
        set => Current.Value = value;
    }
}
