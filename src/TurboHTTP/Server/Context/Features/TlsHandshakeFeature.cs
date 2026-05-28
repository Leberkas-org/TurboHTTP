using System.Net.Security;
using System.Security.Authentication;

namespace TurboHTTP.Server.Context.Features;

internal sealed class TlsHandshakeFeature : ITlsHandshakeFeature
{
    public SslProtocols Protocol { get; init; }
    public TlsCipherSuite? NegotiatedCipherSuite { get; init; }
    public string? HostName { get; init; }
    public SslApplicationProtocol NegotiatedApplicationProtocol { get; init; }
}
