namespace TurboHTTP;

/// <summary>
/// HTTP/1.x-specific configuration options.
/// Defaults are aligned with <c>System.Net.Http.SocketsHttpHandler</c>.
/// </summary>
public sealed class Http1Options
{
    /// <summary>
    /// Maximum number of concurrent TCP connections per server for HTTP/1.x.
    /// Each connection is managed as an independent substream.
    /// Default is 6 (matching browser defaults and RFC 9112 §9.4 guidance).
    /// </summary>
    public int MaxConnectionsPerServer { get; set; } = 6;

    /// <summary>
    /// Maximum number of pipelined HTTP/1.1 requests allowed per connection before waiting for responses.
    /// Higher values increase throughput on high-latency links; lower values reduce head-of-line blocking.
    /// Default is 16.
    /// </summary>
    public int MaxPipelineDepth { get; set; } = 16;

    /// <summary>
    /// Maximum batch weight in bytes for HTTP/1.x request encoding.
    /// Frames are accumulated into batches up to this weight before being serialized into a single buffer,
    /// reducing allocations and memory copies under concurrent load. Higher values increase throughput
    /// at the cost of latency variance. Default is 64 KiB. TurboHttp-specific.
    /// </summary>
    public long MaxBatchWeight { get; set; } = 65_536;

    /// <summary>
    /// Maximum length of the response headers, in kilobytes (KB).
    /// This limits the combined size of all response header fields received from the server.
    /// Default is 64 (same as <c>SocketsHttpHandler.MaxResponseHeadersLength</c>).
    /// </summary>
    public int MaxResponseHeadersLength { get; set; } = 64;

    /// <summary>
    /// Maximum number of reconnect attempts when a TCP connection drops with in-flight requests.
    /// After this many failed reconnects, the connection stage fails with an exception.
    /// Default is 3.
    /// </summary>
    public int MaxReconnectAttempts { get; set; } = 3;

    /// <summary>
    /// Maximum number of bytes to drain from an incomplete response body before
    /// closing the connection. When the unconsumed body is smaller than this limit,
    /// the connection can be returned to the pool instead of being closed.
    /// Default is 1 MB, matching <c>SocketsHttpHandler.MaxResponseDrainSize</c>.
    /// </summary>
    public int MaxResponseDrainSize { get; set; } = 1024 * 1024;

    /// <summary>
    /// Maximum time allowed to drain an incomplete response body.
    /// If draining exceeds this timeout the connection is closed instead.
    /// Default is 2 seconds, matching <c>SocketsHttpHandler.ResponseDrainTimeout</c>.
    /// </summary>
    public TimeSpan ResponseDrainTimeout { get; set; } = TimeSpan.FromSeconds(2);
}

