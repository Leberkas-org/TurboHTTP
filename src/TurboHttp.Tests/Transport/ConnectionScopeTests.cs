using System.Buffers;
using System.Net;
using System.Threading.Channels;
using Akka.Actor;
using TurboHttp.Internal;
using TurboHttp.Transport;

namespace TurboHttp.Tests.Transport;

/// <summary>
/// Tests for <see cref="IConnectionScope"/> implementations:
/// <see cref="SingleRequestConnectionScope"/> (HTTP/1.0) and
/// <see cref="PersistentConnectionScope"/> (HTTP/1.1+), plus
/// <see cref="ConnectionScopeFactory"/>.
/// </summary>
public sealed class ConnectionScopeTests
{
    private static TcpOptions CreateOptions() => new()
    {
        Host = "127.0.0.1",
        Port = 8080,
        MaxFrameSize = 16384
    };

    private static RequestEndpoint CreateEndpoint(Version? version = null) => new()
    {
        Host = "127.0.0.1",
        Port = 8080,
        Scheme = "http",
        Version = version ?? HttpVersion.Version11
    };

    private static ConnectionHandle CreateHandle(RequestEndpoint endpoint)
    {
        var outbound = Channel.CreateUnbounded<(IMemoryOwner<byte> Buffer, int ReadableBytes)>();
        var inbound = Channel.CreateUnbounded<(IMemoryOwner<byte> Buffer, int ReadableBytes)>();
        return new ConnectionHandle(outbound.Writer, inbound.Reader, endpoint, ActorRefs.Nobody);
    }

    private static ClientState CreateState()
    {
        return new ClientState(
            maxFrameSize: 16384,
            stream: new MemoryStream(),
            inboundChannel: null,
            outboundChannel: null);
    }

    private static ConnectionLease CreateFakeLease(RequestEndpoint endpoint)
    {
        var handle = CreateHandle(endpoint);
        var state = CreateState();
        return new ConnectionLease(handle, state);
    }

    /// <summary>
    /// A testable <see cref="ConnectionPool"/> that returns pre-built leases
    /// and tracks acquire/release calls without actual TCP connections.
    /// </summary>
    private sealed class FakeConnectionPool : ConnectionPool
    {
        private readonly Queue<ConnectionLease> _leasesToReturn = new();
        private readonly List<(ConnectionLease Lease, bool CanReuse)> _released = [];
        private int _acquireCount;

        public FakeConnectionPool() : base(TimeSpan.FromSeconds(30))
        {
        }

        public int AcquireCount => _acquireCount;
        public IReadOnlyList<(ConnectionLease Lease, bool CanReuse)> Released => _released;

        public void EnqueueLease(ConnectionLease lease)
        {
            _leasesToReturn.Enqueue(lease);
        }

        public override Task<ConnectionLease> AcquireAsync(
            TcpOptions options,
            RequestEndpoint endpoint,
            CancellationToken ct = default)
        {
            _acquireCount++;
            var lease = _leasesToReturn.Dequeue();
            return Task.FromResult(lease);
        }

        public override void Release(ConnectionLease lease, bool canReuse)
        {
            _released.Add((lease, canReuse));
        }
    }

    #region SingleRequestConnectionScope

    [Fact(DisplayName = "TASK-030-005-001: SingleRequest.AcquireAsync returns lease from pool", Timeout = 5000)]
    public async Task SingleRequest_AcquireAsync_ReturnsLease()
    {
        var endpoint = CreateEndpoint(HttpVersion.Version10);
        using var pool = new FakeConnectionPool();
        var expectedLease = CreateFakeLease(endpoint);
        pool.EnqueueLease(expectedLease);

        await using var scope = new SingleRequestConnectionScope(pool, CreateOptions(), endpoint);
        var lease = await scope.AcquireAsync();

        Assert.Same(expectedLease, lease);
        Assert.Equal(1, pool.AcquireCount);
    }

