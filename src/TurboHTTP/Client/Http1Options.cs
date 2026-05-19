namespace TurboHTTP.Client;

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
    /// Maximum length of the response headers, in kilobytes (KB).
    /// This limits the combined size of all response header fields received from the server.
    /// Default is 64 (same as <c>SocketsHttpHandler.MaxResponseHeadersLength</c>).
    /// </summary>
    public int MaxResponseHeadersLength { get; set; } = 64;

    /// <summary>
    /// Automatically add a Host header derived from the request URI if none is present.
    /// Default is true, matching standard HTTP/1.1 behavior.
    /// </summary>
    public bool AutoHost { get; set; } = true;

    /// <summary>
    /// Automatically add Accept-Encoding: gzip, deflate, br if no Accept-Encoding header is present.
    /// Default is true.
    /// </summary>
    public bool AutoAcceptEncoding { get; set; } = true;

    /// <summary>
    /// Maximum number of reconnect attempts when a TCP connection drops with in-flight requests.
    /// After this many failed reconnects, the connection stage fails with an exception.
    /// Default is 3.
    /// </summary>
    public int MaxReconnectAttempts { get; set; } = 3;

}

