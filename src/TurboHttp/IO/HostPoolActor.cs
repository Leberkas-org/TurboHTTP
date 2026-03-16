using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Event;
using Servus.Akka;
using TurboHttp.Client;
using TurboHttp.IO.Stages;
using TurboHttp.Protocol.RFC9112;

namespace TurboHttp.IO;

public sealed class HostPoolActor : ReceiveActor
{
    public record HostPoolConfig(TcpOptions Options, TurboClientOptions Config, HostKey Key,
        Func<Props>? ConnectionFactory = null);

    // ── Public message protocol ───────────────────────────────────────

    public sealed record ConnectionIdle(IActorRef Connection);

    public sealed record ConnectionFailed(IActorRef Connection);

    public sealed record IdleCheck;

    public sealed record Reconnect(IActorRef Connection);

    public sealed record MarkConnectionNoReuse(IActorRef Connection);

    // ── Fields ────────────────────────────────────────────────────────

    private readonly HostKey _key;
    private readonly TcpOptions _options;
    private readonly TurboClientOptions _config;
    private readonly PerHostConnectionLimiter _limiter;
    private readonly Func<Props>? _connectionFactory;
    private ICancelable? _scheduler;

    private readonly ILoggingAdapter _log = Context.GetLogger();

    private readonly List<ConnectionState> _connections = [];

    /// <summary>Active connection handle (from the most recent ConnectionReady).</summary>
    private ConnectionHandle? _activeHandle;

    /// <summary>Requesters waiting for a ConnectionHandle (queued when no active handle exists).</summary>
    private readonly List<IActorRef> _pendingHandleRequesters = [];

    /// <summary>Host identifier used for the per-host connection limiter.</summary>
    private string HostIdentifier => string.IsNullOrEmpty(_key.Host) ? "default" : $"{_key.Host}:{_key.Port}";

    public HostPoolActor(HostPoolConfig config)
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
        Receive<ConnectionActor.ConnectionReady>(HandleConnectionReady);
        Receive<PoolRouterActor.EnsureHost>(HandleEnsureHost);
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

    // ── ConnectionHandle forwarding ───────────────────────────────────

    private void HandleConnectionReady(ConnectionActor.ConnectionReady msg)
    {
        _activeHandle = msg.Handle;

        // Flush all pending requesters
        foreach (var requester in _pendingHandleRequesters)
        {
            requester.Tell(msg.Handle);
        }

        _pendingHandleRequesters.Clear();
    }

    private void HandleEnsureHost(PoolRouterActor.EnsureHost msg)
    {
        // If we already have an active handle, reply immediately
        if (_activeHandle is not null)
        {
            Sender.Tell(_activeHandle);
            return;
        }

        // Queue the requester — they'll be served when ConnectionReady arrives
        _pendingHandleRequesters.Add(Sender);

        // If there are no active connections, try to spawn one
        if (_connections.All(c => !c.Active))
        {
            SpawnConnection();
        }
    }

    // ── Connection lifecycle ──────────────────────────────────────────

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

        // AC2: mark inactive before removal (for any in-flight observers)
        conn.MarkDead();

        // AC1: remove stale connection state immediately
        _connections.Remove(conn);

        _limiter.Release(HostIdentifier);

        // AC3: invalidate the active handle if it belongs to the failed connection
        if (_activeHandle?.ConnectionActor.Equals(msg.Connection) == true)
        {
            _activeHandle = null;
        }

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

            // Invalidate active handle if this was the active connection
            if (_activeHandle?.ConnectionActor.Equals(conn.Actor) == true)
            {
                _activeHandle = null;
            }
        }

        // Try to serve pending requesters by spawning new connections if slots freed
        if (_activeHandle is null && _pendingHandleRequesters.Count > 0)
        {
            SpawnConnection();
        }
    }

    private void HandleMarkNoReuse(MarkConnectionNoReuse msg)
    {
        var conn = Find(msg.Connection);
        conn?.MarkNoReuse();
    }

    private ConnectionState? Find(IActorRef actor)
        => _connections.Find(x => x.Actor.Equals(actor));
}