using System;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using TurboHttp.IO;
using TurboHttp.Protocol.RFC9110;
using TurboHttp.Protocol.RFC9111;
using TurboHttp.Protocol.RFC9112;

namespace TurboHttp.Client;

public record TurboClientOptions
{
    public Uri? BaseAddress { get; init; }
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan ReconnectInterval { get; init; } = TimeSpan.FromSeconds(5);
    public int MaxReconnectAttempts { get; init; } = 10;
    public int MaxFrameSize { get; init; } = 128 * 1024;

    public RemoteCertificateValidationCallback? ServerCertificateValidationCallback { get; init; } =
        static (_, _, _, sslPolicyErrors) => sslPolicyErrors is SslPolicyErrors.None;

    public X509CertificateCollection? ClientCertificates { get; init; }
    public SslProtocols EnabledSslProtocols { get; init; } = SslProtocols.None;
    public RedirectPolicy? RedirectPolicy { get; init; }
    public RetryPolicy? RetryPolicy { get; init; }
    public CachePolicy? CachePolicy { get; init; }
    public ConnectionPolicy? ConnectionPolicy { get; init; }
    public PoolConfig PoolConfig { get; init; } = new PoolConfig();
}