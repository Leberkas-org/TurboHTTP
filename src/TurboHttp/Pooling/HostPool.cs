using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Event;
using Servus.Akka;
using TurboHttp.Diagnostics;
using TurboHttp.Internal;
using TurboHttp.Transport;
using TurboHttp.Protocol.RFC9112;

namespace TurboHttp.Pooling;

public sealed class HostPool : ReceiveActor
{
    public record HostPoolConfig(
        TcpOptions Options,
        TurboClientOptions Config,
        RequestEndpoint Key,
        Func<Props>? ConnectionFactory = null);

    public sealed record ConnectionIdle(IActorRef Connection);

    public sealed record ConnectionFailed(IActorRef Connection);

    public sealed record IdleCheck;

    public sealed record Reconnect(IActorRef Connection);

    public sealed record MarkConnectionNoReuse(IActorRef Connection);

    public sealed record StreamCompleted(IActorRef Connection);

    public sealed record StreamAcquired(IActorRef Connection);

    public sealed record UpdateMaxConcurrentStreams(IActorRef Connection, int MaxStreams);

    private readonly RequestEndpoint _key;
    private readonly TcpOptions _options;
    private readonly TurboClientOptions _config;
    private readonly PerHostConnectionLimiter _limiter;
    private readonly Func<Props>? _connectionFactory;
    private ICancelable? _scheduler;

    private readonly ILoggingAdapter _log = Context.GetLogger();

    private readonly List<ConnectionState> _connections = [];

    /// <summary>Requesters waiting for a ConnectionHandle (queued when no connection with a free slot exists).</summary>
    private readonly List<IActorRef> _pendingHandleRequesters = [];

    /// <summary>Host identifier used for the per-host connection limiter.</summary>
    private string HostIdentifier => string.IsNullOrEmpty(_key.Host) ? "default" : $"{_key.Host}:{_key.Port}";

    /// <summary>Whether this pool manages QUIC/HTTP3 connections that require OS-level stream management.</summary>
    private bool IsQuic => _key.Version is { Major: 3 };

    /// <summary>Whether this pool manages connections that multiplex multiple streams (HTTP/2+, QUIC/HTTP3).</summary>
    private bool IsMultiStream => _key.Version.Major >= 2;

    public HostPool(HostPoolConfig config)
    {
        _options = config.Options;
        _config = config.Config;
        _key = config.Key;
        _limiter = new PerHostConnectionLimiter();
        _connectionFactory = config.ConnectionFactory;

        Receive<ConnectionIdle>(HandleIdle);
        Receive<ConnectionFailed>(HandleFailure);
        Receive<IdleCheck>(_ => EvictIdleConnections());
        Receive<Reconnect>(HandleReconnect);
        Receive<MarkConnectionNoReuse>(HandleMarkNoReuse);
        Receive<StreamCompleted>(HandleStreamCompleted);
        Receive<StreamAcquired>(HandleStreamAcquired);
        Receive<UpdateMaxConcurrentStreams>(HandleUpdateMaxConcurrentStreams);
        Receive<Terminated>(HandleTerminated);
        Receive<ConnectionActorBase.ConnectionReady>(HandleConnectionReady);
        Receive<PoolRouter.EnsureHost>(HandleEnsureHost);
    }

    protected override void PreStart()
    {
        _scheduler = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(
            _config.IdleTimeout,
            _config.IdleTimeout,
            Self,
            new IdleCheck(),
            Self);

        // Eagerly establish the first connection
        SpawnConnection();
    }

    protected override void PostStop()
    {
        _scheduler?.Cancel();
    }

    private KeyValuePair<string, object?>[] HostTags =>
    [
        new("server.address", _key.Host ?? "unknown"),
        new("server.port", _key.Port)
    ];

