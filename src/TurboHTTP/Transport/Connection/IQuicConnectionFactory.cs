using TurboHTTP.Internal;
using TurboHTTP.Transport.Quic;

namespace TurboHTTP.Transport.Connection;

internal interface IQuicConnectionFactory
{
    Task<QuicConnectionLease> EstablishAsync(QuicOptions options, RequestEndpoint endpoint, CancellationToken ct);
}
