using TurboHTTP.Internal;

namespace TurboHTTP.Transport.Connection;

internal interface IConnectionFactory
{
    Task<ConnectionLease> EstablishAsync(TcpOptions options, RequestEndpoint endpoint, CancellationToken ct);
}
