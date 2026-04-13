using Akka.Actor;
using TurboHTTP.Diagnostics;
using TurboHTTP.Internal;
using TurboHTTP.Transport.Tcp;

namespace TurboHTTP.Transport.Connection;

/// <summary>
/// Single actor that manages ALL per-host TCP/TLS connection state: acquire, release, idle reuse,
/// eviction, and HTTP version-specific slot limits. Every <see cref="TcpConnectionStage"/> talks
/// to this same actor directly via <see cref="Acquire"/> / <see cref="Release"/>.
/// <para>
/// Per-host state (leases, idle queue, pending queue, establishing count) is kept in a
/// <see cref="Dictionary{TKey,TValue}"/> of <see cref="HostState"/> instances — all accessed
/// on the actor's single-threaded mailbox, so no locks needed.
/// </para>
/// Mirrors <see cref="QuicConnectionManagerActor"/> structurally — a senior dev who knows
/// one immediately understands the other.
/// </summary>
internal sealed class TcpConnectionManagerActor : ReceiveActor, IWithTimers
{
    internal sealed record Acquire(
        TcpOptions Options,
        RequestEndpoint Endpoint,
        TaskCompletionSource<ConnectionLease> Tcs,
        CancellationToken Token);

    internal sealed record Release(ConnectionLease Lease, bool CanReuse);

    private sealed record Established(ConnectionLease Lease, Acquire Original);

    private sealed record EstablishFailed(Exception Ex, Acquire Original);

    private sealed class Evict
    {
        public static readonly Evict Instance = new();
    }

    private sealed class HostState
    {
        public readonly RequestEndpoint Endpoint;
        public readonly int MaxConnections;
        public readonly bool IsHttp10;

        public readonly List<ConnectionLease> Leases = [];

        public readonly Queue<ConnectionLease> Idle = new();

        public readonly Queue<Acquire> Pending = new();

        public int Establishing;

        public HostState(RequestEndpoint endpoint, int maxConnectionsPerServer)
        {
            Endpoint = endpoint;
            IsHttp10 = endpoint.Version is { Major: 1, Minor: 0 };
            MaxConnections = IsHttp10 ? int.MaxValue : maxConnectionsPerServer;
        }
    }

    private readonly Dictionary<RequestEndpoint, HostState> _hosts = new();
    private readonly TimeSpan _idleTimeout;
    private readonly int _maxConnectionsPerServer;
    private const string EvictTimerKey = "evict-idle";

    public ITimerScheduler Timers { get; set; } = null!;

    /// <summary>
    /// Sends an <see cref="Acquire"/> to the actor and returns a <see cref="Task{ConnectionLease}"/>
    /// that completes when the actor resolves the request.
    /// Cancellation is wired directly to the <see cref="TaskCompletionSource{T}"/>;
    /// the actor skips already-completed TCS instances on dequeue.
    /// </summary>
    public static Task<ConnectionLease> AcquireAsync(
        IActorRef actor, TcpOptions options, RequestEndpoint endpoint, CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource<ConnectionLease>();

        if (ct.CanBeCanceled)
        {
            ct.UnsafeRegister(
                static (state, token) => ((TaskCompletionSource<ConnectionLease>)state!).TrySetCanceled(token),
                tcs);
        }

        actor.Tell(new Acquire(options, endpoint, tcs, ct));
        return tcs.Task;
    }

    public TcpConnectionManagerActor(TimeSpan idleTimeout, int maxConnectionsPerServer = 6)
    {
        _idleTimeout = idleTimeout;
        _maxConnectionsPerServer = maxConnectionsPerServer;

        Receive<Acquire>(OnAcquire);
        Receive<Release>(OnRelease);
        Receive<Established>(OnEstablished);
        Receive<EstablishFailed>(OnFailed);
        Receive<Evict>(_ => OnEvict());
    }

    protected override void PreStart()
    {
        Timers.StartPeriodicTimer(EvictTimerKey, Evict.Instance, _idleTimeout, _idleTimeout);
    }

