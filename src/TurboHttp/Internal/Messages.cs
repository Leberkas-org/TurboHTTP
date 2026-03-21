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
/// Emitted by <see cref="TurboHttp.Streams.Stages.ConnectionReuseStage"/> based on RFC 9112 §9.
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
/// Used to update the per-connection stream capacity tracked by <see cref="TurboHttp.Lifecycle.HostPool"/>.
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