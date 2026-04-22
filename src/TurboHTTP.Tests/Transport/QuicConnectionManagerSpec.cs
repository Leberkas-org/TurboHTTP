using TurboHTTP.Internal;
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

        var lease = await handle.OpenStreamAsLeaseAsync(bidirectional: true,
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

        var lease = await handle.OpenStreamAsLeaseAsync(bidirectional: false,
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

        var lease = await handle.OpenStreamAsLeaseAsync(bidirectional: false,
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
            await handle.OpenStreamAsLeaseAsync(bidirectional: true, TestContext.Current.CancellationToken);
        var lease2 =
            await handle.OpenStreamAsLeaseAsync(bidirectional: false, TestContext.Current.CancellationToken);

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
            handle.OpenStreamAsLeaseAsync(bidirectional: true, TestContext.Current.CancellationToken),
            handle.OpenStreamAsLeaseAsync(bidirectional: false, TestContext.Current.CancellationToken),
            handle.OpenStreamAsLeaseAsync(bidirectional: false, TestContext.Current.CancellationToken),
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
            handle.OpenStreamAsLeaseAsync(bidirectional: true, cts.Token));
    }

    [Fact(Timeout = 5000)]
    public async Task QuicConnectionHandle_should_pass_through_unknown_inbound_stream_type()
    {
        var provider = new FakeClientProvider(inboundBytes: [0xFF, 0x00]);
        await using var handle = CreateHandle(provider);

        var result = await handle.AcceptInboundStreamAsLeaseAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(0xFF, result.StreamTypeValue);

        result.Lease.Dispose();
    }

    [Fact(Timeout = 5000)]
    public async Task InboundStream_record_should_hold_lease_and_stream_type()
    {
        var provider = new FakeClientProvider();
        await using var handle = CreateHandle(provider);

        var lease = await handle.OpenStreamAsLeaseAsync(bidirectional: true,
            TestContext.Current.CancellationToken);
        var inbound = new QuicConnectionHandle.InboundStream(lease, 0x00, 3);

        Assert.Same(lease, inbound.Lease);
        Assert.Equal(0x00, inbound.StreamTypeValue);

        lease.Dispose();
    }

    [Fact(Timeout = 5000)]
    public async Task AcceptInboundStreamAsLeaseAsync_should_return_control_stream()
    {
        var provider = new FakeClientProvider(inboundBytes: [0x00]);
        await using var handle = CreateHandle(provider);

        var result = await handle.AcceptInboundStreamAsLeaseAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(0x00, result.StreamTypeValue);
        Assert.True(result.Lease.IsAlive);

        result.Lease.Dispose();
    }

    [Fact(Timeout = 5000)]
    public async Task AcceptInboundStreamAsLeaseAsync_should_return_qpack_encoder_stream()
    {
        var provider = new FakeClientProvider(inboundBytes: [0x02]);
        await using var handle = CreateHandle(provider);

        var result = await handle.AcceptInboundStreamAsLeaseAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(0x02, result.StreamTypeValue);

        result.Lease.Dispose();
    }

    [Fact(Timeout = 5000)]
    public async Task AcceptInboundStreamAsLeaseAsync_should_return_qpack_decoder_stream()
    {
        var provider = new FakeClientProvider(inboundBytes: [0x03]);
        await using var handle = CreateHandle(provider);

        var result = await handle.AcceptInboundStreamAsLeaseAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(0x03, result.StreamTypeValue);

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
    public async Task DisposeAsync_should_dispose_provider()
    {
        var provider = new FakeClientProvider();
        var handle = CreateHandle(provider);

        await handle.DisposeAsync();

        Assert.True(provider.Disposed);
    }
}
