using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Servus.Akka.Tests.Utils;

public sealed class LoopbackQuicServer : IAsyncDisposable
{
    public static SslApplicationProtocol Alpn => new("h3");
    private readonly QuicListener _listener;
    private readonly X509Certificate2 _cert;
    public int Port { get; }

    private LoopbackQuicServer(QuicListener listener, X509Certificate2 cert, int port)
    {
        _listener = listener;
        _cert = cert;
        Port = port;
    }

    public static async Task<LoopbackQuicServer> CreateAsync()
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var cert = req.CreateSelfSigned(DateTimeOffset.Now, DateTimeOffset.Now.AddHours(1));

        var protocols = new List<SslApplicationProtocol> { Alpn };

        var listener = await QuicListener.ListenAsync(new QuicListenerOptions
        {
            ListenEndPoint = new IPEndPoint(IPAddress.Loopback, 0),
            ApplicationProtocols = protocols,
            ConnectionOptionsCallback = (_, _, _) => ValueTask.FromResult(new QuicServerConnectionOptions
            {
                DefaultStreamErrorCode = 0x0100,
                DefaultCloseErrorCode = 0x0100,
                ServerAuthenticationOptions = new SslServerAuthenticationOptions
                {
                    ServerCertificate = cert,
                    ApplicationProtocols = protocols
                }
            })
        });

        var port = listener.LocalEndPoint.Port;
        return new LoopbackQuicServer(listener, cert, port);
    }

    public async Task<QuicConnection> AcceptConnectionAsync(CancellationToken ct = default)
    {
        return await _listener.AcceptConnectionAsync(ct);
    }

    public async ValueTask DisposeAsync()
    {
        await _listener.DisposeAsync();
        _cert.Dispose();
    }
}