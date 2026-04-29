using Servus.Akka.IO;
using Servus.Akka.IO.Quic;

#pragma warning disable CA1416

namespace Servus.Akka.Tests.Utils;

internal sealed class InMemoryQuicConnectionFactory : IQuicConnectionFactory
{
    private readonly List<QuicConnectionLease> _established = [];

    public IReadOnlyList<QuicConnectionLease> EstablishedLeases => _established;

    public Task<QuicConnectionLease> EstablishAsync(QuicOptions options, RequestEndpoint endpoint, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var provider = new FakeClientProvider();
        var handle = new QuicConnectionHandle(provider, options, endpoint);
        var lease = new QuicConnectionLease(handle);
        _established.Add(lease);
        return Task.FromResult(lease);
    }
}