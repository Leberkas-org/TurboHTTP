using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Event;
using Servus.Akka;
using TurboHttp.Client;
using TurboHttp.Internal;
using TurboHttp.IO;
using TurboHttp.IO.Stages;
using TurboHttp.Protocol.RFC9112;

namespace TurboHttp.Lifecycle;

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
        Receive<ConnectionActor.ConnectionReady>(HandleConnectionReady);
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

    private void HandleConnectionReady(ConnectionActor.ConnectionReady msg)
    {
        var conn = Find(msg.Handle.ConnectionActor);

        if (conn is null)
        {
            _log.Warning("ConnectionReady received for unknown connection {0}", msg.Handle.ConnectionActor);
            return;
        }

        conn.SetHandle(msg.Handle);

        // Flush all pending requesters
        foreach (var requester in _pendingHandleRequesters)
        {
            requester.Tell(msg.Handle);
        }

        _pendingHandleRequesters.Clear();
    }

    private void HandleEnsureHost(PoolRouter.EnsureHost msg)
    {
        // Try to find a connection with an available slot and a handle
        var conn = SelectConnection();

        if (conn?.Handle is not null)
        {
            conn.MarkBusy();
            Sender.Tell(conn.Handle);
            return;
        }

        // Queue the requester BEFORE attempting to spawn — eliminates the race
        // where ConnectionReady arrives before the requester is enqueued.
        _pendingHandleRequesters.Add(Sender);

        // Attempt to open a new connection (noop if limiter refuses)
        SpawnConnection();
    }

    private ConnectionState? SpawnConnection()
    {
        if (!_limiter.TryAcquire(HostIdentifier))
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
            props = Props.Create(() => new ConnectionActor(_options, clientManager, _key, _config));
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
        conn?.MarkIdle();
    }

    private void HandleFailure(ConnectionFailed msg)
    {
        var conn = Find(msg.Connection);

        if (conn == null)
        {
            return;
        }

        // Mark inactive before removal so any in-flight observers see a dead connection.
        conn.MarkDead();

        // Remove stale connection state immediately.
        _connections.Remove(conn);

        _limiter.Release(HostIdentifier);

        Context.System.Scheduler.ScheduleTellOnceCancelable(
            _config.ReconnectInterval,
            Self,
            new Reconnect(msg.Connection),
            Self);
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

            Context.Unwatch(conn.Actor);
            conn.Actor.Tell(PoisonPill.Instance);
            _connections.Remove(conn);
            _limiter.Release(HostIdentifier);
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
        conn?.MarkIdle();

        // A stream freed up — try to serve queued requesters
        ServeQueuedRequesters();
    }

    private void HandleStreamAcquired(StreamAcquired msg)
    {
        var conn = Find(msg.Connection);
        conn?.MarkBusy();
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
            conn.MarkBusy();
            requester.Tell(conn.Handle);
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