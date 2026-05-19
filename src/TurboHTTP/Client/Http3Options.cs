namespace TurboHTTP.Client;

/// <summary>
/// HTTP/3-specific configuration options.
/// Defaults are aligned with <c>System.Net.Http.SocketsHttpHandler</c>.
/// </summary>
public sealed class Http3Options
{
    /// <summary>
    /// Maximum number of concurrent QUIC connections per server for HTTP/3.
    /// QUIC multiplexes streams more efficiently than TCP, so fewer connections are needed
    /// compared to HTTP/2. Default is 4.
    /// </summary>
    public int MaxConnectionsPerServer { get; set; } = 4;

    /// <summary>
    /// Maximum number of concurrent streams per HTTP/3 connection.
    /// Controls both the GroupBy slot capacity and the per-connection stream budget.
    /// Default is 100.
    /// </summary>
    public int MaxConcurrentStreams { get; set; } = 100;

    /// <summary>
    /// Maximum capacity of the QPACK dynamic table in bytes.
    /// Controls the size of the dynamic table used for header compression.
    /// Larger values improve compression ratio at the cost of memory.
    /// Default is 4096 bytes. RFC 9204 §3.2.3.
    /// </summary>
    public int QpackMaxTableCapacity { get; set; } = 16 * 1024;

    /// <summary>
    /// Maximum number of streams that can be blocked waiting for QPACK encoder instructions.
    /// Higher values allow better compression but risk head-of-line blocking when
    /// encoder references are not yet received. Default is 100. RFC 9204 §3.2.3.
    /// </summary>
    public int QpackBlockedStreams { get; set; } = 100;

    /// <summary>
    /// Maximum size of an HTTP/3 field section (header block) in bytes.
    /// Limits the combined size of all header fields in a single request or response.
    /// Default is 65536 bytes (64 KiB). RFC 9114 §7.2.4.1.
    /// </summary>
    public int MaxFieldSectionSize { get; set; } = 64 * 1024;

    /// <summary>
    /// QUIC idle timeout. If no data is exchanged for this duration, the connection is closed.
    /// Default is 30 seconds. RFC 9000 §10.1.
    /// </summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Maximum number of reconnect attempts when a QUIC connection drops with in-flight requests.
    /// After this many failed reconnects, the connection stage fails with an exception.
    /// Default is 3.
    /// </summary>
    public int MaxReconnectAttempts { get; set; } = 3;

    /// <summary>
    /// Whether to allow QUIC connection migration when the client's local IP address or port changes
    /// (e.g., switching from Wi-Fi to cellular). When enabled, the QUIC connection continues
    /// transparently after the address change. When disabled, the connection is closed and a new
    /// connection is established via the reconnect mechanism.
    /// Default is true. RFC 9000 §9.
    /// </summary>
    public bool AllowConnectionMigration { get; set; } = true;

    /// <summary>
    /// Whether to automatically discover HTTP/3 availability via Alt-Svc headers (RFC 7838)
    /// in HTTP/1.1 and HTTP/2 responses. When enabled, Alt-Svc directives advertising "h3"
    /// are cached per-host and subsequent requests to that host are upgraded to HTTP/3
    /// if QUIC is available. Default is false (opt-in).
    /// </summary>
    public bool EnableAltSvcDiscovery { get; set; }

    /// <summary>
    /// Maximum number of frames that can be buffered during reconnection.
    /// When this limit is reached, new requests fail instead of being buffered.
    /// Default is 64.
    /// </summary>
    public int MaxReconnectBufferSize { get; set; } = 64;
}