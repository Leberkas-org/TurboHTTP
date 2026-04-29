using System.Net;

namespace Servus.Akka.Transport.Quic;

public sealed class QuicConnectionHandle : IAsyncDisposable
{
    private readonly Func<StreamDirection, CancellationToken, Task<(Stream, long)>> _openStream;
    private readonly Func<CancellationToken, Task<(Stream, long)?>> _acceptInboundStream;
    private readonly Func<ValueTask> _dispose;
    private readonly Func<EndPoint?> _getLocalEndPoint;

    internal QuicConnectionHandle(
        Func<StreamDirection, CancellationToken, Task<(Stream, long)>> openStream,
        Func<CancellationToken, Task<(Stream, long)?>> acceptInboundStream,
        Func<EndPoint?> getLocalEndPoint,
        Func<ValueTask> dispose)
    {
        _openStream = openStream;
        _acceptInboundStream = acceptInboundStream;
        _getLocalEndPoint = getLocalEndPoint;
        _dispose = dispose;
    }

    public Task<(Stream Stream, long StreamId)> OpenStreamAsync(
        StreamDirection direction, CancellationToken ct = default)
        => _openStream(direction, ct);

    public Task<(Stream Stream, long StreamId)?> AcceptInboundStreamAsync(
        CancellationToken ct = default)
        => _acceptInboundStream(ct);

    public EndPoint? LocalEndPoint() => _getLocalEndPoint();

    public ValueTask DisposeAsync() => _dispose();
}