namespace HrSync.Functions.NewHires;

public interface IPoisonQueue
{
    Task SendAsync(PoisonEnvelope envelope, CancellationToken ct);
}
