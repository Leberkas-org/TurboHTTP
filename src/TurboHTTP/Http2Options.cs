namespace TurboHTTP;

/// <summary>
/// HTTP/2-specific configuration options.
/// </summary>
public sealed class Http2Options
{
    /// <summary>
    /// Maximum number of concurrent TCP connections per server for HTTP/2.
    /// HTTP/2 multiplexes many streams over a single connection, so far fewer connections
    /// are optimal compared to HTTP/1.x. Default is 2.
    /// </summary>
    public int MaxConnectionsPerServer { get; set; } = 2;

    /// <summary>
    /// Maximum number of concurrent HTTP/2 streams per connection.
    /// Controls how many requests can be in-flight simultaneously on a single H/2 TCP connection,
    /// enabling true request multiplexing within each substream.
    /// Default is 100.
    /// </summary>
    public int MaxConcurrentStreams { get; set; } = 100;

    /// <summary>
    /// Initial HTTP/2 receive flow-control window size in bytes.
    /// Controls the maximum amount of data a server may send before the client sends a
    /// <c>WINDOW_UPDATE</c> frame. A larger window prevents stream stalls on high-bandwidth,
    /// high-latency links (e.g. 1 MB at 50 ms RTT supports ~160 Mbps before throttling).
    /// Default is 1 MiB (1 048 576 bytes). RFC 9113 §6.5.2 minimum is 65 535 bytes.
    /// </summary>
    public int InitialWindowSize { get; set; } = 1_048_576;

    /// <summary>
    /// Maximum HTTP/2 frame size in bytes. Default is 128 KiB.
    /// </summary>
    public int MaxFrameSize { get; set; } = 128 * 1024;
}
