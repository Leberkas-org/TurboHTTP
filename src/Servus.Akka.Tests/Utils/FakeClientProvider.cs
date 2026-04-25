using System.Net;
using Servus.Akka.IO;

namespace Servus.Akka.Tests.Utils;

internal sealed class FakeClientProvider(bool blockGetStream = false, byte[]? inboundBytes = null)
    : IClientProvider
{
    private int _streamsOpened;

    public int StreamsOpened => _streamsOpened;
    public bool Disposed { get; private set; }
    public EndPoint? RemoteEndPoint => new IPEndPoint(IPAddress.Loopback, 8443);
    public bool SupportsMultipleStreams => true;

    public Task<Stream> GetStreamAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (blockGetStream)
        {
            return Task.Delay(Timeout.Infinite, ct).ContinueWith<Stream>(_ =>
                throw new OperationCanceledException(ct), ct);
        }

        Interlocked.Increment(ref _streamsOpened);
        return Task.FromResult<Stream>(new MemoryStream());
    }

    public Task<Stream> GetUnidirectionalStreamAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _streamsOpened);
        return Task.FromResult<Stream>(new MemoryStream());
    }

    public Task<Stream> AcceptInboundStreamAsync(CancellationToken ct = default)
    {
        if (inboundBytes is not null)
        {
            var bytes = inboundBytes;
            return Task.Run<Stream>(() => new MemoryStream(bytes), ct);
        }

        return Task.Delay(Timeout.Infinite, ct).ContinueWith<Stream>(_ =>
            throw new OperationCanceledException(ct), ct);
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}
