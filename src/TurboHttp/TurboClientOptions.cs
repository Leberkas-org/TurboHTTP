using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace TurboHttp;

/// <summary>
/// Configuration for a <see cref="TurboHttpClient"/> instance.
/// Property names and defaults are aligned with <see cref="System.Net.Http.SocketsHttpHandler"/>
/// where applicable, so TurboHttp is a familiar drop-in for existing HttpClient users.
/// </summary>
public sealed class TurboClientOptions
{
    /// <summary>Base address used to resolve relative request URIs.</summary>
    public Uri? BaseAddress { get; set; }

    /// <summary>
    /// Timeout for establishing a new TCP connection.
    /// Default is 15 seconds (same as <c>SocketsHttpHandler.ConnectTimeout</c>).
    /// </summary>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Time a connection may remain idle in the pool before it is evicted.
    /// Default is 90 seconds (same as <c>SocketsHttpHandler.PooledConnectionIdleTimeout</c>).
    /// </summary>
    public TimeSpan PooledConnectionIdleTimeout { get; set; } = TimeSpan.FromSeconds(90);

    /// <summary>
    /// Maximum number of concurrent TCP connections per server for HTTP/1.x.
    /// Each connection is managed as an independent substream.
    /// Default is 6 (matching browser defaults and RFC 9112 §9.4 guidance).
    /// </summary>
    /// <remarks>
    /// <c>SocketsHttpHandler</c> does not expose this directly but defaults to
    /// <c>int.MaxValue</c>. TurboHttp uses 6 to match browser conventions.
    /// For HTTP/2, multiple connections are opened when a single connection's
    /// <c>MAX_CONCURRENT_STREAMS</c> limit is reached (similar to
    /// <c>SocketsHttpHandler.EnableMultipleHttp2Connections</c>).
    /// </remarks>
    public int MaxH1ConnectionsPerServer { get; set; } = 6;

    /// <summary>
    /// Maximum number of pipelined HTTP/1.1 requests allowed per connection before waiting for responses.
    /// Higher values increase throughput on high-latency links; lower values reduce head-of-line blocking.
    /// Default is 16. TurboHttp-specific (no <c>HttpClient</c> equivalent).
    /// </summary>
    public int MaxPipelineDepth { get; set; } = 16;

    /// <summary>Maximum HTTP/2 frame size in bytes. Default is 128 KiB.</summary>
    public int MaxFrameSize { get; set; } = 128 * 1024;

    /// <summary>
    /// Maximum number of concurrent HTTP/2 streams per connection.
    /// Controls how many requests can be in-flight simultaneously on a single H/2 TCP connection,
    /// enabling true request multiplexing within each substream.
    /// Default is 100. TurboHttp-specific.
    /// </summary>
    public int MaxH2ConcurrentStreams { get; set; } = 100;

    /// <summary>
    /// Maximum number of concurrent TCP connections per server for HTTP/2.
    /// HTTP/2 multiplexes many streams over a single connection, so far fewer connections
    /// are optimal compared to HTTP/1.x. Default is 2.
    /// TurboHttp-specific (analogous to <c>SocketsHttpHandler.EnableMultipleHttp2Connections</c>).
    /// </summary>
    public int MaxH2ConnectionsPerServer { get; set; } = 2;

    /// <summary>
    /// Maximum number of distinct endpoint substreams (identified by <c>(scheme, host, port, version)</c>)
    /// that may be active concurrently. Controls the ceiling for per-endpoint multiplexing and connection pooling.
    /// Must be at least 1. Default is 256. TurboHttp-specific.
    /// </summary>
    internal const uint DefaultMaxEndpointSubstreams = 256;

    public uint MaxEndpointSubstreams { get; set; } = DefaultMaxEndpointSubstreams;

    /// <summary>
    /// When <see langword="true"/>, all server certificates are accepted regardless of validation
    /// errors. Overrides <see cref="ServerCertificateValidationCallback"/>.
    /// Intended only for development or testing. Default is <see langword="false"/>.
    /// </summary>
    public bool DangerousAcceptAnyServerCertificate { get; set; }

    /// <summary>
    /// Callback invoked to validate the server's TLS certificate.
    /// Ignored when <see cref="DangerousAcceptAnyServerCertificate"/> is <see langword="true"/>.
    /// </summary>
    public RemoteCertificateValidationCallback? ServerCertificateValidationCallback { get; set; } =
        static (_, _, _, sslPolicyErrors) => sslPolicyErrors is SslPolicyErrors.None;

    /// <summary>
    /// Returns the effective certificate validation callback, taking
    /// <see cref="DangerousAcceptAnyServerCertificate"/> into account.
    /// </summary>
    internal RemoteCertificateValidationCallback? EffectiveServerCertificateValidationCallback
        => DangerousAcceptAnyServerCertificate
            ? static (_, _, _, _) => true
            : ServerCertificateValidationCallback;

    /// <summary>Client certificates presented during TLS handshake. <see langword="null"/> means no client certificate.</summary>
    public X509CertificateCollection? ClientCertificates { get; set; }

    /// <summary>
    /// TLS protocol versions to enable. Defaults to <see cref="SslProtocols.None"/>,
    /// which lets the OS choose the best available protocol.
    /// </summary>
    public SslProtocols EnabledSslProtocols { get; set; } = SslProtocols.None;
}