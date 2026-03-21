using System;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using TurboHttp.Protocol.RFC9110;
using TurboHttp.Protocol.RFC9111;
using TurboHttp.Protocol.RFC9112;

namespace TurboHttp.Client;

/// <summary>
/// Immutable configuration record for a <see cref="TurboHttpClient"/> instance.
/// Pass to <see cref="ITurboHttpClientFactory.CreateClient"/> or construct directly when
/// creating a <see cref="TurboHttpClient"/> without the DI factory.
/// </summary>
public record TurboClientOptions
{
    /// <summary>Base address used to resolve relative request URIs.</summary>
    public Uri? BaseAddress { get; init; }

    /// <summary>Timeout for establishing a new TCP connection. Default is 10 seconds.</summary>
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Delay between reconnection attempts after a connection failure. Default is 5 seconds.</summary>
    public TimeSpan ReconnectInterval { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Time a connection may remain idle before it is evicted from the pool. Default is 10 seconds.</summary>
    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Maximum number of reconnection attempts before the connection actor stops. Default is 10.</summary>
    public int MaxReconnectAttempts { get; init; } = 10;

    /// <summary>Maximum HTTP/2 frame size in bytes. Default is 128 KiB.</summary>
    public int MaxFrameSize { get; init; } = 128 * 1024;

    /// <summary>
    /// Callback invoked to validate the server's TLS certificate. Defaults to accepting only
    /// connections with no TLS policy errors (<see cref="SslPolicyErrors.None"/>).
    /// Set to <see langword="null"/> to accept any certificate (not recommended for production).
    /// </summary>
    public RemoteCertificateValidationCallback? ServerCertificateValidationCallback { get; init; } =
        static (_, _, _, sslPolicyErrors) => sslPolicyErrors is SslPolicyErrors.None;

    /// <summary>Client certificates presented during TLS handshake. <see langword="null"/> means no client certificate.</summary>
    public X509CertificateCollection? ClientCertificates { get; init; }

    /// <summary>
    /// TLS protocol versions to enable. Defaults to <see cref="SslProtocols.None"/>,
    /// which lets the OS choose the best available protocol.
    /// </summary>
    public SslProtocols EnabledSslProtocols { get; init; } = SslProtocols.None;

    /// <summary>Redirect-following policy. <see langword="null"/> disables automatic redirect handling.</summary>
    [Obsolete("Use .WithRedirect() on ITurboHttpClientBuilder instead.")]
    public RedirectPolicy? RedirectPolicy { get; init; }

    /// <summary>Retry policy for idempotent requests. <see langword="null"/> disables automatic retries.</summary>
    [Obsolete("Use .WithRetry() on ITurboHttpClientBuilder instead.")]
    public RetryPolicy? RetryPolicy { get; init; }

    /// <summary>HTTP caching policy. <see langword="null"/> disables the response cache.</summary>
    [Obsolete("Use .WithCache() on ITurboHttpClientBuilder instead.")]
    public CachePolicy? CachePolicy { get; init; }

    /// <summary>Connection management policy controlling per-host connection limits and HTTP/2 multiplexing.</summary>
    public ConnectionPolicy? ConnectionPolicy { get; init; }
}