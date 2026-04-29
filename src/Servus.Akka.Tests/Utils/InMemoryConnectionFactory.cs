using Servus.Akka.IO;

namespace Servus.Akka.Tests.Utils;

internal sealed class InMemoryConnectionFactory : IConnectionFactory
{
    private readonly List<ConnectionLease> _established = [];

    public IReadOnlyList<ConnectionLease> EstablishedLeases => _established;

    public Task<ConnectionLease> EstablishAsync(ITransportOptions options, RequestEndpoint endpoint,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var state = new ClientState(Stream.Null);

        var handle = ConnectionHandle.CreateDirect(
            state.OutboundWriter,
            state.InboundReader,
            endpoint);

        var lease = new ConnectionLease(handle, state);
        _established.Add(lease);
        return Task.FromResult(lease);
    }
}