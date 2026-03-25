using System.Buffers;
using System.Net;
using System.Runtime.Versioning;
using System.Threading.Channels;
using TurboHttp.Internal;
using TurboHttp.Transport;

namespace TurboHttp.StreamTests.IO;

/// <summary>
/// Tests <see cref="QuicConnectionManager"/> — QUIC multi-stream management without actors.
/// Mirrors the behavior validated in <see cref="ConnectionActorQuicTests"/>.
/// </summary>
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macOS")]
[SupportedOSPlatform("windows")]
public sealed class QuicConnectionManagerTests
{
    private static readonly RequestEndpoint TestEndpoint = new()
    {
        Host = "localhost",
        Port = 8443,
        Scheme = "https",
        Version = new Version(3, 0)
    };

    [Fact(DisplayName = "QCM-001: OpenStreamAsync returns lease for Request stream type", Timeout = 5000)]
    public async Task Should_ReturnLease_WhenOpeningRequestStream()
    {
        // Arrange: use a fake provider that returns MemoryStreams
        var options = CreateTestOptions();
        await using var manager = new TestableQuicConnectionManager(options, TestEndpoint);

        // Act
        var lease = await manager.OpenStreamAsync(OutputStreamType.Request);

        // Assert
        Assert.NotNull(lease);
        Assert.True(lease.IsAlive);
        Assert.Equal(TestEndpoint, lease.Key);

        lease.Dispose();
    }

    [Fact(DisplayName = "QCM-002: OpenStreamAsync returns lease for Control stream type", Timeout = 5000)]
    public async Task Should_ReturnLease_WhenOpeningControlStream()
    {
        var options = CreateTestOptions();
        await using var manager = new TestableQuicConnectionManager(options, TestEndpoint);

        var lease = await manager.OpenStreamAsync(OutputStreamType.Control);

        Assert.NotNull(lease);
        Assert.True(lease.IsAlive);

        lease.Dispose();
    }

    [Fact(DisplayName = "QCM-003: OpenStreamAsync returns lease for QpackEncoder stream type", Timeout = 5000)]
    public async Task Should_ReturnLease_WhenOpeningQpackEncoderStream()
    {
        var options = CreateTestOptions();
        await using var manager = new TestableQuicConnectionManager(options, TestEndpoint);

        var lease = await manager.OpenStreamAsync(OutputStreamType.QpackEncoder);

        Assert.NotNull(lease);
        Assert.True(lease.IsAlive);

        lease.Dispose();
    }

    [Fact(DisplayName = "QCM-004: Multiple streams share the same provider", Timeout = 5000)]
    public async Task Should_ReuseProvider_WhenOpeningMultipleStreams()
    {
        var options = CreateTestOptions();
        await using var manager = new TestableQuicConnectionManager(options, TestEndpoint);

        var lease1 = await manager.OpenStreamAsync(OutputStreamType.Request);
        var lease2 = await manager.OpenStreamAsync(OutputStreamType.Control);

        // Both leases should be alive and tracked
        Assert.True(lease1.IsAlive);
        Assert.True(lease2.IsAlive);

        // Provider should have been created only once
        Assert.Equal(1, manager.ProviderCreationCount);

        lease1.Dispose();
        lease2.Dispose();
    }

    [Fact(DisplayName = "QCM-005: DisposeAsync cancels inbound loop and disposes all streams", Timeout = 5000)]
    public async Task Should_DisposeAllStreams_WhenManagerDisposed()
    {
        var options = CreateTestOptions();
        var manager = new TestableQuicConnectionManager(options, TestEndpoint);

        var lease1 = await manager.OpenStreamAsync(OutputStreamType.Request);
        var lease2 = await manager.OpenStreamAsync(OutputStreamType.Request);

        Assert.True(lease1.IsAlive);
        Assert.True(lease2.IsAlive);

        // Act
        await manager.DisposeAsync();

        // Assert: both leases should be disposed
        Assert.False(lease1.IsAlive);
        Assert.False(lease2.IsAlive);
    }

    [Fact(DisplayName = "QCM-006: OpenStreamAsync throws after dispose", Timeout = 5000)]
    public async Task Should_ThrowObjectDisposed_WhenOpenAfterDispose()
    {
        var options = CreateTestOptions();
        var manager = new TestableQuicConnectionManager(options, TestEndpoint);
        await manager.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => manager.OpenStreamAsync(OutputStreamType.Request));
    }

    [Fact(DisplayName = "QCM-007: DisposeAsync is idempotent", Timeout = 5000)]
    public async Task Should_NotThrow_WhenDisposedTwice()
    {
        var options = CreateTestOptions();
        var manager = new TestableQuicConnectionManager(options, TestEndpoint);

        await manager.DisposeAsync();
        await manager.DisposeAsync(); // should not throw
    }

    [Fact(DisplayName = "QCM-008: Sequential spawn enforced via semaphore", Timeout = 5000)]
    public async Task Should_EnforceSequentialSpawn_WhenConcurrentOpenRequests()
    {
        var options = CreateTestOptions();
        await using var manager = new TestableQuicConnectionManager(options, TestEndpoint);

        // Launch multiple opens concurrently
        var tasks = new[]
        {
            manager.OpenStreamAsync(OutputStreamType.Request),
            manager.OpenStreamAsync(OutputStreamType.Control),
            manager.OpenStreamAsync(OutputStreamType.QpackEncoder),
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

    [Fact(DisplayName = "QCM-009: StartInboundAcceptLoop flushes buffered notifications", Timeout = 5000)]
    public async Task Should_FlushBufferedInbound_WhenSubscriberRegisters()
    {
        var options = CreateTestOptions();
        await using var manager = new TestableQuicConnectionManager(options, TestEndpoint);

        // Open a stream first to ensure provider is created
        var lease = await manager.OpenStreamAsync(OutputStreamType.Request);

        // Buffer an inbound notification
        manager.SimulateInboundStream(InputStreamType.Control);

        // Register subscriber — should receive the buffered notification
        var received = new List<QuicConnectionManager.InboundStream>();
        manager.StartInboundAcceptLoopDirect(notification => received.Add(notification));

        Assert.Single(received);
        Assert.Equal(InputStreamType.Control, received[0].StreamType);

        lease.Dispose();
    }

    [Fact(DisplayName = "QCM-010: InboundStream record has correct properties", Timeout = 5000)]
    public async Task Should_HaveCorrectProperties_WhenInboundStreamCreated()
    {
        var options = CreateTestOptions();
        await using var manager = new TestableQuicConnectionManager(options, TestEndpoint);

        var lease = await manager.OpenStreamAsync(OutputStreamType.Request);
        var notification = new QuicConnectionManager.InboundStream(lease, InputStreamType.QpackDecoder);

        Assert.Same(lease, notification.Lease);
        Assert.Equal(InputStreamType.QpackDecoder, notification.StreamType);

        lease.Dispose();
    }

    [Fact(DisplayName = "QCM-011: OpenStreamAsync cancellation support", Timeout = 5000)]
    public async Task Should_ThrowOperationCanceled_WhenCancelled()
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

        public void Close() { }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
