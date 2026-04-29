using Servus.Akka.Transport;
using Servus.Akka.Transport.Tcp;

namespace Servus.Akka.Tests.Transport.Tcp;

internal sealed class SlowTcpConnectionFactory(TimeSpan delay) : ITcpConnectionFactory
{
    public async Task<ConnectionLease> EstablishAsync(TransportOptions options, CancellationToken ct)
    {
        await Task.Delay(delay, CancellationToken.None).ConfigureAwait(false);

        var state = new ClientState(Stream.Null);
        var cts = new CancellationTokenSource();
        var handle = new ConnectionHandle(state.OutboundWriter, state.InboundReader, cts.Token);
        return new ConnectionLease(handle, state, cts);
    }
}
