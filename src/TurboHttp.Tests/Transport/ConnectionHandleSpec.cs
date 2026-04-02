using System.Net;
using System.Threading.Channels;
using Akka.Actor;
using TurboHttp.Internal;
using TurboHttp.Transport.Connection;

namespace TurboHttp.Tests.Transport;

/// <summary>
/// Tests the <see cref="ConnectionHandle"/> record that bundles channel reader/writer pairs with connection identity.
/// Verifies property accessors, channel I/O, and HTTP version reflection from the RequestEndpoint.
/// </summary>
/// <remarks>
/// Actor under test: <see cref="ConnectionHandle"/>.
/// Validates that the handle correctly exposes the underlying channel pair and host key.
/// </remarks>
public sealed class ConnectionHandleSpec
{
    private ConnectionHandle CreateHandle()
    {
        var outbound = Channel.CreateUnbounded<NetworkBuffer>();
        var inbound = Channel.CreateUnbounded<NetworkBuffer>();
        var key = new RequestEndpoint
        {
            Host = "localhost",
            Port = 443,
            Scheme = "https",
            Version = HttpVersion.Version20
        };

        return new ConnectionHandle(outbound.Writer, inbound.Reader, key, ActorRefs.Nobody);
    }

    [Fact(Timeout = 5000)]
    public void ConnectionHandle_should_default_to_100_when_created()
    {
        var handle = CreateHandle();

        Assert.Equal(100, handle.MaxConcurrentStreams);
    }

    [Fact(Timeout = 5000)]
    public void ConnectionHandle_should_set_new_value_when_update_max_concurrent_streams_called()
    {
        var handle = CreateHandle();

        handle.UpdateMaxConcurrentStreams(42);

        Assert.Equal(42, handle.MaxConcurrentStreams);
    }

    [Fact(Timeout = 5000)]
    public void ConnectionHandle_should_not_affect_equality_when_max_concurrent_streams_updated()
    {
        var outbound = Channel.CreateUnbounded<NetworkBuffer>();
        var inbound = Channel.CreateUnbounded<NetworkBuffer>();
        var key = new RequestEndpoint
        {
            Host = "localhost",
            Port = 443,
            Scheme = "https",
            Version = HttpVersion.Version20
        };

        var handle1 = new ConnectionHandle(outbound.Writer, inbound.Reader, key, ActorRefs.Nobody);
        var handle2 = new ConnectionHandle(outbound.Writer, inbound.Reader, key, ActorRefs.Nobody);

        handle1.UpdateMaxConcurrentStreams(1);
        handle2.UpdateMaxConcurrentStreams(999);

        // Records with same constructor args should be equal regardless of volatile field
        Assert.Equal(handle1, handle2);
        Assert.Equal(handle1.GetHashCode(), handle2.GetHashCode());
    }

    [Fact(Timeout = 5000)]
    public async Task ConnectionHandle_should_not_throw_when_concurrent_writes_and_reads_occur()
    {
        var handle = CreateHandle();
        const int iterations = 10_000;
        const int targetValue = 256;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // Writer task: rapidly update the value
        var writerTask = Task.Run(() =>
        {
            for (var i = 1; i <= iterations; i++)
            {
                handle.UpdateMaxConcurrentStreams(i);
            }

            handle.UpdateMaxConcurrentStreams(targetValue);
        }, cts.Token);

        // Reader task: rapidly read the value — should never throw
        var readerTask = Task.Run(() =>
        {
            var lastSeen = 0;
            for (var i = 0; i < iterations; i++)
            {
                var value = handle.MaxConcurrentStreams;
                Assert.True(value > 0, $"Expected positive value but got {value}");
                lastSeen = value;
            }

            return lastSeen;
        }, cts.Token);

        await Task.WhenAll(writerTask, readerTask);

        // After writer completes, the final value should be eventually visible
        Assert.Equal(targetValue, handle.MaxConcurrentStreams);
    }
}
