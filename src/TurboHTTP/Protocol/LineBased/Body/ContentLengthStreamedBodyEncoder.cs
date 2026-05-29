using System.Buffers;
using Akka.Actor;

namespace TurboHTTP.Protocol.LineBased.Body;

internal sealed class ContentLengthStreamedBodyEncoder : IBodyEncoder
{
    private readonly int _chunkSize;
    private readonly CancellationTokenSource _cts = new();

    public ContentLengthStreamedBodyEncoder(int chunkSize = 16 * 1024)
    {
        _chunkSize = chunkSize;
    }

    public void Start(Stream bodyStream, IActorRef stageActor)
    {
        _ = DrainAsync(bodyStream, stageActor, _cts.Token);
    }

    private async Task DrainAsync(Stream stream, IActorRef stageActor, CancellationToken ct)
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

                stageActor.Tell(new OutboundBodyChunk(owner, bytesRead));
            }

            stageActor.Tell(new OutboundBodyComplete());
        }
        catch (Exception ex)
        {
            stageActor.Tell(new OutboundBodyFailed(ex));
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}