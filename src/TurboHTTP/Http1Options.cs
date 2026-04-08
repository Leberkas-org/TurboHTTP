namespace TurboHTTP;

/// <summary>
/// HTTP/1.x-specific configuration options.
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
}
