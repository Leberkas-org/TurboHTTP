using System.Buffers;

namespace TurboHTTP.Protocol.Multiplexed.Body;

internal sealed class StreamingBodyEncoder : IBodyEncoder
{
    private readonly int _chunkSize;
    private readonly CancellationTokenSource _cts = new();

    public StreamingBodyEncoder(int chunkSize = 16 * 1024)
    {
        _chunkSize = chunkSize;
    }

    public void Start(Stream bodyStream, Action<object> onMessage) => _ = DrainAsync(bodyStream, onMessage, _cts.Token);

    private async Task DrainAsync(Stream stream, Action<object> onMessage, CancellationToken ct)
    {
        try
        {
            while (true)
            {
                var owner = MemoryPool<byte>.Shared.Rent(_chunkSize);
                var bytesRead = await stream.ReadAsync(owner.Memory[.._chunkSize], ct).ConfigureAwait(false);
                if (bytesRead == 0)
                {
                    owner.Dispose();
                    break;
                }

                onMessage(new OutboundBodyChunk(owner, bytesRead));
            }

            onMessage(new OutboundBodyComplete());
        }
        catch (Exception ex)
        {
            onMessage(new OutboundBodyFailed(ex));
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
