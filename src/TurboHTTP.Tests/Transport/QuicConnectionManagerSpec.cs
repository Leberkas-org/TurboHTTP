using System.Net;
using System.Runtime.Versioning;
using TurboHTTP.Internal;
using TurboHTTP.Transport.Quic;
using TurboHTTP.Transport.Connection;

namespace TurboHTTP.Tests.Transport;

/// <summary>
/// Tests <see cref="QuicConnectionHandle"/> — per-connection stream-opening,
/// inbound-stream acceptance, and provider disposal.
/// </summary>
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macOS")]
[SupportedOSPlatform("windows")]
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
        Port = 8443,
        MaxFrameSize = 16384,
    };

    private static QuicConnectionHandle CreateHandle(FakeClientProvider provider)
        => new(provider, CreateTestOptions(), TestEndpoint);

    [Fact(Timeout = 5000)]
    public async Task QuicConnectionHandle_should_return_live_lease_when_opening_request_stream()
    {
        var provider = new FakeClientProvider();
        await using var handle = CreateHandle(provider);

        var lease = await handle.OpenStreamAsLeaseAsync(OutputStreamType.Request, TestContext.Current.CancellationToken);

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

        var lease = await handle.OpenStreamAsLeaseAsync(OutputStreamType.Control, TestContext.Current.CancellationToken);

        Assert.NotNull(lease);
        Assert.True(lease.IsAlive);

        lease.Dispose();
    }

    [Fact(Timeout = 5000)]
    public async Task QuicConnectionHandle_should_return_live_lease_when_opening_qpack_encoder_stream()
    {
        var provider = new FakeClientProvider();
        await using var handle = CreateHandle(provider);

        var lease = await handle.OpenStreamAsLeaseAsync(OutputStreamType.QpackEncoder, TestContext.Current.CancellationToken);

        Assert.NotNull(lease);
        Assert.True(lease.IsAlive);

        lease.Dispose();
    }

    [Fact(Timeout = 5000)]
    public async Task QuicConnectionHandle_should_reuse_provider_across_multiple_stream_opens()
    {
        var provider = new FakeClientProvider();
        await using var handle = CreateHandle(provider);

        var lease1 = await handle.OpenStreamAsLeaseAsync(OutputStreamType.Request, TestContext.Current.CancellationToken);
        var lease2 = await handle.OpenStreamAsLeaseAsync(OutputStreamType.Control, TestContext.Current.CancellationToken);

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
            handle.OpenStreamAsLeaseAsync(OutputStreamType.Request, TestContext.Current.CancellationToken),
            handle.OpenStreamAsLeaseAsync(OutputStreamType.Control, TestContext.Current.CancellationToken),
            handle.OpenStreamAsLeaseAsync(OutputStreamType.QpackEncoder, TestContext.Current.CancellationToken),
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

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => handle.OpenStreamAsLeaseAsync(OutputStreamType.Request, cts.Token));
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

        var lease = await handle.OpenStreamAsLeaseAsync(OutputStreamType.Request, TestContext.Current.CancellationToken);
        var inbound = new QuicConnectionHandle.InboundStream(lease, InputStreamType.Control);

        Assert.Same(lease, inbound.Lease);
        Assert.Equal(InputStreamType.Control, inbound.StreamType);

        lease.Dispose();
    }

    private sealed class FakeClientProvider(bool blockGetStream = false, byte[]? inboundBytes = null)
        : IClientProvider
    {
        private int _streamsOpened;

        public int StreamsOpened => _streamsOpened;
        public EndPoint? RemoteEndPoint => new IPEndPoint(IPAddress.Loopback, 8443);
        public bool SupportsMultipleStreams => true;

        public Task<Stream> GetStreamAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (blockGetStream)
            {
                return Task.Delay(Timeout.Infinite, ct).ContinueWith<Stream>(_ =>
                    throw new OperationCanceledException(ct), ct);
            }

            Interlocked.Increment(ref _streamsOpened);
            return Task.FromResult<Stream>(new MemoryStream());
        }

        public Task<Stream> GetUnidirectionalStreamAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _streamsOpened);
            return Task.FromResult<Stream>(new MemoryStream());
        }

        public Task<Stream> AcceptInboundStreamAsync(CancellationToken ct = default)
        {
            if (inboundBytes is not null)
            {
                return Task.FromResult<Stream>(new MemoryStream(inboundBytes));
            }

            // Block until cancelled — simulates waiting for server-initiated streams
            return Task.Delay(Timeout.Infinite, ct).ContinueWith<Stream>(_ =>
                throw new OperationCanceledException(ct), ct);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
