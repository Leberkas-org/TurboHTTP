using TurboHttp.Internal;

namespace TurboHttp.Transport;

/// <summary>
/// Defines the contract for transport-specific connection handling within <see cref="ConnectionStage"/>.
/// Implementations encapsulate either TCP single-stream (HTTP/1.x, HTTP/2) or QUIC multi-stream (HTTP/3) logic.
/// </summary>
internal interface ITransportHandler
{
    /// <summary>
    /// Called during <c>PreStart</c> (or on first <see cref="HandleConnectItem"/> call).
    /// The handler must register all async callbacks via <paramref name="callbacks"/>.<see cref="IStageCallbacks.GetAsyncCallback{T}"/>
    /// here so they are bound to the stage event loop before any I/O begins.
    /// </summary>
    void Initialize(IStageCallbacks callbacks);

    /// <summary>Initiate connection acquisition for the given <paramref name="connect"/> item.</summary>
    void HandleConnectItem(ConnectItem connect);

    /// <summary>Write <paramref name="dataItem"/> to the transport, or buffer it until the connection is available.</summary>
    void HandleDataItem(DataItem dataItem);

    /// <summary>
    /// Route a tagged QUIC output item to the correct stream.
    /// TCP handlers may treat this as a no-op; QUIC handlers dispatch based on <see cref="Http3OutputTaggedItem.StreamType"/>.
    /// </summary>
    void HandleTaggedItem(Http3OutputTaggedItem outputTagged);

    /// <summary>Apply the connection-reuse decision for the current request/response cycle.</summary>
    void HandleConnectionReuseItem(ConnectionReuseItem reuseItem);

    /// <summary>Update the per-connection stream capacity from a received <c>SETTINGS_MAX_CONCURRENT_STREAMS</c> value.</summary>
    void HandleMaxConcurrentStreamsItem(MaxConcurrentStreamsItem item);

    /// <summary>Reserve capacity for a new HTTP/2 stream before the request is sent.</summary>
    void HandleStreamAcquireItem(StreamAcquireItem item);

    /// <summary>Upstream has completed — no more output items will arrive.</summary>
    void OnUpstreamFinished();

    /// <summary>The connect-acquisition timer fired before a connection was established.</summary>
    void OnConnectTimeout();

    /// <summary>Dispose all resources owned by this handler (called from <c>PostStop</c> and <c>OnDownstreamFinish</c>).</summary>
    void Cleanup();
}
