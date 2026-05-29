using System.Buffers;
using Akka.Actor;

namespace TurboHTTP.Protocol.LineBased.Body;

internal sealed class ContentLengthBufferedBodyEncoder : IBodyEncoder
{
    private readonly CancellationTokenSource _cts = new();

    public void Start(Stream bodyStream, IActorRef stageActor)
    {
        _ = DrainAsync(bodyStream, stageActor, _cts.Token);
    }

    private static async Task DrainAsync(Stream stream, IActorRef stageActor, CancellationToken ct)
    {
        try
        {
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, ct).ConfigureAwait(false);
            var length = (int)ms.Length;
            var owner = MemoryPool<byte>.Shared.Rent(length);
            ms.GetBuffer().AsSpan(0, length).CopyTo(owner.Memory.Span);
            stageActor.Tell(new OutboundBodyChunk(owner, length));
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