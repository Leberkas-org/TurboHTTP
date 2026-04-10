using System.Buffers;
using System.Collections.Concurrent;
using TurboHTTP.Transport.Connection;
using TurboHTTP.Protocol.Http11;
using TurboHTTP.Streams.Stages;

namespace TurboHTTP.Internal;

/// <summary>Marker interface for items flowing from the network into the protocol engine (inbound data).</summary>
public interface IInputItem
{
    RequestEndpoint Key { get; }
}

/// <summary>Marker interface for items flowing from the protocol engine toward the network (outbound data and control signals).</summary>
public interface IOutputItem
{
    RequestEndpoint Key { get; }
}

/// <summary>Marker interface for non-data control signals that flow out toward the network layer.</summary>
public interface IControlItem : IOutputItem;

/// <summary>
/// Signals the connection-reuse decision for the current request/response cycle.
/// Emitted by <see cref="TurboHTTP.Streams.Stages.Features.ConnectionReuseStage"/> based on RFC 9112 §9.
/// Pooled to avoid per-request heap allocation on the hot path.
/// </summary>
public sealed class ConnectionReuseItem : IControlItem
{
    private static readonly ConcurrentStack<ConnectionReuseItem> Pool = new();

    public RequestEndpoint Key { get; set; }
    public ConnectionReuseDecision Decision { get; set; } = null!;

    private ConnectionReuseItem() { }

    /// <summary>Rents an item from the pool, setting <see cref="Key"/> and <see cref="Decision"/>.</summary>
    public static ConnectionReuseItem Rent(RequestEndpoint key, ConnectionReuseDecision decision)
    {
        if (!Pool.TryPop(out var item))
        {
            item = new ConnectionReuseItem();
        }

        item.Key = key;
        item.Decision = decision;
        return item;
    }

    /// <summary>Returns this item to the pool for reuse.</summary>
    public void Return()
    {
        Key = default;
        Decision = null!;
        Pool.Push(this);
    }
}

/// <summary>
/// Requests the connection stage to establish (or re-establish) a TCP connection using the given options.
/// Emitted once per host when no active connection exists.
/// </summary>
public record ConnectItem(TcpOptions Options) : IControlItem
{
    public RequestEndpoint Key { get; init; }
}

/// <summary>
/// A unified buffer type for the TurboHttp pipeline.
/// Implements both <see cref="IOutputItem"/> (outbound write) and <see cref="IInputItem"/> (inbound read).
/// Internally backed by <see cref="MemoryPool{T}.Shared"/> via <see cref="IMemoryOwner{T}"/>;
/// the pooling abstraction is never exposed to pipeline stages.
/// The wrapper object itself is pooled via a <see cref="ConcurrentStack{T}"/> to eliminate
/// one heap allocation per encoded request on the hot path.
/// Dispose returns the <see cref="IMemoryOwner{T}"/> to the pool and the wrapper object
/// to the internal wrapper pool. Idempotent.
/// </summary>
public sealed class NetworkBuffer : IInputItem, IOutputItem
{
    private static readonly ConcurrentStack<NetworkBuffer> WrapperPool = new();

    /// <summary>Maximum number of wrapper objects to pool. When exceeded, excess wrappers are discarded.</summary>
    private static int _maxPoolSize = Environment.ProcessorCount * 2;

    private IMemoryOwner<byte>? _owner;

    /// <summary>Number of valid bytes in the buffer.</summary>
    public int Length { get; set; }

    public RequestEndpoint Key { get; set; }

    /// <summary>Slice of <see cref="Length"/> bytes — for downstream consumption.</summary>
    public Memory<byte> Memory => _owner!.Memory[..Length];

    /// <summary>Read-only span of <see cref="Length"/> bytes.</summary>
    public ReadOnlySpan<byte> Span => _owner!.Memory.Span[..Length];

    /// <summary>Full rented capacity — for writing into the buffer before <see cref="Length"/> is set.</summary>
    internal Memory<byte> FullMemory => _owner!.Memory;

    /// <summary>Total rented capacity of the underlying buffer.</summary>
    internal int Capacity => _owner?.Memory.Length ?? 0;

    private NetworkBuffer()
    {
    }

    /// <summary>
    /// Configures the maximum size of the wrapper pool. Must be called once during client initialization.
    /// Thread-safe; subsequent calls overwrite the previous value.
    /// </summary>
    /// <param name="maxPoolSize">Maximum number of wrapper objects to pool. If &lt;= 0, no pooling is used.</param>
    internal static void ConfigurePoolSize(int maxPoolSize)
    {
        _maxPoolSize = maxPoolSize;
    }

    /// <summary>
    /// Rents a buffer of at least <paramref name="minimumSize"/> bytes from <see cref="MemoryPool{T}.Shared"/>.
    /// Reuses a pooled wrapper object when available; allocates a new one otherwise.
    /// </summary>
    public static NetworkBuffer Rent(int minimumSize)
    {
        var owner = MemoryPool<byte>.Shared.Rent(minimumSize);
        if (!WrapperPool.TryPop(out var buf))
        {
            return new NetworkBuffer { _owner = owner };
        }

        buf._owner = owner;
        buf.Length = 0;
        buf.Key = default;
        return buf;
    }