    private void HandleConnectionReady(ConnectionActorBase.ConnectionReady msg)
    {
        var conn = Find(msg.Handle.ConnectionActor);

        if (conn is null)
        {
            _log.Warning("ConnectionReady received for unknown connection {0}", msg.Handle.ConnectionActor);
            return;
        }

        conn.SetHandle(msg.Handle);

        // New connection is active and idle
        TurboHttpMetrics.ConnectionActive.Add(1, HostTags);
        TurboHttpMetrics.ConnectionIdle.Add(1, HostTags);

        if (IsMultiStream)
        {
            // HTTP/2+, QUIC: serve via version-aware path (respects MaxConcurrentStreams)
            ServeQueuedRequesters();
        }
        else
        {
            // HTTP/1.x: flush all pending requesters with the shared handle
            foreach (var requester in _pendingHandleRequesters)
            {
                requester.Tell(msg.Handle);
            }

            _pendingHandleRequesters.Clear();
        }
    }

    private void HandleEnsureHost(PoolRouter.EnsureHost msg)
    {
        // Try to find a connection with an available slot and a handle
        var conn = SelectConnection();

        if (conn?.Handle is not null)
        {
            if (IsQuic)
            {
                // QUIC: request a new stream on the existing connection.
                // Each requester gets its own channel pair via OpenTypedStream.
                var wasIdleQuic = conn.Idle;
                conn.MarkBusy();
                if (wasIdleQuic && !conn.Idle)
                {
                    TurboHttpMetrics.ConnectionIdle.Add(-1, HostTags);
                }

                conn.Actor.Tell(new Http3ConnectionActor.OpenTypedStream(Sender, OutputStreamType.Request));
                return;
            }

            // HTTP/2: don't MarkBusy here — Http20ConnectionStage emits StreamAcquireItem
            // which triggers HandleStreamAcquired → MarkBusy. Double-counting would inflate
            // PendingRequests and break HasAvailableSlot.
            if (!IsMultiStream)
            {
                var wasIdleH1 = conn.Idle;
                conn.MarkBusy();
                if (wasIdleH1 && !conn.Idle)
                {
                    TurboHttpMetrics.ConnectionIdle.Add(-1, HostTags);
                }
            }

            Sender.Tell(conn.Handle);
            return;
        }

        // Queue the requester BEFORE attempting to spawn — eliminates the race
        // where ConnectionReady arrives before the requester is enqueued.
        // Deduplicate: ConnectionStage retries EnsureHost on timeout, which would
        // queue the same StageActor multiple times. Without deduplication, a single
        // ConnectionReady delivers the handle N times → concurrent inbound pumps.
        if (!_pendingHandleRequesters.Contains(Sender))
        {
            _pendingHandleRequesters.Add(Sender);
        }

        // Don't spawn extra connections while one is still connecting and no ready connection exists.
        // For QUIC this avoids duplicate QUIC connections; for HTTP/1.x it prevents the race
        // where PreStart's eager connection hasn't received its handle yet and EnsureHost spawns a
        // second one whose ByteMover tasks can interfere with the pipeline.
        if (_connections.Exists(c => c.Handle is null && c.Active))
        {
            return;
        }

        // Attempt to open a new connection (noop if limiter refuses)
        SpawnConnection();
    }

    private ConnectionState? SpawnConnection()
    {
        // HTTP/2+, QUIC: one connection per host is optimal — skip the per-host limiter.
        if (!IsMultiStream && !_limiter.TryAcquire(HostIdentifier))
        {
            _log.Debug("Per-host connection limit ({0}) reached for {1}, request queued", 6, HostIdentifier);
            return null;
        }

        Props props;
        if (_connectionFactory != null)
        {
            props = _connectionFactory();
        }
        else
        {
            var clientManager = Context.GetActor<ClientManager>();
            props = _key.Version.Major switch
            {
                3 => Props.Create(() => new Http3ConnectionActor((QuicOptions)_options, clientManager, _key, _config)),
                2 => Props.Create(() => new Http2ConnectionActor(_options, clientManager, _key, _config)),
                _ => Props.Create(() => new Http1ConnectionActor(_options, clientManager, _key, _config)),
            };
        }

        var actor = Context.ActorOf(props);

        Context.Watch(actor);

        var state = new ConnectionState(actor);
        _connections.Add(state);

        return state;
    }

