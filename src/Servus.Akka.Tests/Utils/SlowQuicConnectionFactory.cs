using Servus.Akka.IO;
using Servus.Akka.IO.Quic;

#pragma warning disable CA1416

namespace Servus.Akka.Tests.Utils;

/// <summary>
/// A test QUIC factory that intentionally ignores the cancellation token during establish,
/// simulating a slow network that completes after the caller has already cancelled their request.
/// Used to exercise the OnEstablished path where TrySetResult returns false.
/// </summary>
internal sealed class SlowQuicConnectionFactory(TimeSpan delay) : IQuicConnectionFactory
{
    public async Task<QuicConnectionLease> EstablishAsync(QuicOptions options, RequestEndpoint endpoint, CancellationToken ct)
    {
        // Deliberately ignore ct — simulates a slow network that doesn't respect cancellation.
        await Task.Delay(delay, CancellationToken.None).ConfigureAwait(false);

        var provider = new FakeClientProvider();
        var handle = new QuicConnectionHandle(provider, options, endpoint);
        return new QuicConnectionLease(handle);
    }
}
