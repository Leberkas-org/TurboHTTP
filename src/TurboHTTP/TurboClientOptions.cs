using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace TurboHTTP;

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

    /// <summary>HTTP/1.x-specific configuration.</summary>
    public Http1Options Http1 { get; set; } = new();

    /// <summary>HTTP/2-specific configuration.</summary>
    public Http2Options Http2 { get; set; } = new();

    /// <summary>HTTP/3-specific configuration.</summary>
    public Http3Options Http3 { get; set; } = new();

    /// <summary>
    /// Maximum batch weight in bytes for HTTP/2 frame encoding.
    /// Frames are accumulated into batches up to this weight before being serialized into a single buffer,
    /// reducing allocations and memory copies under concurrent load. Higher values increase throughput
    /// at the cost of latency variance. Default is 256 KiB. TurboHttp-specific.
    /// </summary>
    public long Http2MaxBatchWeight { get; set; } = 262_144;

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

    /// <summary>
    /// TCP/QUIC socket send buffer size in bytes. When <see langword="null"/>, the OS default is used.
    /// Operators can tune this for their network environment. Default is <see langword="null"/>.
    /// </summary>
    public int? SocketSendBufferSize { get; set; }

    /// <summary>
    /// TCP/QUIC socket receive buffer size in bytes. When <see langword="null"/>, the OS default is used.
    /// Operators can tune this for their network environment. Default is <see langword="null"/>.
    /// </summary>
    public int? SocketReceiveBufferSize { get; set; }

    /// <summary>
    /// Maximum number of pooled <see cref="TurboHTTP.Internal.NetworkBuffer"/> wrapper objects to retain.
    /// When the pool reaches this capacity, excess wrappers are discarded on return instead of being pooled.
    /// Prevents unbounded pool growth under bursty load.
    /// Default is <c>Environment.ProcessorCount * 2</c>. TurboHttp-specific.
    /// </summary>
    public int NetworkBufferPoolSize { get; set; } = Environment.ProcessorCount * 2;
}