    private void HandleIdle(ConnectionIdle msg)
    {
        var conn = Find(msg.Connection);
        if (conn is not null)
        {
            var wasIdle = conn.Idle;
            conn.MarkIdle();
            if (!wasIdle && conn.Idle)
            {
                TurboHttpMetrics.ConnectionIdle.Add(1, HostTags);
            }
        }
    }

    private void HandleTerminated(Terminated msg)
    {
        var conn = Find(msg.ActorRef);

        if (conn == null)
        {
            return;
        }

        _log.Debug("HostPool: Watched connection actor terminated: {0}", msg.ActorRef);

        // Treat the same as ConnectionFailed — clean up and immediately spawn replacement.
        RemoveFailedConnection(conn);
    }

    private void HandleFailure(ConnectionFailed msg)
    {
        var conn = Find(msg.Connection);

        if (conn == null)
        {
            return;
        }

        RemoveFailedConnection(conn);
    }

    /// <summary>
    /// Removes a failed/terminated connection, immediately spawns a replacement,
    /// and serves any queued requesters. This avoids the previous 5-second
    /// <see cref="TcpOptions.ReconnectInterval"/> delay that caused HTTP/1.0
    /// multi-request tests to time out.
    /// </summary>
    private void RemoveFailedConnection(ConnectionState conn)
    {
        // Record metrics before state change
        TurboHttpMetrics.ConnectionActive.Add(-1, HostTags);
        if (conn.Idle)
        {
            TurboHttpMetrics.ConnectionIdle.Add(-1, HostTags);
        }

        // Mark inactive before removal so any in-flight observers see a dead connection.
        conn.MarkDead();

        Context.Unwatch(conn.Actor);

        // Remove stale connection state immediately.
        _connections.Remove(conn);

        if (!IsMultiStream)
        {
            _limiter.Release(HostIdentifier);
        }

        // Immediately spawn a replacement connection so that queued requesters
        // are served as soon as the new connection is ready — no delay.
        if (_pendingHandleRequesters.Count > 0 || _connections.Count == 0)
        {
            SpawnConnection();
        }
    }

    private void HandleReconnect(Reconnect msg)
    {
        // Connection was already removed from _connections in HandleFailure.
        // Spawn a fresh replacement connection.
        SpawnConnection();
    }

    private void EvictIdleConnections()
    {
        var now = DateTime.UtcNow;

        foreach (var conn in _connections.ToArray())
        {
            if (!conn.Idle)
            {
                continue;
            }

            var expiredIdle = now - conn.LastActivity > _config.IdleTimeout && _connections.Count > 1;
            var nonReusable = !conn.Reusable;

            if (!expiredIdle && !nonReusable) continue;

            TurboHttpMetrics.ConnectionActive.Add(-1, HostTags);
            if (conn.Idle)
            {
                TurboHttpMetrics.ConnectionIdle.Add(-1, HostTags);
            }

            // Do NOT send PoisonPill — the mover tasks may still be draining
            // the pipe. The actor will stop itself via its disconnect/terminate path.
            Context.Unwatch(conn.Actor);
            _connections.Remove(conn);

            if (!IsMultiStream)
            {
                _limiter.Release(HostIdentifier);
            }
        }

        ServeQueuedRequesters();
    }

    private void HandleMarkNoReuse(MarkConnectionNoReuse msg)
    {
        var conn = Find(msg.Connection);
        conn?.MarkNoReuse();
    }

