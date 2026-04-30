using Servus.Akka.Transport;
using Servus.Akka.Transport.Quic;

namespace Servus.Akka.Tests.Transport.Quic;

internal sealed class InMemoryQuicConnectionFactory : IQuicConnectionFactory
{
    public int EstablishCount;
    public bool ShouldFail = false;

    public Task<QuicConnectionLease> EstablishAsync(QuicTransportOptions options, CancellationToken ct = default)
    {
        Interlocked.Increment(ref EstablishCount);
        if (ShouldFail)
        {
            return Task.FromException<QuicConnectionLease>(new IOException("Simulated failure"));
        }

        var handle = CreateMockHandle();
        return Task.FromResult(new QuicConnectionLease(handle, options.MaxBidirectionalStreams));
    }

    private static QuicConnectionHandle CreateMockHandle()
    {
        return new QuicConnectionHandle(
            openStream: (_, _) => Task.FromResult((Stream: (Stream)new MemoryStream(), StreamId: 0L)),
            acceptInboundStream: _ => Task.FromResult<(Stream, long)?>(null),
            getLocalEndPoint: () => null,
            dispose: () => ValueTask.CompletedTask);
    }
}