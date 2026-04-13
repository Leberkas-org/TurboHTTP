using System.Runtime.Versioning;
using Akka.Actor;
using TurboHTTP.Diagnostics;
using TurboHTTP.Internal;
using TurboHTTP.Transport.Quic;

// QUIC APIs are platform-guarded; usage is gated at runtime via QuicOptions.
#pragma warning disable CA1416

namespace TurboHTTP.Transport.Connection;

/// <summary>
/// Single actor that manages ALL per-host QUIC connection state: acquire, release, idle reuse,
/// eviction, and per-host connection limits. Every <see cref="TurboHTTP.Transport.Quic.QuicConnectionStage"/>
/// talks to this actor via <see cref="Acquire"/> / <see cref="Release"/>.
/// <para>
/// Per-host state (leases, pending queue, establishing count) is kept in a
/// <see cref="Dictionary{TKey,TValue}"/> of <see cref="HostState"/> instances — all accessed
/// on the actor's single-threaded mailbox, so no locks needed.
/// </para>
/// Key difference from <see cref="TcpConnectionManagerActor"/>: no separate Idle queue.
/// QUIC connections are multiplexed internally, so <see cref="HostState.Leases"/> is scanned
/// for <see cref="QuicConnectionLease.CanAcceptStream"/> on every acquire.
/// Mirrors <see cref="TcpConnectionManagerActor"/> structurally — a senior dev who knows
/// one immediately understands the other.
/// </summary>
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macOS")]
[SupportedOSPlatform("windows")]
internal sealed class QuicConnectionManagerActor : ReceiveActor, IWithTimers
{
    internal sealed record Acquire(
        QuicOptions Options,
        RequestEndpoint Endpoint,
        TaskCompletionSource<QuicConnectionLease> Tcs,
        CancellationToken Token);

    internal sealed record Release(QuicConnectionLease Lease, bool CanReuse);

    private sealed record Established(QuicConnectionLease Lease, Acquire Original);

    private sealed record EstablishFailed(Exception Ex, Acquire Original);

    private sealed class Evict
    {
        public static readonly Evict Instance = new();
    }

    private sealed class HostState
    {
        public readonly RequestEndpoint Endpoint;
        public readonly int MaxConnections;

        public readonly List<QuicConnectionLease> Leases = [];

        public readonly Queue<Acquire> Pending = new();

        public int Establishing;

        public HostState(RequestEndpoint endpoint, int maxConnectionsPerHost)
        {
            Endpoint = endpoint;
            MaxConnections = maxConnectionsPerHost;
        }
    }

    private readonly Dictionary<RequestEndpoint, HostState> _hosts = new();
    private readonly TimeSpan _idleTimeout;
    private readonly int _maxConnectionsPerHost;
    private const string EvictTimerKey = "evict-idle";

    public ITimerScheduler Timers { get; set; } = null!;

    /// <summary>
    /// Sends an <see cref="Acquire"/> to the actor and returns a <see cref="Task{QuicConnectionLease}"/>
    /// that completes when the actor resolves the request.
    /// Cancellation is wired directly to the <see cref="TaskCompletionSource{T}"/>;
    /// the actor skips already-completed TCS instances on dequeue.
    /// </summary>
    public static Task<QuicConnectionLease> AcquireAsync(
        IActorRef actor, QuicOptions options, RequestEndpoint endpoint, CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource<QuicConnectionLease>();

        if (ct.CanBeCanceled)
        {
            ct.UnsafeRegister(
                static (state, token) => ((TaskCompletionSource<QuicConnectionLease>)state!).TrySetCanceled(token),
                tcs);
        }

        actor.Tell(new Acquire(options, endpoint, tcs, ct));
        return tcs.Task;
    }

