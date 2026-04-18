using System.Net;
using System.Threading.Channels;
using Akka.Actor;
using TurboHTTP.Internal;
using TurboHTTP.Transport.Connection;

namespace TurboHTTP.Tests.Transport;

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

    [Fact(Timeout = 5000)]
    public void CloseKind_should_default_to_zero()
    {
        var handle = CreateHandle();

        Assert.Equal(default, handle.CloseKind);
    }

    [Fact(Timeout = 5000)]
    public void SetCloseKind_should_update_close_kind()
    {
        var handle = CreateHandle();

        handle.SetCloseKind(TlsCloseKind.CleanClose);

        Assert.Equal(TlsCloseKind.CleanClose, handle.CloseKind);
    }

    [Fact(Timeout = 5000)]
    public void SetCloseKind_should_allow_multiple_updates()
    {
        var handle = CreateHandle();

        handle.SetCloseKind(TlsCloseKind.CleanClose);
        Assert.Equal(TlsCloseKind.CleanClose, handle.CloseKind);

        handle.SetCloseKind(TlsCloseKind.AbruptClose);
        Assert.Equal(TlsCloseKind.AbruptClose, handle.CloseKind);
    }

    [Fact(Timeout = 5000)]
    public void CreateDirect_should_create_handle_with_nobody_actor()
    {
        var outbound = Channel.CreateUnbounded<NetworkBuffer>();
        var inbound = Channel.CreateUnbounded<NetworkBuffer>();
        var key = new RequestEndpoint
        {
            Host = "example.com",
            Port = 8080,
            Scheme = "http",
            Version = HttpVersion.Version11
        };

        var handle = ConnectionHandle.CreateDirect(outbound.Writer, inbound.Reader, key);

        Assert.Equal(ActorRefs.Nobody, handle.ConnectionActor);
        Assert.Same(outbound.Writer, handle.OutboundWriter);
        Assert.Same(inbound.Reader, handle.InboundReader);
        Assert.Equal(key, handle.Key);
    }

    [Fact(Timeout = 5000)]
    public void CreateDirect_should_create_handle_with_default_max_concurrent_streams()
    {
        var outbound = Channel.CreateUnbounded<NetworkBuffer>();
        var inbound = Channel.CreateUnbounded<NetworkBuffer>();
        var key = new RequestEndpoint
        {
            Host = "example.com",
            Port = 8080,
            Scheme = "http",
            Version = HttpVersion.Version11
        };

        var handle = ConnectionHandle.CreateDirect(outbound.Writer, inbound.Reader, key);

        Assert.Equal(100, handle.MaxConcurrentStreams);
    }

    [Fact(Timeout = 5000)]
    public void Key_property_should_be_preserved()
    {
        var handle = CreateHandle();
        var expectedKey = handle.Key;

        Assert.Equal("localhost", expectedKey.Host);
        Assert.Equal((ushort)443, expectedKey.Port);
        Assert.Equal("https", expectedKey.Scheme);
        Assert.Equal(HttpVersion.Version20, expectedKey.Version);
    }

    [Fact(Timeout = 5000)]
    public void ConnectionActor_property_should_be_set()
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

        var handle = new ConnectionHandle(outbound.Writer, inbound.Reader, key, ActorRefs.Nobody);

        Assert.Equal(ActorRefs.Nobody, handle.ConnectionActor);
    }
}