namespace TurboHTTP.Client;

/// <summary>
/// HTTP/2-specific configuration options.
/// Defaults are aligned with <c>System.Net.Http.SocketsHttpHandler</c>.
/// </summary>
public sealed class Http2Options
{
    /// <summary>
    /// Maximum number of concurrent TCP connections per server for HTTP/2.
    /// HTTP/2 multiplexes many streams over a single connection, so far fewer connections
    /// are needed compared to HTTP/1.x. Default is 6 to spread load across multiple
    /// actor turns at medium concurrency (CL=8–128).
    /// </summary>
    public int MaxConnectionsPerServer { get; set; } = 6;

    /// <summary>
    /// Maximum number of concurrent HTTP/2 streams per connection.
    /// Controls how many requests can be in-flight simultaneously on a single H/2 TCP connection,
    /// enabling true request multiplexing within each substream.
    /// Default is 100.
    /// </summary>
    public int MaxConcurrentStreams { get; set; } = 100;

    /// <summary>
    /// Connection-level flow control window size in bytes (RFC 9113 §6.9).
    /// Advertised via WINDOW_UPDATE on stream 0 during the connection preface.
    /// Default is 64 MB.
    /// </summary>
    public int InitialConnectionWindowSize { get; set; } = 64 * 1024 * 1024;

    /// <summary>
    /// Per-stream initial flow control window size in bytes (RFC 9113 §6.9.2).
    /// Advertised via SETTINGS_INITIAL_WINDOW_SIZE in the connection preface.
    /// Default is 65,535 (RFC 9113 §6.9.2 default).
    /// </summary>
    public int InitialStreamWindowSize { get; set; } = 2 * 1024 * 1024;

    /// <summary>
    /// Maximum HTTP/2 frame payload size in bytes (RFC 9113 §4.2).
    /// Advertised via SETTINGS_MAX_FRAME_SIZE in the connection preface.
    /// Default is 16,384 (RFC 9113 minimum/default).
    /// </summary>
    public int MaxFrameSize { get; set; } = 64 * 1024;

    /// <summary>
    /// HPACK dynamic table size in bytes (RFC 7541 §4.2).
    /// Advertised via SETTINGS_HEADER_TABLE_SIZE in the connection preface.
    /// Default is 4,096 (RFC 7541 default).
    /// </summary>
    public int HeaderTableSize { get; set; } = 64 * 1024;

    /// <summary>
    /// Maximum number of reconnect attempts when a TCP connection drops with in-flight requests.
    /// After this many failed reconnects, the connection stage fails with an exception.
    /// Default is 3.
    /// </summary>
    public int MaxReconnectAttempts { get; set; } = 3;

    /// <summary>
    /// Delay before sending a keep-alive PING frame when no frames have been received.
    /// Set to <see cref="Timeout.InfiniteTimeSpan"/> to disable keep-alive pings (default).
    /// </summary>
    public TimeSpan KeepAlivePingDelay { get; set; } = Timeout.InfiniteTimeSpan;

    /// <summary>
    /// Timeout for keep-alive PING acknowledgment. If no frame is received within this
    /// duration after a PING is sent, the connection is closed and reconnected.
    /// Default is 20 seconds.
    /// </summary>
    public TimeSpan KeepAlivePingTimeout { get; set; } = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Controls when keep-alive PINGs are sent.
    /// <see cref="HttpKeepAlivePingPolicy.Always"/> sends pings for the connection lifetime;
    /// <see cref="HttpKeepAlivePingPolicy.WithActiveRequests"/> only while streams are active.
    /// Default is <see cref="HttpKeepAlivePingPolicy.Always"/>.
    /// </summary>
    public HttpKeepAlivePingPolicy KeepAlivePingPolicy { get; set; } = HttpKeepAlivePingPolicy.Always;
}