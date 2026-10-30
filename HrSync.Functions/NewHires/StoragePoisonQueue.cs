using System.Text.Json;
using Azure.Storage.Queues;

namespace HrSync.Functions.NewHires;

// The QueueClient must use QueueMessageEncoding.Base64 to match what the Functions queue extension
// writes and expects (see Program.cs).
public sealed class StoragePoisonQueue(QueueClient queue) : IPoisonQueue
{
    public const string QueueName = "new-hires-poison";

    private readonly SemaphoreSlim _createGate = new(1, 1);
    private volatile bool _queueReady;

    public async Task SendAsync(PoisonEnvelope envelope, CancellationToken ct)
    {
        await EnsureQueueAsync(ct);
        await queue.SendMessageAsync(JsonSerializer.Serialize(envelope), ct);
    }

    private async Task EnsureQueueAsync(CancellationToken ct)
    {
        if (_queueReady)
            return;

        await _createGate.WaitAsync(ct);
        try
        {
            if (!_queueReady)
            {
                await queue.CreateIfNotExistsAsync(cancellationToken: ct);
                _queueReady = true;
            }
        }
        finally
        {
            _createGate.Release();
        }
    }
}
