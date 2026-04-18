using TurboHTTP.Internal;
using TurboHTTP.Protocol.Http3;
using TurboHTTP.Tests.Shared;
using TurboHTTP.Transport.Connection;

#pragma warning disable CA1416

namespace TurboHTTP.Tests.Transport;

public sealed class QuicConnectionManagerSpec
{
    private static readonly RequestEndpoint TestEndpoint = new()
    {
        Host = "localhost",
        Port = 8443,
        Scheme = "https",
        Version = new Version(3, 0)
    };

    private static QuicOptions CreateTestOptions() => new()
    {
        Host = "localhost",
        Port = 8443
    };

    private static QuicConnectionHandle CreateHandle(FakeClientProvider provider)
        => new(provider, CreateTestOptions(), TestEndpoint);

    [Fact(Timeout = 5000)]
    public async Task QuicConnectionHandle_should_return_live_lease_when_opening_request_stream()
    {
        var provider = new FakeClientProvider();
        await using var handle = CreateHandle(provider);

        var lease = await handle.OpenStreamAsLeaseAsync(Http3StreamType.Request,
            TestContext.Current.CancellationToken);

        Assert.NotNull(lease);
        Assert.True(lease.IsAlive);
        Assert.Equal(TestEndpoint, lease.Key);

        lease.Dispose();
    }

    [Fact(Timeout = 5000)]
    public async Task QuicConnectionHandle_should_return_live_lease_when_opening_control_stream()
    {
        var provider = new FakeClientProvider();
        await using var handle = CreateHandle(provider);

        var lease = await handle.OpenStreamAsLeaseAsync(Http3StreamType.Control,
            TestContext.Current.CancellationToken);

        Assert.NotNull(lease);
        Assert.True(lease.IsAlive);

        lease.Dispose();
    }

    [Fact(Timeout = 5000)]
    public async Task QuicConnectionHandle_should_return_live_lease_when_opening_qpack_encoder_stream()
    {
        var provider = new FakeClientProvider();
        await using var handle = CreateHandle(provider);

        var lease = await handle.OpenStreamAsLeaseAsync(Http3StreamType.QpackEncoder,
            TestContext.Current.CancellationToken);

        Assert.NotNull(lease);
        Assert.True(lease.IsAlive);

        lease.Dispose();
    }

    [Fact(Timeout = 5000)]
    public async Task QuicConnectionHandle_should_reuse_provider_across_multiple_stream_opens()
    {
        var provider = new FakeClientProvider();
        await using var handle = CreateHandle(provider);

        var lease1 =
            await handle.OpenStreamAsLeaseAsync(Http3StreamType.Request, TestContext.Current.CancellationToken);
        var lease2 =
            await handle.OpenStreamAsLeaseAsync(Http3StreamType.Control, TestContext.Current.CancellationToken);

        Assert.True(lease1.IsAlive);
        Assert.True(lease2.IsAlive);
        Assert.Equal(2, provider.StreamsOpened);

        lease1.Dispose();
        lease2.Dispose();
    }

    [Fact(Timeout = 5000)]
    public async Task QuicConnectionHandle_should_open_streams_concurrently_without_deadlock()
    {
        var provider = new FakeClientProvider();
        await using var handle = CreateHandle(provider);

        var tasks = new[]
        {
            handle.OpenStreamAsLeaseAsync(Http3StreamType.Request, TestContext.Current.CancellationToken),
            handle.OpenStreamAsLeaseAsync(Http3StreamType.Control, TestContext.Current.CancellationToken),
            handle.OpenStreamAsLeaseAsync(Http3StreamType.QpackEncoder, TestContext.Current.CancellationToken),
        };

        var leases = await Task.WhenAll(tasks);

        Assert.Equal(3, leases.Length);
        Assert.All(leases, l => Assert.True(l.IsAlive));
        Assert.Equal(3, provider.StreamsOpened);

        foreach (var lease in leases)
        {
            lease.Dispose();
        }
    }