    private void OnAcquire(Acquire msg)
    {
        if (msg.Tcs.Task.IsCompleted)
        {
            return;
        }

        var host = GetOrCreateHost(msg.Endpoint);
        var version = msg.Endpoint.Version;

        if (version.Major >= 2)
        {
            // H2/H3 connections are exclusively owned by one TcpConnectionStage each.
            // Sharing a ConnectionHandle causes competing inbound pumps that split
            // response frames across wrong pipeline instances. Use the slot-limit
            // pattern instead: establish up to MaxConnections, then queue.
            if (host.Leases.Count + host.Establishing < host.MaxConnections)
            {
                Establish(host, msg);
            }
            else
            {
                host.Pending.Enqueue(msg);
            }

            return;
        }

        // HTTP/1.0: always new, no limit
        if (host.IsHttp10)
        {
            Establish(host, msg);
            return;
        }

        // HTTP/1.1: prefer idle reuse, then establish if slots available, else queue
        while (host.Idle.TryDequeue(out var idle))
        {
            if (idle is { IsAlive: true, Reusable: true })
            {
                idle.MarkBusy();

                if (!msg.Tcs.TrySetResult(idle))
                {
                    idle.MarkIdle();
                    host.Idle.Enqueue(idle);
                }
                else
                {
                    TurboHttpMetrics.OpenConnections.Add(-1,
                        new("http.connection.state", "idle"),
                        new("server.address", host.Endpoint.Host),
                        new("server.port", host.Endpoint.Port));
                }

                return;
            }

            // Stale — dispose and free the slot
            host.Leases.Remove(idle);
            idle.Dispose();
            TurboHttpMetrics.OpenConnections.Add(-1,
                new("http.connection.state", "active"),
                new("server.address", host.Endpoint.Host),
                new("server.port", host.Endpoint.Port));
        }

        // No idle — check slot budget
        if (host.Leases.Count + host.Establishing < host.MaxConnections)
        {
            Establish(host, msg);
        }
        else
        {
            host.Pending.Enqueue(msg);
        }
    }

    private void OnRelease(Release msg)
    {
        var endpoint = msg.Lease.Key;
        if (!_hosts.TryGetValue(endpoint, out var host))
        {
            msg.Lease.Dispose();
            return;
        }

        var version = endpoint.Version;

        // HTTP/1.0: always dispose
        if (host.IsHttp10)
        {
            host.Leases.Remove(msg.Lease);
            msg.Lease.Dispose();
            TurboHttpMetrics.OpenConnections.Add(-1,
                new("http.connection.state", "active"),
                new("server.address", host.Endpoint.Host),
                new("server.port", host.Endpoint.Port));
            return;
        }

        // HTTP/2+: exclusively owned — always dispose on release and serve next pending
        if (version.Major >= 2)
        {
            host.Leases.Remove(msg.Lease);
            msg.Lease.Dispose();
            TurboHttpMetrics.OpenConnections.Add(-1,
                new("http.connection.state", "active"),
                new("server.address", host.Endpoint.Host),
                new("server.port", host.Endpoint.Port));
            ServeNextPending(host);
            return;
        }

        // HTTP/1.1
        msg.Lease.MarkIdle();

        if (msg is { CanReuse: true, Lease: { IsAlive: true, Reusable: true } })
        {
            // Direct handoff to a pending caller
            while (host.Pending.TryDequeue(out var pending))
            {
                if (!pending.Tcs.Task.IsCompleted)
                {
                    msg.Lease.MarkBusy();
                    pending.Tcs.TrySetResult(msg.Lease);
                    return;
                }
            }

            // No pending callers — park in idle pool
            host.Idle.Enqueue(msg.Lease);
            TurboHttpMetrics.OpenConnections.Add(1,
                new("http.connection.state", "idle"),
                new("server.address", host.Endpoint.Host),
                new("server.port", host.Endpoint.Port));
        }
        else
        {
            // Not reusable — dispose and free the slot
            host.Leases.Remove(msg.Lease);
            msg.Lease.Dispose();
            TurboHttpMetrics.OpenConnections.Add(-1,
                new("http.connection.state", "active"),
                new("server.address", host.Endpoint.Host),
                new("server.port", host.Endpoint.Port));

            ServeNextPending(host);
        }
    }

