using TurboHTTP.Internal;

namespace TurboHTTP.Transport.Connection;

internal interface IConnectionFactory
{
    Task<ConnectionLease> EstablishAsync(ITransportOptions options, RequestEndpoint endpoint, CancellationToken ct);
}
