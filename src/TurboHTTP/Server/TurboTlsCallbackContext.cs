using System.Net;
using System.Net.Security;

namespace TurboHTTP.Server;

public sealed class TurboTlsCallbackContext(
    SslClientHelloInfo clientHelloInfo,
    EndPoint localEndPoint,
    EndPoint remoteEndPoint,
    CancellationToken cancellationToken)
{
    public SslClientHelloInfo ClientHelloInfo { get; } = clientHelloInfo;
    public EndPoint LocalEndPoint { get; } = localEndPoint;
    public EndPoint RemoteEndPoint { get; } = remoteEndPoint;
    public CancellationToken CancellationToken { get; } = cancellationToken;
    public bool AllowDelayedClientCertificateNegotiation { get; set; }
}