    private void OnEstablished(Established msg)
    {
        var host = GetOrCreateHost(msg.Original.Endpoint);
        host.Establishing--;
        host.Leases.Add(msg.Lease);
        msg.Lease.MarkBusy();
        TurboHttpMetrics.OpenConnections.Add(1,
            new("http.connection.state", "active"),
            new("server.address", host.Endpoint.Host),
            new("server.port", host.Endpoint.Port));

        if (!msg.Original.Tcs.TrySetResult(msg.Lease))
        {
            // Original caller cancelled — treat as immediate release
            OnRelease(new Release(msg.Lease, CanReuse: true));
        }
    }

    private void OnFailed(EstablishFailed msg)
    {
        if (_hosts.TryGetValue(msg.Original.Endpoint, out var host))
        {
            host.Establishing--;
        }

        if (msg.Ex is OperationCanceledException oce)
        {
            msg.Original.Tcs.TrySetCanceled(oce.CancellationToken);
        }
        else
        {
            msg.Original.Tcs.TrySetException(msg.Ex);
        }

        if (host is not null)
        {
            ServeNextPending(host);
        }
    }

    private void OnEvict()
    {
        foreach (var (_, host) in _hosts)
        {
            EvictHost(host);
        }
    }

    private void EvictHost(HostState host)
    {
        if (host.Idle.Count == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var fresh = new List<ConnectionLease>();
        var expired = new List<ConnectionLease>();

        while (host.Idle.TryDequeue(out var idle))
        {
            if (!idle.IsAlive || now - idle.LastActivity > _idleTimeout)
            {
                expired.Add(idle);
            }
            else
            {
                fresh.Add(idle);
            }
        }

        // Keep at least one idle connection per host
        if (fresh.Count == 0 && expired.Count > 0)
        {
            var keeper = expired[0];
            for (var i = 1; i < expired.Count; i++)
            {
                if (expired[i].IsAlive && expired[i].LastActivity > keeper.LastActivity)
                {
                    keeper = expired[i];
                }
            }

            if (keeper.IsAlive)
            {
                expired.Remove(keeper);
                fresh.Add(keeper);
            }
        }

        foreach (var item in fresh)
        {
            host.Idle.Enqueue(item);
        }

        foreach (var lease in expired)
        {
            host.Leases.Remove(lease);
            lease.Dispose();
            TurboHttpMetrics.OpenConnections.Add(-1,
                new("http.connection.state", "idle"),
                new("server.address", host.Endpoint.Host),
                new("server.port", host.Endpoint.Port));
        }
    }

    protected override void PostStop()
    {
        Timers.CancelAll();

        foreach (var (_, host) in _hosts)
        {
            while (host.Pending.TryDequeue(out var pending))
            {
                pending.Tcs.TrySetException(new ObjectDisposedException(
                    nameof(TcpConnectionManagerActor),
                    "TCP connection manager was stopped while requests were pending."));
            }

            host.Idle.Clear();

            foreach (var lease in host.Leases)
            {
                lease.Dispose();
            }

            host.Leases.Clear();
        }

        _hosts.Clear();
    }

    private HostState GetOrCreateHost(RequestEndpoint endpoint)
    {
        if (!_hosts.TryGetValue(endpoint, out var state))
        {
            state = new HostState(endpoint, _maxConnectionsPerServer);
            _hosts[endpoint] = state;
        }

        return state;
    }

    private void Establish(HostState host, Acquire msg)
    {
        host.Establishing++;
        _ = DirectConnectionFactory
            .EstablishAsync(msg.Options, msg.Endpoint, msg.Token)
            .PipeTo(Self,
                success: lease => new Established(lease, msg),
                failure: ex => new EstablishFailed(ex, msg));
    }

    private void ServeNextPending(HostState host)
    {
        while (host.Pending.TryDequeue(out var next))
        {
            if (!next.Tcs.Task.IsCompleted)
            {
                Establish(host, next);
                return;
            }
        }
    }
}