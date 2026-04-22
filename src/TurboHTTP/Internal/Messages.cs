using System.Buffers;
using System.Collections.Concurrent;
using TurboHTTP.Protocol.Http11;
using TurboHTTP.Transport.Connection;

namespace TurboHTTP.Internal;

internal interface IInputItem
{
    RequestEndpoint Key { get; }
}

internal interface IOutputItem
{
    RequestEndpoint Key { get; }
}

internal interface IControlItem : IOutputItem;

internal readonly record struct ConnectionReuseItem(ConnectionReuseDecision Decision) : IControlItem
{
    public RequestEndpoint Key { get; init; }
}

internal readonly record struct ConnectItem(ITransportOptions Options) : IControlItem
{
    public RequestEndpoint Key { get; init; }
    public bool IsReconnect { get; init; }
}

internal readonly record struct MaxConcurrentStreamsItem(int MaxStreams) : IControlItem
{
    public RequestEndpoint Key { get; init; }
}

internal readonly record struct StreamAcquireItem : IControlItem
{
    public RequestEndpoint Key { get; init; }
}

internal enum TlsCloseKind
{
    /// <summary>
    /// The peer sent a TLS close_notify alert before closing the connection,
    /// or a plain TCP connection received a FIN. The response body (if any)
    /// that was buffered before the close is considered complete (RFC 9112 §9.8).
    /// </summary>
    CleanClose,

    /// <summary>
    /// The connection was closed abruptly (TCP RST, I/O error, or TLS error
    /// without close_notify). Any partially received response must be treated
    /// as incomplete and should not be delivered to the application.
    /// </summary>
    AbruptClose
}

internal readonly record struct CloseSignalItem(TlsCloseKind CloseKind) : IInputItem
{
    public RequestEndpoint Key { get; init; }
}

internal readonly record struct ConnectedSignalItem : IInputItem
{
    public RequestEndpoint Key { get; init; }
}

internal class NetworkBuffer : IInputItem, IOutputItem
{
    private static readonly ConcurrentStack<NetworkBuffer> WrapperPool = new();

    protected static int MaxPoolSize { get; private set; } = Environment.ProcessorCount * 2;

    protected IMemoryOwner<byte>? Owner;

    public int Length { get; set; }

    public RequestEndpoint Key { get; set; }

    public Memory<byte> Memory => Owner!.Memory[..Length];

    public ReadOnlySpan<byte> Span => Owner!.Memory.Span[..Length];

    public Memory<byte> FullMemory => Owner!.Memory;

    internal int Capacity => Owner?.Memory.Length ?? 0;

    internal static void ConfigurePoolSize(int maxPoolSize)
    {
        MaxPoolSize = maxPoolSize;
    }

    public static NetworkBuffer Rent(int minimumSize)
    {
        var owner = MemoryPool<byte>.Shared.Rent(minimumSize);
        if (!WrapperPool.TryPop(out var buf))
        {
            return new NetworkBuffer { Owner = owner };
        }

        buf.Owner = owner;
        buf.Length = 0;
        buf.Key = RequestEndpoint.Default;
        return buf;
    }

    protected void DisposeOwner()
    {
        var owner = Interlocked.Exchange(ref Owner, null);
        owner?.Dispose();
    }

    public virtual void Dispose()
    {
        DisposeOwner();

        if (MaxPoolSize > 0 && WrapperPool.Count <= MaxPoolSize)
        {
            WrapperPool.Push(this);
        }
    }
}

internal class Http3NetworkBuffer : NetworkBuffer
{
    private static readonly ConcurrentStack<Http3NetworkBuffer> WrapperPool = new();

    public long? StreamTypeValue { get; set; }

    public long? StreamId { get; set; }

    public new static Http3NetworkBuffer Rent(int minimumSize)
    {
        var owner = MemoryPool<byte>.Shared.Rent(minimumSize);
        if (!WrapperPool.TryPop(out var buf))
        {
            return new Http3NetworkBuffer { Owner = owner };
        }

        buf.Owner = owner;
        buf.Length = 0;
        buf.Key = default;
        buf.StreamTypeValue = null;
        buf.StreamId = null;
        return buf;
    }

    public override void Dispose()
    {
        DisposeOwner();
        if (MaxPoolSize > 0 && WrapperPool.Count <= MaxPoolSize)
        {
            WrapperPool.Push(this);
        }
    }
}

/// <summary>
/// Signals that all HTTP/3 frames for the current request have been emitted.
/// The transport handles this by completing the request stream's write side,
/// which causes the QUIC layer to send FIN and lets the server process the request.
/// RFC 9114 §4.1: the client MUST send a FIN on the request stream after the last frame.
/// </summary>
internal readonly record struct Http3EndOfRequestItem : IOutputItem
{
    public RequestEndpoint Key { get; init; }
    public long StreamId { get; init; }
}

/// <summary>
/// Discriminates the reason a QUIC stream or connection was closed.
/// Used by <see cref="QuicCloseItem"/> so the protocol layer can choose
/// the appropriate recovery strategy (flush response, reconnect, or complete).
/// </summary>
internal enum QuicCloseKind
{
    /// <summary>
    /// Server sent FIN on the request stream. The response body is delimited
    /// by this FIN. Keep the QUIC connection and control/encoder streams alive.
    /// </summary>
    RequestStreamComplete,

    /// <summary>
    /// Connection-level failure (TCP RST, I/O error, or TLS error).
    /// Tear down all streams and initiate reconnection if requests are in flight.
    /// </summary>
    ConnectionFailure,

    /// <summary>
    /// Connection migration detected when migration is disabled.
    /// Close and reconnect from the original endpoint.
    /// </summary>
    MigrationDisallowed,

    /// <summary>
    /// Outbound write to a QUIC stream failed.
    /// </summary>
    WriteFailed,

    /// <summary>
    /// Connection acquisition timed out or the underlying provider threw.
    /// </summary>
    AcquisitionFailed,
}

/// <summary>
/// Unified close signal for the QUIC transport layer. Consolidates all QUIC
/// close scenarios into a single message type with a <see cref="QuicCloseKind"/>
/// discriminator so the protocol stage can choose the appropriate recovery path.
/// The <see cref="QuicCloseKind"/> discriminator tells the protocol stage
/// which recovery path to take.
/// </summary>
internal readonly record struct QuicCloseItem(QuicCloseKind Kind, long StreamId = -1) : IInputItem
{
    public RequestEndpoint Key { get; init; }
}