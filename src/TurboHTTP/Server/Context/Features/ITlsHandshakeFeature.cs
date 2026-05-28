using System.Net.Security;
using System.Security.Authentication;

namespace TurboHTTP.Server.Context.Features;

public interface ITlsHandshakeFeature
{
    SslProtocols Protocol { get; }
    TlsCipherSuite? NegotiatedCipherSuite { get; }
    string? HostName { get; }
    SslApplicationProtocol NegotiatedApplicationProtocol { get; }
}