    [Fact(Timeout = 5000)]
    public async Task QuicConnectionHandle_should_throw_operation_canceled_when_open_cancelled()
    {
        var provider = new FakeClientProvider(blockGetStream: true);
        await using var handle = CreateHandle(provider);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            handle.OpenStreamAsLeaseAsync(Http3StreamType.Request, cts.Token));
    }

    [Fact(Timeout = 5000)]
    public async Task QuicConnectionHandle_should_return_null_for_unknown_inbound_stream_type()
    {
        // An inbound stream whose varint identifies an unknown type should be discarded (returns null).
        var provider = new FakeClientProvider(inboundBytes: [0xFF, 0x00]); // unrecognized stream type
        await using var handle = CreateHandle(provider);

        var result = await handle.AcceptInboundStreamAsLeaseAsync(TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact(Timeout = 5000)]
    public async Task InboundStream_record_should_hold_lease_and_stream_type()
    {
        var provider = new FakeClientProvider();
        await using var handle = CreateHandle(provider);

        var lease = await handle.OpenStreamAsLeaseAsync(Http3StreamType.Request,
            TestContext.Current.CancellationToken);
        var inbound = new QuicConnectionHandle.InboundStream(lease, Http3StreamType.Control);

        Assert.Same(lease, inbound.Lease);
        Assert.Equal(Http3StreamType.Control, inbound.StreamType);

        lease.Dispose();
    }

    [Fact(Timeout = 5000)]
    public async Task AcceptInboundStreamAsLeaseAsync_should_return_control_stream()
    {
        var controlVarint = new byte[1];
        QuicVarInt.Encode((long)StreamType.Control, controlVarint);

        var provider = new FakeClientProvider(inboundBytes: controlVarint);
        await using var handle = CreateHandle(provider);

        var result = await handle.AcceptInboundStreamAsLeaseAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(Http3StreamType.Control, result.StreamType);
        Assert.True(result.Lease.IsAlive);

        result.Lease.Dispose();
    }

    [Fact(Timeout = 5000)]
    public async Task AcceptInboundStreamAsLeaseAsync_should_return_qpack_encoder_stream()
    {
        var varint = new byte[1];
        QuicVarInt.Encode((long)StreamType.QpackEncoder, varint);

        var provider = new FakeClientProvider(inboundBytes: varint);
        await using var handle = CreateHandle(provider);

        var result = await handle.AcceptInboundStreamAsLeaseAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(Http3StreamType.QpackEncoder, result.StreamType);

        result.Lease.Dispose();
    }

    [Fact(Timeout = 5000)]
    public async Task AcceptInboundStreamAsLeaseAsync_should_return_qpack_decoder_stream()
    {
        var varint = new byte[1];
        QuicVarInt.Encode((long)StreamType.QpackDecoder, varint);

        var provider = new FakeClientProvider(inboundBytes: varint);
        await using var handle = CreateHandle(provider);

        var result = await handle.AcceptInboundStreamAsLeaseAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(Http3StreamType.QpackDecoder, result.StreamType);

        result.Lease.Dispose();
    }

    [Fact(Timeout = 5000)]
    public async Task AcceptInboundStreamAsLeaseAsync_should_return_null_for_empty_stream()
    {
        var provider = new FakeClientProvider(inboundBytes: []);
        await using var handle = CreateHandle(provider);

        var result = await handle.AcceptInboundStreamAsLeaseAsync(TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact(Timeout = 5000)]
    public async Task AcceptInboundStreamAsLeaseAsync_should_return_null_when_cancelled()
    {
        var provider = new FakeClientProvider();
        await using var handle = CreateHandle(provider);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var result = await handle.AcceptInboundStreamAsLeaseAsync(cts.Token);

        Assert.Null(result);
    }

    [Fact(Timeout = 5000)]
    public async Task OpenStreamAsLeaseAsync_should_throw_for_unknown_type()
    {
        var provider = new FakeClientProvider();
        await using var handle = CreateHandle(provider);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            handle.OpenStreamAsLeaseAsync(Http3StreamType.QpackDecoder, TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    public async Task DisposeAsync_should_dispose_provider()
    {
        var provider = new FakeClientProvider();
        var handle = CreateHandle(provider);

        await handle.DisposeAsync();

        Assert.True(provider.Disposed);
    }
}