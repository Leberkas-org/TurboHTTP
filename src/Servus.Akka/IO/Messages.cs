using System.Buffers;
using System.Collections.Concurrent;

namespace Servus.Akka.IO;

public interface IInputItem
{
    RequestEndpoint Key { get; }
}

public interface IOutputItem
{
    RequestEndpoint Key { get; }
}

public interface IControlItem : IOutputItem;

public readonly record struct ConnectionReuseItem(bool CanReuse) : IControlItem
{
    public RequestEndpoint Key { get; init; }
}

public readonly record struct ConnectItem(ITransportOptions Options) : IControlItem
{
    public RequestEndpoint Key { get; init; }
    public bool IsReconnect { get; init; }
}

public readonly record struct MaxConcurrentStreamsItem(int MaxStreams) : IControlItem
{
    public RequestEndpoint Key { get; init; }
}

public readonly record struct StreamAcquireItem : IControlItem
{
    public RequestEndpoint Key { get; init; }
}

public enum TlsCloseKind
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

public readonly record struct CloseSignalItem(TlsCloseKind CloseKind) : IInputItem
{
    public RequestEndpoint Key { get; init; }
}

public readonly record struct ConnectedSignalItem : IInputItem
{
    public RequestEndpoint Key { get; init; }
}

public class NetworkBuffer : IInputItem, IOutputItem
{
    private static readonly ConcurrentStack<NetworkBuffer> WrapperPool = new();

    protected static int MaxPoolSize { get; private set; } = Environment.ProcessorCount * 4;

    protected IMemoryOwner<byte>? Owner;

    public int Length { get; set; }

    public RequestEndpoint Key { get; set; }

    public Memory<byte> Memory => Owner!.Memory[..Length];

    public ReadOnlySpan<byte> Span => Owner!.Memory.Span[..Length];

    public Memory<byte> FullMemory => Owner!.Memory;

    public int Capacity => Owner?.Memory.Length ?? 0;

    public static void ConfigurePoolSize(int maxPoolSize)
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

public class RoutedNetworkBuffer : NetworkBuffer
{
    private static readonly ConcurrentStack<RoutedNetworkBuffer> WrapperPool = new();

    public long? StreamTypeValue { get; set; }

    public long? StreamId { get; set; }

    public new static RoutedNetworkBuffer Rent(int minimumSize)
    {
        var owner = MemoryPool<byte>.Shared.Rent(minimumSize);
        if (!WrapperPool.TryPop(out var buf))
        {
            return new RoutedNetworkBuffer { Owner = owner };
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

public readonly record struct Http3EndOfRequestItem : IOutputItem
{
    public RequestEndpoint Key { get; init; }
    public long StreamId { get; init; }
}

public enum QuicCloseKind
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

public readonly record struct QuicCloseItem(QuicCloseKind Kind, long StreamId = -1) : IInputItem
{
    public RequestEndpoint Key { get; init; }
}

public readonly record struct OpenTypedStreamItem(long StreamTypeValue, long SyntheticStreamId, bool Outbound)
    : IOutputItem
{
    public RequestEndpoint Key { get; init; }
}

public readonly record struct ProtocolReadyItem : IOutputItem
{
    public RequestEndpoint Key { get; init; }
}