    public QuicConnectionManagerActor(TimeSpan idleTimeout, int maxConnectionsPerHost = 1)
    {
        _idleTimeout = idleTimeout;
        _maxConnectionsPerHost = maxConnectionsPerHost;

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

        // Scan existing connections for available capacity
        foreach (var lease in host.Leases)
        {
            if (!lease.CanAcceptStream)
            {
                continue;
            }

            lease.MarkBusy();

            if (!msg.Tcs.TrySetResult(lease))
            {
                lease.MarkIdle();
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

        // No existing connection with capacity — establish new if slot available
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
            msg.Lease.MarkIdle();
            if (!msg.CanReuse)
            {
                msg.Lease.MarkNoReuse();
            }

            if (msg.Lease.ActiveStreams == 0)
            {
                msg.Lease.Dispose();
            }

            return;
        }

        msg.Lease.MarkIdle();

        if (!msg.CanReuse || !msg.Lease.IsAlive)
        {
            msg.Lease.MarkNoReuse();

            if (msg.Lease.ActiveStreams == 0)
            {
                host.Leases.Remove(msg.Lease);
                msg.Lease.Dispose();
                TurboHttpMetrics.OpenConnections.Add(-1,
                    new("http.connection.state", "active"),
                    new("server.address", host.Endpoint.Host),
                    new("server.port", host.Endpoint.Port));
            }

            ServeNextPending(host);
            return;
        }

        // Reusable — try direct handoff to a pending caller
        while (host.Pending.TryDequeue(out var pending))
        {
            if (!pending.Tcs.Task.IsCompleted && msg.Lease.CanAcceptStream)
            {
                msg.Lease.MarkBusy();

                if (pending.Tcs.TrySetResult(msg.Lease))
                {
                    return;
                }

                msg.Lease.MarkIdle(); // cancelled — try next
            }
        }

        // No pending — connection stays in Leases pool; next Acquire scans CanAcceptStream
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
        if (host.Leases.Count == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var toEvict = new List<QuicConnectionLease>();
        var toKeep = new List<QuicConnectionLease>();

        foreach (var lease in host.Leases)
        {
            // Evict dead leases, or idle leases (no active streams) that have expired
            var idle = lease.ActiveStreams == 0;
            if (!lease.IsAlive || (idle && now - lease.LastActivity > _idleTimeout))
            {
                toEvict.Add(lease);
            }
            else
            {
                toKeep.Add(lease);
            }
        }

        // Keep at least one connection per host (sentinel — most recently active)
        if (toKeep.Count == 0 && toEvict.Count > 0)
        {
            var keeper = toEvict[0];
            for (var i = 1; i < toEvict.Count; i++)
            {
                if (toEvict[i].IsAlive && toEvict[i].LastActivity > keeper.LastActivity)
                {
                    keeper = toEvict[i];
                }
            }

            if (keeper.IsAlive)
            {
                toEvict.Remove(keeper);
                toKeep.Add(keeper);
            }
        }

        host.Leases.Clear();
        foreach (var lease in toKeep)
        {
            host.Leases.Add(lease);
        }

        foreach (var lease in toEvict)
        {
            lease.Dispose();
            TurboHttpMetrics.OpenConnections.Add(-1,
                new("http.connection.state", "active"),
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
                    nameof(QuicConnectionManagerActor),
                    "QUIC connection manager was stopped while requests were pending."));
            }

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
            state = new HostState(endpoint, _maxConnectionsPerHost);
            _hosts[endpoint] = state;
        }

        return state;
    }

    private void Establish(HostState host, Acquire msg)
    {
        host.Establishing++;
        QuicConnectionFactory
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
                // Check if any existing connection now has capacity
                foreach (var lease in host.Leases)
                {
                    if (lease.CanAcceptStream)
                    {
                        lease.MarkBusy();
                        if (next.Tcs.TrySetResult(lease))
                        {
                            return;
                        }
                        lease.MarkIdle(); // cancelled — try next pending
                    }
                }

                // Establish new connection if slot available
                if (host.Leases.Count + host.Establishing < host.MaxConnections)
                {
                    Establish(host, next);
                    return;
                }

                // All slots taken — put back and wait for next release
                host.Pending.Enqueue(next);
                return;
            }
        }
    }
}
