using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace TurboHTTP.Client;

/// <summary>
/// Snapshot of <see cref="TurboHttpClient"/> configuration captured at request-submission time.
/// Passed into the pipeline so that per-request options reflect the values set on the client at the moment of submission.
/// </summary>
public record TurboRequestOptions(
    Uri? BaseAddress,
    HttpRequestHeaders DefaultRequestHeaders,
    Version DefaultRequestVersion,
    HttpVersionPolicy DefaultVersionPolicy,
    TimeSpan Timeout,
    ICredentials? Credentials = null,
    bool PreAuthenticate = false);

/// <summary>
/// Configuration for a <see cref="TurboHttpClient"/> instance.
/// Property names and defaults are aligned with <see cref="System.Net.Http.SocketsHttpHandler"/>
/// where applicable, so TurboHttp is a familiar drop-in for existing HttpClient users.
/// </summary>
public sealed class TurboClientOptions
{
    /// <summary>Base address used to resolve relative request URIs.</summary>
    public Uri? BaseAddress { get; set; }

    /// <summary>HTTP/1.x-specific configuration.</summary>
    public Http1Options Http1 { get; init; } = new();

    /// <summary>HTTP/2-specific configuration.</summary>
    public Http2Options Http2 { get; init; } = new();

    /// <summary>HTTP/3-specific configuration.</summary>
    public Http3Options Http3 { get; init; } = new();

    /// <summary>
    /// Maximum response body size (in bytes) that will be buffered in memory.
    /// Bodies larger than this are streamed. Default is 4 MB.
    /// </summary>
    public long MaxBufferedBodySize { get; set; } = 4 * 1024 * 1024L;

    /// <summary>
    /// Maximum response body size (in bytes) when streaming.
    /// Null means unlimited. Default is null.
    /// </summary>
    public long? MaxStreamedBodySize { get; set; } = null;

    /// <summary>
    /// Timeout for establishing a new TCP connection.
    /// Default is 15 seconds.
    /// </summary>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Time a connection may remain idle in the pool before it is evicted.
    /// Default is 90 seconds.
    /// </summary>
    public TimeSpan PooledConnectionIdleTimeout { get; set; } = TimeSpan.FromSeconds(90);

    /// <summary>
    /// Maximum lifetime of a connection in the pool, regardless of activity.
    /// When a connection exceeds this age it is evicted on the next pool sweep
    /// and not handed out for new requests.
    /// Default is <see cref="Timeout.InfiniteTimeSpan"/> (no lifetime limit).
    /// </summary>
    public TimeSpan PooledConnectionLifetime { get; set; } = Timeout.InfiniteTimeSpan;

    /// <summary>
    /// Maximum number of distinct endpoint substreams (identified by <c>(scheme, host, port, version)</c>)
    /// that may be active concurrently. Controls the ceiling for per-endpoint multiplexing and connection pooling.
    /// Must be at least 1. Default is 256. TurboHTTP-specific.
    /// </summary>
    public uint MaxEndpointSubstreams { get; set; } = 256;

    /// <summary>
    /// TLS protocol versions to enable. Defaults to <see cref="SslProtocols.None"/>,
    /// which lets the OS choose the best available protocol.
    /// </summary>
    public SslProtocols EnabledSslProtocols { get; set; } = SslProtocols.None;

    /// <summary>Client certificates presented during TLS handshake. <see langword="null"/> means no client certificate.</summary>
    public X509CertificateCollection? ClientCertificates { get; set; }

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
    /// Whether to route requests through a proxy.
    /// When <see langword="true"/> and <see cref="Proxy"/> is set, requests are
    /// tunnelled through the configured proxy. Default is <see langword="true"/>.
    /// </summary>
    public bool UseProxy { get; set; } = true;

    /// <summary>
    /// The web proxy to use when <see cref="UseProxy"/> is <see langword="true"/>.
    /// When <see langword="null"/>, no proxy is used regardless of <see cref="UseProxy"/>.
    /// Default is <see langword="null"/>.
    /// </summary>
    public IWebProxy? Proxy { get; set; }

    /// <summary>
    /// Default credentials sent to the proxy for authentication.
    /// Only used when <see cref="UseProxy"/> is <see langword="true"/> and
    /// <see cref="Proxy"/> is set. Default is <see langword="null"/>.
    /// </summary>
    public ICredentials? DefaultProxyCredentials { get; set; }

    /// <summary>
    /// Credentials for server authentication (e.g., Basic, Digest).
    /// When set, the <c>Authorization</c> header is injected into requests
    /// during enrichment. Default is <see langword="null"/>.
    /// </summary>
    public ICredentials? Credentials { get; set; }

    /// <summary>
    /// When <see langword="true"/>, an <c>Authorization</c> header is sent with
    /// the initial request instead of waiting for a 401 challenge.
    /// Only effective when <see cref="Credentials"/> is set. Default is <see langword="false"/>.
    /// </summary>
    public bool PreAuthenticate { get; set; }

    /// <summary>
    /// Returns the effective certificate validation callback, taking
    /// <see cref="DangerousAcceptAnyServerCertificate"/> into account.
    /// </summary>
    public RemoteCertificateValidationCallback? EffectiveServerCertificateValidationCallback
        => DangerousAcceptAnyServerCertificate
            ? static (_, _, _, _) => true
            : ServerCertificateValidationCallback;
}