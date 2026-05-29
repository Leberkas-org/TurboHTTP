using System.Buffers;

namespace TurboHTTP.Protocol.Multiplexed.Body;

internal sealed class BufferedBodyEncoder : IBodyEncoder
{
    private readonly CancellationTokenSource _cts = new();

    public void Start(Stream bodyStream, Action<object> onMessage) => _ = DrainAsync(bodyStream, onMessage, _cts.Token);

    private static async Task DrainAsync(Stream stream, Action<object> onMessage, CancellationToken ct)
    {
        try
        {
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, ct).ConfigureAwait(false);
            var length = (int)ms.Length;
            var owner = MemoryPool<byte>.Shared.Rent(length);
            ms.GetBuffer().AsSpan(0, length).CopyTo(owner.Memory.Span);
            onMessage(new OutboundBodyChunk(owner, length));
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
