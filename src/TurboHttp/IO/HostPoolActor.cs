using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Akka;
using Akka.Actor;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Dsl;
using Servus.Akka;
using TurboHttp.IO.Stages;

namespace TurboHttp.IO;

public sealed class HostPoolActor : ReceiveActor
{
    public record HostPoolConfig(TcpOptions Options, PoolConfig Config, HostKey Key);

    // ── Public message protocol ───────────────────────────────────────

    public sealed record ConnectionIdle(IActorRef Connection);

    public sealed record ConnectionFailed(IActorRef Connection);

    public sealed record RegisterConnectionRefs(
        IActorRef Connection,
        Source<DataItem, NotUsed> ResponseSource);

    public sealed record IdleCheck;

    public sealed record Reconnect(IActorRef Connection);

    public sealed record StreamComplete(IActorRef Connection);

    public sealed record MarkConnectionNoReuse(IActorRef Connection);

    // ── Fields ────────────────────────────────────────────────────────

    private readonly HostKey _key;
    private readonly TcpOptions _options;
    private readonly PoolConfig _config;
    private ICancelable? _scheduler;

    private readonly ILoggingAdapter _log = Context.GetLogger();

    private readonly List<ConnectionState> _connections = [];

    /// <summary>Per-connection outbound request queues, keyed by ConnectionActor ref.</summary>
    private readonly Dictionary<IActorRef, ISourceQueueWithComplete<DataItem>> _connectionQueues = new();

    /// <summary>Requests waiting for a connection with an established queue.</summary>
    private readonly Queue<DataItem> _pending = new();

    private IMaterializer? _mat;
    private Sink<DataItem, NotUsed>? _mergeHubSink;

    /// <summary>Aggregated response stream; wired into PoolRouterActor's global MergeHub.</summary>
    private Source<DataItem, NotUsed>? _responseSource;

    public HostPoolActor(HostPoolConfig config)
    {
        _options = config.Options;
        _config = config.Config;
        _key = config.Key;

        Receive<ConnectionIdle>(HandleIdle);
        Receive<ConnectionFailed>(HandleFailure);
        Receive<IdleCheck>(_ => EvictIdleConnections());
        Receive<Reconnect>(HandleReconnect);
        Receive<MarkConnectionNoReuse>(HandleMarkNoReuse);
        Receive<RegisterConnectionRefs>(HandleRegisterConnectionRefs);
        Receive<DataItem>(HandleDataItem);
    }

    protected override void PreStart()
    {
        _scheduler = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(
            _config.IdleCheckInterval,
            _config.IdleCheckInterval,
            Self,
            new IdleCheck(),
            Self);

        _mat = Context.System.Materializer();

        // ── Response aggregation: all connections → PoolRouterActor's global MergeHub ──
        var (mergeHubSink, responseSource) = MergeHub.Source<DataItem>().PreMaterialize(_mat);
        _mergeHubSink = mergeHubSink;
        _responseSource = responseSource;

        // Wire this host's aggregated response source into the global MergeHub via parent.
        Context.Parent.Tell(new PoolRouterActor.RegisterHostResponseSource(_responseSource));

        // Eagerly establish the first connection
        SpawnConnection();
    }

    protected override void PostStop()
    {
        _scheduler?.Cancel();
    }

    // ── Request routing ───────────────────────────────────────────────

    private void HandleDataItem(DataItem item)
    {
        var conn = SelectConnectionWithQueue(item.Key.HttpVersion);
        if (conn != null)
        {
            _ = _connectionQueues[conn.Actor].OfferAsync(item);
        }
        else
        {
            _pending.Enqueue(item);
            if (_connections.Count == 0)
            {
                SpawnConnection();
            }
        }
    }

    private void HandleRegisterConnectionRefs(RegisterConnectionRefs msg)
    {
        // Create a per-connection request queue (buffer 128)
        var (connectionQueue, connectionSource) =
            Source.Queue<DataItem>(128, OverflowStrategy.Backpressure)
                .PreMaterialize(_mat!);

        // Wire queue items → ConnectionActor as messages (which writes to TCP outbound)
        var connection = msg.Connection;
        connectionSource.RunWith(
            Sink.ForEach<DataItem>(item => connection.Tell(item)),
            _mat!);

        // Wire ConnectionActor's response source → MergeHub (merged response stream)
        msg.ResponseSource.RunWith(_mergeHubSink!, _mat!);

        _connectionQueues[msg.Connection] = connectionQueue;

        // Drain any pending items that were queued while waiting for a connection
        DrainPending(connectionQueue);
    }

    private void DrainPending(ISourceQueueWithComplete<DataItem> queue)
    {
        while (_pending.TryDequeue(out var item))
        {
            _ = queue.OfferAsync(item);
        }
    }

    // ── Connection selection ──────────────────────────────────────────

    /// <summary>
    /// Selects an available connection that also has a registered queue.
    /// Only connections with queues can receive routed requests.
    /// </summary>
    private ConnectionState? SelectConnectionWithQueue(Version? version = null)
    {
        version ??= HttpVersion.Version11;

        if (version == HttpVersion.Version20)
        {
            return _connections.FirstOrDefault(x =>
                x is { Active: true } &&
                x.HttpVersion == HttpVersion.Version20 &&
                _connectionQueues.ContainsKey(x.Actor));
        }

        if (version == HttpVersion.Version10)
        {
            return _connections.FirstOrDefault(x =>
                x is { Active: true, Idle: true, Reusable: true } &&
                x.HttpVersion == HttpVersion.Version10 &&
                x.PendingRequests < _config.MaxRequestsPerConnection &&
                _connectionQueues.ContainsKey(x.Actor));
        }

        // HTTP/1.1 default
        return _connections.FirstOrDefault(x =>
            x is { Active: true, Idle: true, Reusable: true } &&
            x.PendingRequests < _config.MaxRequestsPerConnection &&
            _connectionQueues.ContainsKey(x.Actor));
    }

    // ── Connection lifecycle ──────────────────────────────────────────

    private ConnectionState SpawnConnection()
    {
        var clientManager = Context.GetActor<ClientManager>();
        var actor = Context.ActorOf(Props.Create(() => new ConnectionActor(_options, clientManager, _key)));

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

        conn.MarkDead();

        // Remove queue for the failed connection
        if (_connectionQueues.Remove(msg.Connection, out var queue))
        {
            queue.Complete();
        }

        Context.System.Scheduler.ScheduleTellOnceCancelable(
            _config.ReconnectInterval,
            Self,
            new Reconnect(msg.Connection),
            Self);
    }

    private void HandleReconnect(Reconnect msg)
    {
        var conn = Find(msg.Connection);

        if (conn == null)
        {
            return;
        }

        var previousVersion = conn.HttpVersion;
        _connections.Remove(conn);

        var newConn = SpawnConnection();
        newConn.HttpVersion = previousVersion;
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

            if (now - conn.LastActivity > _config.IdleTimeout && _connections.Count > 1)
            {
                Context.Unwatch(conn.Actor);
                conn.Actor.Tell(PoisonPill.Instance);
                _connectionQueues.Remove(conn.Actor, out _);
                _connections.Remove(conn);
            }
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
