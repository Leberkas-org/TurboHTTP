namespace Servus.Akka.Transport.Quic;

public interface IQuicConnectionFactory
{
    Task<QuicConnectionLease> EstablishAsync(QuicTransportOptions options, CancellationToken ct);
}
