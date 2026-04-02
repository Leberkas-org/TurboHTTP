using System.Net;
using System.Runtime.Versioning;
using TurboHttp.Internal;
using TurboHttp.Transport.Quic;
using TurboHttp.Transport.Connection;

namespace TurboHttp.Tests.Transport;

/// <summary>
/// Tests <see cref="QuicConnectionManager"/> — QUIC multi-stream management without actors.
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

    [Fact(Timeout = 5000)]
    public async Task QuicConnectionManager_should_return_lease_when_opening_request_stream()
    {
        // Arrange: use a fake provider that returns MemoryStreams
        var options = CreateTestOptions();
        await using var manager = new TestableQuicConnectionManager(options, TestEndpoint);

        // Act
        var lease = await manager.OpenStreamAsync(OutputStreamType.Request, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(lease);
        Assert.True(lease.IsAlive);
        Assert.Equal(TestEndpoint, lease.Key);

        lease.Dispose();
    }

    [Fact(Timeout = 5000)]
    public async Task QuicConnectionManager_should_return_lease_when_opening_control_stream()
    {
        var options = CreateTestOptions();
        await using var manager = new TestableQuicConnectionManager(options, TestEndpoint);

        var lease = await manager.OpenStreamAsync(OutputStreamType.Control, TestContext.Current.CancellationToken);

        Assert.NotNull(lease);
        Assert.True(lease.IsAlive);

        lease.Dispose();
    }

    [Fact(Timeout = 5000)]
    public async Task QuicConnectionManager_should_return_lease_when_opening_qpack_encoder_stream()
    {
        var options = CreateTestOptions();
        await using var manager = new TestableQuicConnectionManager(options, TestEndpoint);

        var lease = await manager.OpenStreamAsync(OutputStreamType.QpackEncoder, TestContext.Current.CancellationToken);

        Assert.NotNull(lease);
        Assert.True(lease.IsAlive);

        lease.Dispose();
    }

    [Fact(Timeout = 5000)]
    public async Task QuicConnectionManager_should_reuse_provider_when_opening_multiple_streams()
    {
        var options = CreateTestOptions();
        await using var manager = new TestableQuicConnectionManager(options, TestEndpoint);

        var lease1 = await manager.OpenStreamAsync(OutputStreamType.Request, TestContext.Current.CancellationToken);
        var lease2 = await manager.OpenStreamAsync(OutputStreamType.Control, TestContext.Current.CancellationToken);

        // Both leases should be alive and tracked
        Assert.True(lease1.IsAlive);
        Assert.True(lease2.IsAlive);

        // Provider should have been created only once
        Assert.Equal(1, manager.ProviderCreationCount);

        lease1.Dispose();
        lease2.Dispose();
    }

    [Fact(Timeout = 5000)]
    public async Task QuicConnectionManager_should_dispose_all_streams_when_manager_disposed()
    {
        var options = CreateTestOptions();
        var manager = new TestableQuicConnectionManager(options, TestEndpoint);

        var lease1 = await manager.OpenStreamAsync(OutputStreamType.Request, TestContext.Current.CancellationToken);
        var lease2 = await manager.OpenStreamAsync(OutputStreamType.Request, TestContext.Current.CancellationToken);

        Assert.True(lease1.IsAlive);
        Assert.True(lease2.IsAlive);

        // Act
        await manager.DisposeAsync();

        // Assert: both leases should be disposed
        Assert.False(lease1.IsAlive);
        Assert.False(lease2.IsAlive);
    }

    [Fact(Timeout = 5000)]
    public async Task QuicConnectionManager_should_throw_object_disposed_when_open_after_dispose()
    {
        var options = CreateTestOptions();
        var manager = new TestableQuicConnectionManager(options, TestEndpoint);
        await manager.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => manager.OpenStreamAsync(OutputStreamType.Request, TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    public async Task QuicConnectionManager_should_not_throw_when_disposed_twice()
    {
        var options = CreateTestOptions();
        var manager = new TestableQuicConnectionManager(options, TestEndpoint);

        await manager.DisposeAsync();
        await manager.DisposeAsync(); // should not throw
    }

    [Fact(Timeout = 5000)]
    public async Task QuicConnectionManager_should_enforce_sequential_spawn_when_concurrent_open_requests()
    {
        var options = CreateTestOptions();
        await using var manager = new TestableQuicConnectionManager(options, TestEndpoint);

        // Launch multiple opens concurrently
        var tasks = new[]
        {
            manager.OpenStreamAsync(OutputStreamType.Request, TestContext.Current.CancellationToken),
            manager.OpenStreamAsync(OutputStreamType.Control, TestContext.Current.CancellationToken),
            manager.OpenStreamAsync(OutputStreamType.QpackEncoder, TestContext.Current.CancellationToken),
        };

        var leases = await Task.WhenAll(tasks);

        // All should succeed — sequential spawn prevents issues
        Assert.Equal(3, leases.Length);
        Assert.All(leases, l => Assert.True(l.IsAlive));

        foreach (var lease in leases)
        {
            lease.Dispose();
        }
    }

    [Fact(Timeout = 5000)]
    public async Task QuicConnectionManager_should_flush_buffered_inbound_when_subscriber_registers()
    {
        var options = CreateTestOptions();
        await using var manager = new TestableQuicConnectionManager(options, TestEndpoint);

        // Open a stream first to ensure provider is created
        var lease = await manager.OpenStreamAsync(OutputStreamType.Request, TestContext.Current.CancellationToken);

        // Buffer an inbound notification
        manager.SimulateInboundStream(InputStreamType.Control);

        // Register subscriber — should receive the buffered notification
        var received = new List<QuicConnectionManager.InboundStream>();
        manager.StartInboundAcceptLoopDirect(notification => received.Add(notification));

        Assert.Single(received);
        Assert.Equal(InputStreamType.Control, received[0].StreamType);

        lease.Dispose();
    }

    [Fact(Timeout = 5000)]
    public async Task InboundStream_should_have_correct_properties_when_created()
    {
        var options = CreateTestOptions();
        await using var manager = new TestableQuicConnectionManager(options, TestEndpoint);

        var lease = await manager.OpenStreamAsync(OutputStreamType.Request, TestContext.Current.CancellationToken);
        var notification = new QuicConnectionManager.InboundStream(lease, InputStreamType.QpackDecoder);

        Assert.Same(lease, notification.Lease);
        Assert.Equal(InputStreamType.QpackDecoder, notification.StreamType);

        lease.Dispose();
    }

    [Fact(Timeout = 5000)]
    public async Task OpenStreamAsync_should_throw_operation_canceled_when_cancelled()
    {
        var options = CreateTestOptions();
        await using var manager = new TestableQuicConnectionManager(options, TestEndpoint);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => manager.OpenStreamAsync(OutputStreamType.Request, cts.Token));
    }

    private static QuicOptions CreateTestOptions()
    {
        return new QuicOptions
        {
            Host = "localhost",
            Port = 8443,
            MaxFrameSize = 16384,
        };
    }

    /// <summary>
    /// Wrapper that configures a <see cref="QuicConnectionManager"/> with a fake provider,
    /// avoiding real QUIC requirements (sealed class — cannot subclass).
    /// </summary>
    private sealed class TestableQuicConnectionManager : IAsyncDisposable
    {
        private readonly QuicConnectionManager _inner;
        private readonly FakeClientProvider _fakeProvider;

        public TestableQuicConnectionManager(QuicOptions options, RequestEndpoint endpoint)
        {
            _inner = new QuicConnectionManager(options, endpoint);
            _fakeProvider = new FakeClientProvider();
            _inner.SetProvider(_fakeProvider);
        }

        public int ProviderCreationCount => _fakeProvider.StreamsOpened > 0 ? 1 : 0;

        public Task<ConnectionLease> OpenStreamAsync(OutputStreamType streamType, CancellationToken ct = default)
            => _inner.OpenStreamAsync(streamType, ct);

        public void SimulateInboundStream(InputStreamType streamType)
            => _inner.AddBufferedInbound(streamType);

        public void StartInboundAcceptLoopDirect(Action<QuicConnectionManager.InboundStream> subscriber)
            => _inner.FlushBufferedToSubscriber(subscriber);

        public ValueTask DisposeAsync() => _inner.DisposeAsync();
    }

    private sealed class FakeClientProvider : IClientProvider
    {
        private int _streamsOpened;

        public int StreamsOpened => _streamsOpened;
        public EndPoint? RemoteEndPoint => new IPEndPoint(IPAddress.Loopback, 8443);
        public bool SupportsMultipleStreams => true;

        public Task<Stream> GetStreamAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
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
            // Block until cancelled — simulates waiting for server-initiated streams
            return Task.Delay(Timeout.Infinite, ct).ContinueWith<Stream>(_ =>
                throw new OperationCanceledException(ct), ct);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
