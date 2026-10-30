namespace HrSync.Adapters.Correlation;

// Ambient correlation id for the current async flow (one queue message / one request).
public interface ICorrelationContext
{
    string? CorrelationId { get; set; }
}
