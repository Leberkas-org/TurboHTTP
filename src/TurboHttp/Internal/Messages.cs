using System.Buffers;
using TurboHttp.Transport;
using TurboHttp.Protocol.RFC9112;

namespace TurboHttp.Internal;

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
/// Emitted by <see cref="TurboHttp.Streams.Stages.Features.ConnectionReuseStage"/> based on RFC 9112 §9.
/// </summary>
public record ConnectionReuseItem(RequestEndpoint Key, ConnectionReuseDecision Decision) : IControlItem;

/// <summary>
/// Requests the connection stage to establish (or re-establish) a TCP connection using the given options.
/// Emitted once per host when no active connection exists.
/// </summary>
public record ConnectItem(TcpOptions Options) : IControlItem
{
    public RequestEndpoint Key { get; init; }
}

/// <summary>
/// Carries a raw byte buffer between the pipeline and the TCP layer.
/// Implements both <see cref="IOutputItem"/> (outbound write) and <see cref="IInputItem"/> (inbound read).
/// The caller is responsible for disposing <see cref="Memory"/> after use.
/// </summary>
public record DataItem(IMemoryOwner<byte> Memory, int Length) : IOutputItem, IInputItem
{
    public RequestEndpoint Key { get; init; }
}

/// <summary>
/// Carries the <c>SETTINGS_MAX_CONCURRENT_STREAMS</c> value received from the server in an HTTP/2 SETTINGS frame.
/// Used to update the per-connection stream capacity tracked by <see cref="TurboHttp.Pooling.HostPool"/>.
/// </summary>
public record MaxConcurrentStreamsItem(int MaxStreams) : IControlItem
{
    public RequestEndpoint Key { get; init; }
}

/// <summary>
/// Requests the connection stage to reserve capacity for a new HTTP/2 stream.
/// Emitted before each HTTP/2 request to ensure stream-count limits are honoured.
/// </summary>
public record StreamAcquireItem : IControlItem
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
/// Emitted by <see cref="TurboHttp.Transport.ConnectionStage"/> when the inbound data channel completes.
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
/// Wraps an <see cref="IOutputItem"/> with an <see cref="OutputStreamType"/> tag
/// so the demux stage can route it to the correct QUIC stream.
/// </summary>
public record Http3TaggedItem(IOutputItem Inner, OutputStreamType StreamType) : IOutputItem
{
    public RequestEndpoint Key => Inner.Key;
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