    [Fact(DisplayName = "TASK-030-005-002: SingleRequest.AcquireAsync throws on double acquire", Timeout = 5000)]
    public async Task SingleRequest_AcquireAsync_ThrowsOnDoubleAcquire()
    {
        var endpoint = CreateEndpoint(HttpVersion.Version10);
        using var pool = new FakeConnectionPool();
        pool.EnqueueLease(CreateFakeLease(endpoint));

        await using var scope = new SingleRequestConnectionScope(pool, CreateOptions(), endpoint);
        await scope.AcquireAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.AcquireAsync());
    }

    [Fact(DisplayName = "TASK-030-005-003: SingleRequest.ReturnAsync always releases with canReuse=false", Timeout = 5000)]
    public async Task SingleRequest_ReturnAsync_AlwaysReleasesNonReusable()
    {
        var endpoint = CreateEndpoint(HttpVersion.Version10);
        using var pool = new FakeConnectionPool();
        var lease = CreateFakeLease(endpoint);
        pool.EnqueueLease(lease);

        await using var scope = new SingleRequestConnectionScope(pool, CreateOptions(), endpoint);
        await scope.AcquireAsync();

        // Even when canReuse=true, HTTP/1.0 should always release with canReuse=false
        await scope.ReturnAsync(canReuse: true);

        Assert.Single(pool.Released);
        Assert.Same(lease, pool.Released[0].Lease);
        Assert.False(pool.Released[0].CanReuse);
    }

    [Fact(DisplayName = "TASK-030-005-004: SingleRequest.CanReuse always returns false", Timeout = 5000)]
    public async Task SingleRequest_CanReuse_AlwaysFalse()
    {
        var endpoint = CreateEndpoint(HttpVersion.Version10);
        using var pool = new FakeConnectionPool();

        await using var scope = new SingleRequestConnectionScope(pool, CreateOptions(), endpoint);

        Assert.False(scope.CanReuse());
    }

    [Fact(DisplayName = "TASK-030-005-005: SingleRequest.CleanupAsync releases active lease", Timeout = 5000)]
    public async Task SingleRequest_CleanupAsync_ReleasesActiveLease()
    {
        var endpoint = CreateEndpoint(HttpVersion.Version10);
        using var pool = new FakeConnectionPool();
        var lease = CreateFakeLease(endpoint);
        pool.EnqueueLease(lease);

        var scope = new SingleRequestConnectionScope(pool, CreateOptions(), endpoint);
        await scope.AcquireAsync();
        await scope.CleanupAsync();

        Assert.Single(pool.Released);
        Assert.Same(lease, pool.Released[0].Lease);
        Assert.False(pool.Released[0].CanReuse);
    }

    [Fact(DisplayName = "TASK-030-005-006: SingleRequest.CleanupAsync is safe when no lease", Timeout = 5000)]
    public async Task SingleRequest_CleanupAsync_SafeWhenNoLease()
    {
        var endpoint = CreateEndpoint(HttpVersion.Version10);
        using var pool = new FakeConnectionPool();

        var scope = new SingleRequestConnectionScope(pool, CreateOptions(), endpoint);
        await scope.CleanupAsync();

        Assert.Empty(pool.Released);
    }

    [Fact(DisplayName = "TASK-030-005-007: SingleRequest double-dispose is safe", Timeout = 5000)]
    public async Task SingleRequest_DoubleDispose_IsSafe()
    {
        var endpoint = CreateEndpoint(HttpVersion.Version10);
        using var pool = new FakeConnectionPool();
        var lease = CreateFakeLease(endpoint);
        pool.EnqueueLease(lease);

        var scope = new SingleRequestConnectionScope(pool, CreateOptions(), endpoint);
        await scope.AcquireAsync();

        await scope.DisposeAsync();
        await scope.DisposeAsync(); // second dispose should not throw or double-release

        Assert.Single(pool.Released);
    }

    [Fact(DisplayName = "TASK-030-005-008: SingleRequest.ReturnAsync is no-op when no lease", Timeout = 5000)]
    public async Task SingleRequest_ReturnAsync_NoOpWhenNoLease()
    {
        var endpoint = CreateEndpoint(HttpVersion.Version10);
        using var pool = new FakeConnectionPool();

        await using var scope = new SingleRequestConnectionScope(pool, CreateOptions(), endpoint);
        await scope.ReturnAsync(canReuse: false);

        Assert.Empty(pool.Released);
    }

    [Fact(DisplayName = "TASK-030-005-009: SingleRequest acquire-return-acquire cycle works", Timeout = 5000)]
    public async Task SingleRequest_AcquireReturnAcquire_Works()
    {
        var endpoint = CreateEndpoint(HttpVersion.Version10);
        using var pool = new FakeConnectionPool();
        var lease1 = CreateFakeLease(endpoint);
        var lease2 = CreateFakeLease(endpoint);
        pool.EnqueueLease(lease1);
        pool.EnqueueLease(lease2);

        await using var scope = new SingleRequestConnectionScope(pool, CreateOptions(), endpoint);

        var result1 = await scope.AcquireAsync();
        Assert.Same(lease1, result1);

        await scope.ReturnAsync(canReuse: false);

        var result2 = await scope.AcquireAsync();
        Assert.Same(lease2, result2);
        Assert.Equal(2, pool.AcquireCount);
    }

    [Fact(DisplayName = "TASK-030-005-010: SingleRequest.AcquireAsync throws after dispose", Timeout = 5000)]
    public async Task SingleRequest_AcquireAsync_ThrowsAfterDispose()
    {
        var endpoint = CreateEndpoint(HttpVersion.Version10);
        using var pool = new FakeConnectionPool();

        var scope = new SingleRequestConnectionScope(pool, CreateOptions(), endpoint);
        await scope.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => scope.AcquireAsync());
    }

    #endregion

    #region PersistentConnectionScope

    [Fact(DisplayName = "TASK-030-005-011: Persistent.AcquireAsync returns lease from pool", Timeout = 5000)]
    public async Task Persistent_AcquireAsync_ReturnsLease()
    {
        var endpoint = CreateEndpoint(HttpVersion.Version11);
        using var pool = new FakeConnectionPool();
        var expectedLease = CreateFakeLease(endpoint);
        pool.EnqueueLease(expectedLease);

        await using var scope = new PersistentConnectionScope(pool, CreateOptions(), endpoint);
        var lease = await scope.AcquireAsync();

        Assert.Same(expectedLease, lease);
        Assert.Equal(1, pool.AcquireCount);
    }

    [Fact(DisplayName = "TASK-030-005-012: Persistent.ReturnAsync with canReuse=true releases reusable", Timeout = 5000)]
    public async Task Persistent_ReturnAsync_CanReuse_ReleasesReusable()
    {
        var endpoint = CreateEndpoint(HttpVersion.Version11);
        using var pool = new FakeConnectionPool();
        var lease = CreateFakeLease(endpoint);
        pool.EnqueueLease(lease);

        await using var scope = new PersistentConnectionScope(pool, CreateOptions(), endpoint);
        await scope.AcquireAsync();
        await scope.ReturnAsync(canReuse: true);

        Assert.Single(pool.Released);
        Assert.True(pool.Released[0].CanReuse);
    }

    [Fact(DisplayName = "TASK-030-005-013: Persistent.ReturnAsync with canReuse=false releases non-reusable", Timeout = 5000)]
    public async Task Persistent_ReturnAsync_CannotReuse_ReleasesNonReusable()
    {
        var endpoint = CreateEndpoint(HttpVersion.Version11);
        using var pool = new FakeConnectionPool();
        var lease = CreateFakeLease(endpoint);
        pool.EnqueueLease(lease);

        await using var scope = new PersistentConnectionScope(pool, CreateOptions(), endpoint);
        await scope.AcquireAsync();
        await scope.ReturnAsync(canReuse: false);

        Assert.Single(pool.Released);
        Assert.False(pool.Released[0].CanReuse);
    }

    [Fact(DisplayName = "TASK-030-005-014: Persistent.CanReuse returns false after close", Timeout = 5000)]
    public async Task Persistent_CanReuse_FalseAfterClose()
    {
        var endpoint = CreateEndpoint(HttpVersion.Version11);
        using var pool = new FakeConnectionPool();
        var lease = CreateFakeLease(endpoint);
        pool.EnqueueLease(lease);

        await using var scope = new PersistentConnectionScope(pool, CreateOptions(), endpoint);
        await scope.AcquireAsync();
        await scope.ReturnAsync(canReuse: false);

        Assert.False(scope.CanReuse());
    }

    [Fact(DisplayName = "TASK-030-005-015: Persistent.CleanupAsync releases active lease", Timeout = 5000)]
    public async Task Persistent_CleanupAsync_ReleasesActiveLease()
    {
        var endpoint = CreateEndpoint(HttpVersion.Version11);
        using var pool = new FakeConnectionPool();
        var lease = CreateFakeLease(endpoint);
        pool.EnqueueLease(lease);

        var scope = new PersistentConnectionScope(pool, CreateOptions(), endpoint);
        await scope.AcquireAsync();
        await scope.CleanupAsync();

        Assert.Single(pool.Released);
    }

    [Fact(DisplayName = "TASK-030-005-016: Persistent double-dispose is safe", Timeout = 5000)]
    public async Task Persistent_DoubleDispose_IsSafe()
    {
        var endpoint = CreateEndpoint(HttpVersion.Version11);
        using var pool = new FakeConnectionPool();
        var lease = CreateFakeLease(endpoint);
        pool.EnqueueLease(lease);

        var scope = new PersistentConnectionScope(pool, CreateOptions(), endpoint);
        await scope.AcquireAsync();

        await scope.DisposeAsync();
        await scope.DisposeAsync(); // second dispose should not throw or double-release

        Assert.Single(pool.Released);
    }

    [Fact(DisplayName = "TASK-030-005-017: Persistent.CleanupAsync is safe when no lease", Timeout = 5000)]
    public async Task Persistent_CleanupAsync_SafeWhenNoLease()
    {
        var endpoint = CreateEndpoint(HttpVersion.Version11);
        using var pool = new FakeConnectionPool();

        var scope = new PersistentConnectionScope(pool, CreateOptions(), endpoint);
        await scope.CleanupAsync();

        Assert.Empty(pool.Released);
    }

    [Fact(DisplayName = "TASK-030-005-018: Persistent.AcquireAsync throws after dispose", Timeout = 5000)]
    public async Task Persistent_AcquireAsync_ThrowsAfterDispose()
    {
        var endpoint = CreateEndpoint(HttpVersion.Version11);
        using var pool = new FakeConnectionPool();

        var scope = new PersistentConnectionScope(pool, CreateOptions(), endpoint);
        await scope.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => scope.AcquireAsync());
    }

    [Fact(DisplayName = "TASK-030-005-019: Persistent.ReturnAsync is no-op when no lease", Timeout = 5000)]
    public async Task Persistent_ReturnAsync_NoOpWhenNoLease()
    {
        var endpoint = CreateEndpoint(HttpVersion.Version11);
        using var pool = new FakeConnectionPool();

        await using var scope = new PersistentConnectionScope(pool, CreateOptions(), endpoint);
        await scope.ReturnAsync(canReuse: true);

        Assert.Empty(pool.Released);
    }

    [Fact(DisplayName = "TASK-030-005-020: Persistent.CleanupAsync with reusable lease returns reusable", Timeout = 5000)]
    public async Task Persistent_CleanupAsync_ReusableLease_ReturnsReusable()
    {
        var endpoint = CreateEndpoint(HttpVersion.Version11);
        using var pool = new FakeConnectionPool();
        var lease = CreateFakeLease(endpoint);
        pool.EnqueueLease(lease);

        var scope = new PersistentConnectionScope(pool, CreateOptions(), endpoint);
        await scope.AcquireAsync();

        // Lease is alive and reusable by default, lastCanReuse starts as true
        await scope.CleanupAsync();

        Assert.Single(pool.Released);
        Assert.True(pool.Released[0].CanReuse);
    }

    #endregion

    #region ConnectionScopeFactory

    [Fact(DisplayName = "TASK-030-005-021: Factory creates SingleRequestConnectionScope for HTTP/1.0", Timeout = 5000)]
    public void Factory_CreatesCorrectType_Http10()
    {
        using var pool = new FakeConnectionPool();
        var endpoint = CreateEndpoint(HttpVersion.Version10);

        var scope = ConnectionScopeFactory.Create(HttpVersion.Version10, pool, CreateOptions(), endpoint);

        Assert.IsType<SingleRequestConnectionScope>(scope);
    }

    [Fact(DisplayName = "TASK-030-005-022: Factory creates PersistentConnectionScope for HTTP/1.1", Timeout = 5000)]
    public void Factory_CreatesCorrectType_Http11()
    {
        using var pool = new FakeConnectionPool();
        var endpoint = CreateEndpoint(HttpVersion.Version11);

        var scope = ConnectionScopeFactory.Create(HttpVersion.Version11, pool, CreateOptions(), endpoint);

        Assert.IsType<PersistentConnectionScope>(scope);
    }

    [Fact(DisplayName = "TASK-030-005-023: Factory creates PersistentConnectionScope for HTTP/2.0", Timeout = 5000)]
    public void Factory_CreatesCorrectType_Http20()
    {
        using var pool = new FakeConnectionPool();
        var endpoint = CreateEndpoint(HttpVersion.Version20);

        var scope = ConnectionScopeFactory.Create(HttpVersion.Version20, pool, CreateOptions(), endpoint);

        Assert.IsType<PersistentConnectionScope>(scope);
    }

    [Fact(DisplayName = "TASK-030-005-024: Factory throws on null version", Timeout = 5000)]
    public void Factory_ThrowsOnNullVersion()
    {
        using var pool = new FakeConnectionPool();
        var endpoint = CreateEndpoint();

        Assert.Throws<ArgumentNullException>(() =>
            ConnectionScopeFactory.Create(null!, pool, CreateOptions(), endpoint));
    }

    [Fact(DisplayName = "TASK-030-005-025: Factory throws on null pool", Timeout = 5000)]
    public void Factory_ThrowsOnNullPool()
    {
        var endpoint = CreateEndpoint();

        Assert.Throws<ArgumentNullException>(() =>
            ConnectionScopeFactory.Create(HttpVersion.Version11, null!, CreateOptions(), endpoint));
    }

    #endregion
}