    private void HandleStreamCompleted(StreamCompleted msg)
    {
        var conn = Find(msg.Connection);
        if (conn is not null)
        {
            var wasIdle = conn.Idle;
            conn.MarkIdle();
            if (!wasIdle && conn.Idle)
            {
                TurboHttpMetrics.ConnectionIdle.Add(1, HostTags);
            }

            // Eagerly remove non-reusable idle connections from the tracking list
            // and release the per-host limiter slot. Do NOT send PoisonPill here —
            // the connection actor's mover tasks may still be draining the pipe.
            // The actor will stop itself via its own disconnect/terminate path.
            if (!conn.Reusable && conn.Idle)
            {
                TurboHttpMetrics.ConnectionActive.Add(-1, HostTags);
                TurboHttpMetrics.ConnectionIdle.Add(-1, HostTags);

                Context.Unwatch(conn.Actor);
                _connections.Remove(conn);

                if (!IsMultiStream)
                {
                    _limiter.Release(HostIdentifier);
                }
            }
        }

        // A stream freed up — try to serve queued requesters
        ServeQueuedRequesters();
    }

    private void HandleStreamAcquired(StreamAcquired msg)
    {
        var conn = Find(msg.Connection);
        if (conn is not null)
        {
            var wasIdle = conn.Idle;
            conn.MarkBusy();
            if (wasIdle && !conn.Idle)
            {
                TurboHttpMetrics.ConnectionIdle.Add(-1, HostTags);
            }
        }
    }

    private void HandleUpdateMaxConcurrentStreams(UpdateMaxConcurrentStreams msg)
    {
        var conn = Find(msg.Connection);

        conn?.Handle?.UpdateMaxConcurrentStreams(msg.MaxStreams);

        // Limit may have increased — try to serve queued requesters
        ServeQueuedRequesters();
    }

    /// <summary>
    /// Drains the pending requester queue one-at-a-time, selecting the best connection
    /// for each requester and marking it busy before serving the next.
    /// Stops when no connection with a free slot is available.
    /// </summary>
    private void ServeQueuedRequesters()
    {
        while (_pendingHandleRequesters.Count > 0)
        {
            var conn = SelectConnection();

            if (conn?.Handle is null)
            {
                break;
            }

            var requester = _pendingHandleRequesters[0];
            _pendingHandleRequesters.RemoveAt(0);

            if (IsQuic)
            {
                // QUIC: each requester needs its own stream (own channel pair).
                // MarkBusy here — no StreamAcquired from Http20ConnectionStage for QUIC.
                var wasIdleQuic = conn.Idle;
                conn.MarkBusy();
                if (wasIdleQuic && !conn.Idle)
                {
                    TurboHttpMetrics.ConnectionIdle.Add(-1, HostTags);
                }

                conn.Actor.Tell(new Http3ConnectionActor.OpenTypedStream(requester, OutputStreamType.Request));
            }
            else if (IsMultiStream)
            {
                // HTTP/2: don't MarkBusy — Http20ConnectionStage emits StreamAcquireItem.
                requester.Tell(conn.Handle);
            }
            else
            {
                // HTTP/1.x: MarkBusy here — no stage-level stream accounting.
                var wasIdleH1 = conn.Idle;
                conn.MarkBusy();
                if (wasIdleH1 && !conn.Idle)
                {
                    TurboHttpMetrics.ConnectionIdle.Add(-1, HostTags);
                }

                requester.Tell(conn.Handle);
            }
        }
    }

    /// <summary>
    /// Selects the most-recently-used connection that has a free stream slot.
    /// MRU ordering minimises the number of idle connections by packing requests
    /// onto the most recently active connection first.
    /// </summary>
    /// <returns>The best eligible <see cref="ConnectionState"/>, or <c>null</c> if none qualify.</returns>
    internal static ConnectionState? SelectConnection(List<ConnectionState> connections)
    {
        ConnectionState? best = null;

        foreach (var conn in connections)
        {
            if (!conn.HasAvailableSlot)
            {
                continue;
            }

            if (best is null || conn.LastActivity > best.LastActivity)
            {
                best = conn;
            }
        }

        return best;
    }

    private ConnectionState? SelectConnection() => SelectConnection(_connections);

    private ConnectionState? Find(IActorRef actor)
        => _connections.Find(x => x.Actor.Equals(actor));
}