    /// <summary>
    /// Creates a non-pooled buffer backed by a managed array.
    /// Dispose is a no-op; the GC owns the array. Intended for tests and one-off buffers.
    /// </summary>
    internal static NetworkBuffer FromArray(byte[] data, int length = -1)
    {
        var len = length < 0 ? data.Length : length;
        return new NetworkBuffer { _owner = new NonDisposingOwner(data), Length = len };
    }

    /// <summary>
    /// Disposes the internal <see cref="IMemoryOwner{T}"/> and returns the wrapper to the pool
    /// if the pool has not reached capacity. Idempotent.
    /// </summary>
    public void Dispose()
    {
        var owner = Interlocked.Exchange(ref _owner, null);
        if (owner is null)
        {
            return;
        }

        owner.Dispose();

        // Only return to pool if capacity allows
        if (_maxPoolSize > 0 && WrapperPool.Count < _maxPoolSize)
        {
            WrapperPool.Push(this);
        }
    }

    private sealed class NonDisposingOwner(byte[] data) : IMemoryOwner<byte>
    {
        public Memory<byte> Memory { get; } = data;

        public void Dispose()
        {
        }
    }
}

/// <summary>
/// Carries the <c>SETTINGS_MAX_CONCURRENT_STREAMS</c> value received from the server in an HTTP/2 SETTINGS frame.
/// Used to update the per-connection stream capacity tracked by the connection pool.
/// </summary>
public record MaxConcurrentStreamsItem(int MaxStreams) : IControlItem
{
    public RequestEndpoint Key { get; init; }
}

/// <summary>
/// Requests the connection stage to reserve capacity for a new HTTP/2 stream.
/// Emitted before each HTTP/2 request to ensure stream-count limits are honoured.
/// Pooled to avoid per-request heap allocation on the hot path.
/// </summary>
public sealed class StreamAcquireItem : IControlItem
{
    private static readonly ConcurrentStack<StreamAcquireItem> Pool = new();

    public RequestEndpoint Key { get; set; }

    private StreamAcquireItem() { }

    /// <summary>Rents an item from the pool with the given <paramref name="key"/>.</summary>
    public static StreamAcquireItem Rent(RequestEndpoint key)
    {
        if (!Pool.TryPop(out var item))
        {
            item = new StreamAcquireItem();
        }

        item.Key = key;
        return item;
    }

    /// <summary>Returns this item to the pool for reuse.</summary>
    public void Return()
    {
        Key = default;
        Pool.Push(this);
    }
}

/// <summary>
/// Carries an orphaned in-flight request whose pipelined response was never received
/// because the server closed the connection. Emitted by
/// <see cref="Http11ConnectionStage"/> via the
/// <c>OutNetwork</c> outlet so that upstream layers can re-issue the request on a
/// fresh connection.
/// </summary>
public record PipelineRetryItem(HttpRequestMessage Request) : IControlItem
{
    public RequestEndpoint Key { get; init; }
}

/// <summary>
/// Indicates how a transport connection was closed.
/// Used to distinguish clean TLS closure (close_notify received) from abrupt TCP disconnection.
/// </summary>
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

/// <summary>
/// Signals that the transport connection has closed, carrying the <see cref="TlsCloseKind"/>
/// so that decoder stages can decide whether a partially buffered response is complete.
/// Emitted by <see cref="TurboHTTP.Transport.TcpConnectionStage"/> when the inbound data channel completes.
/// </summary>
public record CloseSignalItem(TlsCloseKind CloseKind) : IInputItem
{
    public RequestEndpoint Key { get; init; }
}

/// <summary>
/// Identifies the QUIC stream that an HTTP/3 output item should be routed to.
/// Used by <c>Http30StreamDemuxStage</c> to route tagged items to the correct QUIC stream.
/// </summary>
public enum OutputStreamType
{
    /// <summary>Bidirectional request stream (default for request/response data).</summary>
    Request,

    /// <summary>Unidirectional control stream (type 0x00) — carries SETTINGS and GOAWAY frames.</summary>
    Control,

    /// <summary>Unidirectional QPACK encoder instruction stream (type 0x02).</summary>
    QpackEncoder,
}

/// <summary>
/// Identifies the QUIC unidirectional stream that an inbound HTTP/3 item arrived on.
/// Used to route inbound items to the correct processing pipeline.
/// </summary>
public enum InputStreamType
{
    /// <summary>Bidirectional request/response stream (default).</summary>
    Request,

    /// <summary>Unidirectional control stream (type 0x00) — carries SETTINGS and GOAWAY frames.</summary>
    Control,

    /// <summary>Unidirectional QPACK encoder instruction stream (type 0x02).</summary>
    QpackEncoder,

    /// <summary>Unidirectional QPACK decoder instruction stream (type 0x03).</summary>
    QpackDecoder,
}

/// <summary>
/// Wraps an <see cref="IInputItem"/> with an <see cref="InputStreamType"/> tag
/// so the engine can route it to the correct processing pipeline.
/// </summary>
public record Http3InputTaggedItem(IInputItem Inner, InputStreamType StreamType) : IInputItem
{
    public RequestEndpoint Key => Inner.Key;
}

/// <summary>
/// Wraps an <see cref="IOutputItem"/> with an <see cref="OutputStreamType"/> tag
/// so the demux stage can route it to the correct QUIC stream.
/// </summary>
public record Http3OutputTaggedItem(IOutputItem Inner, OutputStreamType StreamType) : IOutputItem
{
    public RequestEndpoint Key => Inner.Key;
}