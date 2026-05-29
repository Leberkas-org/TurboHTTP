using System.Buffers;
using System.Globalization;
using Akka.Actor;

namespace TurboHTTP.Protocol.LineBased.Body;

internal sealed class ChunkedBodyEncoder : IBodyEncoder
{
    private readonly int _chunkSize;
    private readonly CancellationTokenSource _cts = new();

    public ChunkedBodyEncoder(int chunkSize = 16 * 1024)
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
            var dataBuffer = new byte[_chunkSize];

            while (true)
            {
                var bytesRead = await stream.ReadAsync(dataBuffer, ct).ConfigureAwait(false);
                if (bytesRead == 0)
                {
                    break;
                }

                stageActor.Tell(BuildChunk(dataBuffer.AsSpan(0, bytesRead)));
            }

            stageActor.Tell(BuildTerminator());
            stageActor.Tell(new OutboundBodyComplete());
        }
        catch (Exception ex)
        {
            stageActor.Tell(new OutboundBodyFailed(ex));
        }
    }

    private static OutboundBodyChunk BuildChunk(ReadOnlySpan<byte> data)
    {
        var sizeHex = data.Length.ToString("x", CultureInfo.InvariantCulture);
        // {hex}\r\n{data}\r\n
        var totalLen = sizeHex.Length + 2 + data.Length + 2;
        var owner = MemoryPool<byte>.Shared.Rent(totalLen);
        var writer = SpanWriter.Create(owner.Memory.Span);
        writer.WriteHex(data.Length);
        writer.WriteCrlf();
        writer.WriteBytes(data);
        writer.WriteCrlf();
        return new OutboundBodyChunk(owner, totalLen);
    }

    private static OutboundBodyChunk BuildTerminator()
    {
        // 0\r\n\r\n
        var owner = MemoryPool<byte>.Shared.Rent(5);
        var writer = SpanWriter.Create(owner.Memory.Span);
        writer.WriteBytes(WellKnownHeaders.ZeroValue);
        writer.WriteCrlf();
        writer.WriteCrlf();
        return new OutboundBodyChunk(owner, writer.BytesWritten);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}