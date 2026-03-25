using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using Akka.Actor;
using TurboHttp.Internal;
using TurboHttp.Pooling;
using TurboHttp.Transport;

namespace TurboHttp.Tests.Transport;

/// <summary>
/// Tests <see cref="ConnectionPool"/> and its nested <see cref="ConnectionPool.HostConnections"/>:
/// version-aware acquire/release, idle reuse, MRU selection, per-host limits, idle eviction.
/// </summary>
public sealed class ConnectionPoolTests : IAsyncLifetime
{
    private TcpListener? _listener;
    private int _port;

    public ValueTask InitializeAsync()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        _port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _listener?.Stop();
        return ValueTask.CompletedTask;
    }

    private TcpOptions CreateOptions() => new()
    {
        Host = "127.0.0.1",
        Port = _port,
        MaxFrameSize = 16384
    };

    private RequestEndpoint CreateEndpoint(Version? version = null) => new()
    {
        Host = "127.0.0.1",
        Port = (ushort)_port,
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

    #region AcquireAsync — HTTP/1.0

    [Fact(DisplayName = "TASK-026-004-001: AcquireAsync HTTP/1.0 always creates new connection", Timeout = 5000)]
    public async Task AcquireAsync_Http10_AlwaysCreatesNew()
    {
        using var pool = new ConnectionPool(TimeSpan.FromSeconds(30));
        var options = CreateOptions();
        var endpoint = CreateEndpoint(HttpVersion.Version10);

        var lease1 = await pool.AcquireAsync(options, endpoint);
        pool.Release(lease1, canReuse: true);

        var lease2 = await pool.AcquireAsync(options, endpoint);

        // HTTP/1.0 never reuses — must be different leases
        Assert.NotSame(lease1, lease2);

        lease2.Dispose();
    }

    #endregion

    #region AcquireAsync — HTTP/1.1

    [Fact(DisplayName = "TASK-026-004-002: AcquireAsync HTTP/1.1 reuses idle connection", Timeout = 5000)]
    public async Task AcquireAsync_Http11_ReusesIdle()
    {
        using var pool = new ConnectionPool(TimeSpan.FromSeconds(30));
        var options = CreateOptions();
        var endpoint = CreateEndpoint(HttpVersion.Version11);

        var lease1 = await pool.AcquireAsync(options, endpoint);
        pool.Release(lease1, canReuse: true);

        var lease2 = await pool.AcquireAsync(options, endpoint);

        // HTTP/1.1 should reuse the idle connection
        Assert.Same(lease1, lease2);

        lease2.Dispose();
    }

    #endregion

    #region AcquireAsync — HTTP/2

    [Fact(DisplayName = "TASK-026-004-003: AcquireAsync HTTP/2 multiplexes on same connection", Timeout = 5000)]
    public async Task AcquireAsync_Http2_MultiplexesOnSame()
    {
        using var pool = new ConnectionPool(TimeSpan.FromSeconds(30));
        var options = CreateOptions();
        var endpoint = CreateEndpoint(HttpVersion.Version20);

        var lease1 = await pool.AcquireAsync(options, endpoint);
        // Don't release — acquire second on same multiplexed connection
        var lease2 = await pool.AcquireAsync(options, endpoint);

        // HTTP/2 should multiplex on the same lease
        Assert.Same(lease1, lease2);
        Assert.Equal(2, lease2.ActiveStreams);

        pool.Release(lease1, canReuse: true);
        pool.Release(lease2, canReuse: true);
    }

    #endregion

    #region Release

    [Fact(DisplayName = "TASK-026-004-004: Release canReuse returns to idle", Timeout = 5000)]
    public async Task Release_CanReuse_ReturnsToIdle()
    {
        using var pool = new ConnectionPool(TimeSpan.FromSeconds(30));
        var options = CreateOptions();
        var endpoint = CreateEndpoint(HttpVersion.Version11);

        var lease = await pool.AcquireAsync(options, endpoint);
        pool.Release(lease, canReuse: true);

        // Connection is still alive (returned to idle, not disposed)
        Assert.True(lease.IsAlive);
    }

    [Fact(DisplayName = "TASK-026-004-005: Release cannotReuse disposes connection", Timeout = 5000)]
    public async Task Release_CannotReuse_Disposes()
    {
        using var pool = new ConnectionPool(TimeSpan.FromSeconds(30));
        var options = CreateOptions();
        var endpoint = CreateEndpoint(HttpVersion.Version11);

        var lease = await pool.AcquireAsync(options, endpoint);
        pool.Release(lease, canReuse: false);

        // Give async dispose a moment
        await Task.Delay(50);

        Assert.False(lease.IsAlive);
    }

    #endregion

    #region Idle eviction

    [Fact(DisplayName = "TASK-026-004-006: EvictIdle removes expired connections", Timeout = 5000)]
    public async Task EvictIdle_RemovesExpired()
    {
        // Use a very short idle timeout so connections expire quickly
        using var pool = new ConnectionPool(TimeSpan.FromMilliseconds(50));
        var options = CreateOptions();
        var endpoint = CreateEndpoint(HttpVersion.Version11);

        // Acquire two connections simultaneously (both active, so no reuse)
        var lease1 = await pool.AcquireAsync(options, endpoint);
        var lease2 = await pool.AcquireAsync(options, endpoint);
        Assert.NotSame(lease1, lease2);

        // Release both to idle
        pool.Release(lease1, canReuse: true);
        pool.Release(lease2, canReuse: true);

        // Wait for idle timeout + eviction timer to fire
        await Task.Delay(300);

        // At least one expired connection should have been evicted
        // (keep-at-least-one preserves one, but the other should be disposed)
        var evictedCount = (!lease1.IsAlive ? 1 : 0) + (!lease2.IsAlive ? 1 : 0);
        Assert.True(evictedCount >= 1, "At least one idle connection should have been evicted");
    }

    [Fact(DisplayName = "TASK-026-004-007: EvictIdle keeps at least one per host", Timeout = 5000)]
    public async Task EvictIdle_KeepsAtLeastOne()
    {
        // Very short idle timeout
        using var pool = new ConnectionPool(TimeSpan.FromMilliseconds(50));
        var options = CreateOptions();
        var endpoint = CreateEndpoint(HttpVersion.Version11);

        // Acquire two connections simultaneously to get distinct leases
        var lease1 = await pool.AcquireAsync(options, endpoint);
        var lease2 = await pool.AcquireAsync(options, endpoint);

        // Release both to idle
        pool.Release(lease1, canReuse: true);
        pool.Release(lease2, canReuse: true);

        // Wait for idle timeout + eviction
        await Task.Delay(300);

        // At least one should still be alive (keep-at-least-one invariant)
        var anyAlive = lease1.IsAlive || lease2.IsAlive;
        Assert.True(anyAlive);

        // But not both — at least one should have been evicted
        var bothAlive = lease1.IsAlive && lease2.IsAlive;
        Assert.False(bothAlive, "Eviction should have removed at least one expired idle connection");
    }

    #endregion

    #region Per-host limit

    [Fact(DisplayName = "TASK-026-004-008: AcquireAsync per-host limit blocks when full", Timeout = 5000)]
    public async Task AcquireAsync_PerHostLimit_Blocks()
    {
        using var pool = new ConnectionPool(TimeSpan.FromSeconds(30));
        var options = CreateOptions();
        var endpoint = CreateEndpoint(HttpVersion.Version11);

        // Acquire 6 connections (the HTTP/1.x per-host limit)
        var leases = new List<ConnectionLease>();
        for (var i = 0; i < 6; i++)
        {
            leases.Add(await pool.AcquireAsync(options, endpoint));
        }

        // 7th acquire should block (we use a short timeout to detect blocking)
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await pool.AcquireAsync(options, endpoint, cts.Token);
        });

        // Cleanup
        foreach (var lease in leases)
        {
            lease.Dispose();
        }
    }

    #endregion

    #region MRU selection

    [Fact(DisplayName = "TASK-026-004-009: SelectMru returns latest active lease", Timeout = 5000)]
    public async Task SelectMru_ReturnsLatestActive()
    {
        using var pool = new ConnectionPool(TimeSpan.FromSeconds(30));
        var options = CreateOptions();
        var endpoint = CreateEndpoint(HttpVersion.Version20);

        // Create first connection
        var lease1 = await pool.AcquireAsync(options, endpoint);
        pool.Release(lease1, canReuse: true);

        // Small delay to ensure different LastActivity timestamps
        await Task.Delay(50);

        // Mark lease1 as non-reusable to force a second connection
        lease1.MarkNoReuse();

        // Create second connection (lease1 has no available slot since non-reusable)
        var lease2 = await pool.AcquireAsync(options, endpoint);

        // lease2 is the most recently active and should be selected
        // (It was just established and marked busy)
        Assert.True(lease2.LastActivity >= lease1.LastActivity);

        pool.Release(lease2, canReuse: true);
        lease1.Dispose();
    }

    #endregion

    #region IDisposable

    [Fact(DisplayName = "TASK-026-004-010: DisposeAsync disposes all hosts", Timeout = 5000)]
    public async Task DisposeAsync_DisposesAllHosts()
    {
        var pool = new ConnectionPool(TimeSpan.FromSeconds(30));
        var options = CreateOptions();
        var endpoint = CreateEndpoint(HttpVersion.Version11);

        var lease = await pool.AcquireAsync(options, endpoint);

        pool.Dispose();

        Assert.False(lease.IsAlive);
    }

    [Fact(DisplayName = "TASK-026-004-011: AcquireAsync throws after disposal", Timeout = 5000)]
    public async Task AcquireAsync_ThrowsAfterDisposal()
    {
        var pool = new ConnectionPool(TimeSpan.FromSeconds(30));
        pool.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
        {
            await pool.AcquireAsync(CreateOptions(), CreateEndpoint());
        });
    }

    #endregion

    #region HTTP/2 release behavior

    [Fact(DisplayName = "TASK-026-004-012: HTTP/2 Release disposes only when last stream and non-reusable", Timeout = 5000)]
    public async Task Http2_Release_DisposesOnLastStreamAndNonReusable()
    {
        using var pool = new ConnectionPool(TimeSpan.FromSeconds(30));
        var options = CreateOptions();
        var endpoint = CreateEndpoint(HttpVersion.Version20);

        var lease1 = await pool.AcquireAsync(options, endpoint);
        var lease2 = await pool.AcquireAsync(options, endpoint);

        // Same multiplexed connection
        Assert.Same(lease1, lease2);
        Assert.Equal(2, lease2.ActiveStreams);

        // Release first stream as non-reusable — should NOT dispose (still has active streams)
        pool.Release(lease1, canReuse: false);
        Assert.True(lease1.IsAlive); // Still alive because 1 active stream remains

        // Release last stream — should dispose (non-reusable + 0 active)
        pool.Release(lease2, canReuse: true);

        await Task.Delay(50);
        Assert.False(lease2.IsAlive);
    }

    #endregion

    #region HTTP/1.0 release behavior

    [Fact(DisplayName = "TASK-026-004-013: HTTP/1.0 Release always disposes", Timeout = 5000)]
    public async Task Http10_Release_AlwaysDisposes()
    {
         using var pool = new ConnectionPool(TimeSpan.FromSeconds(30));
        var options = CreateOptions();
        var endpoint = CreateEndpoint(HttpVersion.Version10);

        var lease = await pool.AcquireAsync(options, endpoint);
        pool.Release(lease, canReuse: true); // canReuse is ignored for HTTP/1.0

        await Task.Delay(50);
        Assert.False(lease.IsAlive);
    }

    #endregion
}
