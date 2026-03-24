using System;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using TurboHttp.Protocol.RFC9112;

namespace TurboHttp;

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

    /// <summary>Time a connection may remain idle before it is evicted from the pool. Default is 10 seconds.</summary>
    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Maximum HTTP/2 frame size in bytes. Default is 128 KiB.</summary>
    public int MaxFrameSize { get; init; } = 128 * 1024;

    /// <summary>
    /// When <see langword="true"/>, all server certificates are accepted regardless of validation
    /// errors. This overrides <see cref="ServerCertificateValidationCallback"/> and is intended
    /// only for development or testing scenarios. Default is <see langword="false"/>.
    /// </summary>
    public bool DangerousAcceptAnyServerCertificate { get; init; }

    /// <summary>
    /// Callback invoked to validate the server's TLS certificate. Defaults to accepting only
    /// connections with no TLS policy errors (<see cref="SslPolicyErrors.None"/>).
    /// Ignored when <see cref="DangerousAcceptAnyServerCertificate"/> is <see langword="true"/>.
    /// </summary>
    public RemoteCertificateValidationCallback? ServerCertificateValidationCallback { get; init; } =
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
    public X509CertificateCollection? ClientCertificates { get; init; }

    /// <summary>
    /// TLS protocol versions to enable. Defaults to <see cref="SslProtocols.None"/>,
    /// which lets the OS choose the best available protocol.
    /// </summary>
    public SslProtocols EnabledSslProtocols { get; init; } = SslProtocols.None;

    /// <summary>Connection management policy controlling per-host connection limits and HTTP/2 multiplexing.</summary>
    public ConnectionPolicy? ConnectionPolicy { get; init; }
}