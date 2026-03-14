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
    // ── Private async-handshake messages ─────────────────────────────
    private sealed record MergeHubReady(ISourceRef<IDataItem> SourceRef);

    private sealed record MergeHubFailed(Exception Error);

    // ── Public message protocol ───────────────────────────────────────
    public sealed record ConnectionIdle(IActorRef Connection);

    public sealed record ConnectionFailed(IActorRef Connection);

    public sealed record RegisterConnectionRefs(
        IActorRef Connection,
        ISinkRef<IDataItem> Sink,
        ISourceRef<IDataItem> Source);

    public sealed record HostStreamRefsReady(HostKey Key, ISourceRef<IDataItem> Source);

    public sealed record IdleCheck;

    public sealed record Reconnect(IActorRef Connection);

    public sealed record StreamComplete(IActorRef Connection);

    public sealed record MarkConnectionNoReuse(IActorRef Connection);

    // ── Fields ────────────────────────────────────────────────────────
    private readonly TcpOptions _options;
    private readonly PoolConfig _config;

    private readonly ILoggingAdapter _log = Context.GetLogger();

    private readonly List<ConnectionState> _connections = [];

    /// <summary>Per-connection outbound request queues, keyed by ConnectionActor ref.</summary>
    private readonly Dictionary<IActorRef, ISourceQueueWithComplete<IDataItem>> _connectionQueues = new();

    /// <summary>Requests waiting for a connection with an established queue.</summary>
    private readonly Queue<DataItem> _pending = new();

    private IMaterializer? _mat;
    private Sink<IDataItem, NotUsed>? _mergeHubSink;

    public HostPoolActor(TcpOptions options, PoolConfig config)
    {
        _options = options;
        _config = config;

        Receive<ConnectionIdle>(HandleIdle);
        Receive<ConnectionFailed>(HandleFailure);
        Receive<IdleCheck>(_ => EvictIdleConnections());
        Receive<Reconnect>(HandleReconnect);
        Receive<StreamComplete>(HandleStreamComplete);
        Receive<MarkConnectionNoReuse>(HandleMarkNoReuse);
        Receive<RegisterConnectionRefs>(HandleRegisterConnectionRefs);
        Receive<DataItem>(HandleDataItem);

        Receive<MergeHubReady>(msg =>
        {
            var key = new HostKey { Schema = "http", Host = _options.Host, Port = (ushort)_options.Port };
            Context.Parent.Tell(new HostStreamRefsReady(key, msg.SourceRef));
        });

        Receive<MergeHubFailed>(msg => { _log.Error(msg.Error, "MergeHub SourceRef initialization failed"); });
    }

    protected override void PreStart()
    {
        Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(
            _config.IdleCheckInterval,
            _config.IdleCheckInterval,
            Self,
            new IdleCheck(),
            Self);

        _mat = Context.System.Materializer();

        var (sink, source) = MergeHub.Source<IDataItem>().PreMaterialize(_mat);
        _mergeHubSink = sink;

        source.RunWith(StreamRefs.SourceRef<IDataItem>(), _mat)
            .PipeTo(
                Self,
                success: sourceRef => new MergeHubReady(sourceRef),
                failure: ex => new MergeHubFailed(ex));
    }

    // ── Request routing ───────────────────────────────────────────────

    private void HandleDataItem(DataItem item)
    {
        var conn = SelectConnectionWithQueue();

        if (conn != null)
        {
            conn.MarkBusy();
            _ = _connectionQueues[conn.Actor].OfferAsync(item);
        }
        else if (_connections.Count < _config.MaxConnectionsPerHost)
        {
            SpawnConnection();
            _pending.Enqueue(item);
        }
        else
        {
            _pending.Enqueue(item);
        }
    }

    private void HandleRegisterConnectionRefs(RegisterConnectionRefs msg)
    {
        // Create a per-connection request queue (buffer 128)
        var (queue, queueSource) =
            Source.Queue<IDataItem>(128, OverflowStrategy.Backpressure)
                .PreMaterialize(_mat!);

        // Wire queue → SinkRef → ConnectionActor's TCP outbound
        queueSource.RunWith(msg.Sink.Sink, _mat!);

        // Wire ConnectionActor's SourceRef → MergeHub (merged response stream)
        msg.Source.Source.RunWith(_mergeHubSink!, _mat!);

        _connectionQueues[msg.Connection] = queue;

        // Drain any pending requests now that a queue is available
        DrainPending();
    }

    private void DrainPending()
    {
        while (_pending.Count > 0)
        {
            var conn = SelectConnectionWithQueue();

            if (conn == null)
            {
                break;
            }

            conn.MarkBusy();
            _ = _connectionQueues[conn.Actor].OfferAsync(_pending.Dequeue());
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
                x.HasAvailableStreamCapacity &&
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
        var actor = Context.ActorOf(Props.Create(() => new ConnectionActor(_options, clientManager)));

        Context.Watch(actor);

        var state = new ConnectionState(actor);
        _connections.Add(state);

        return state;
    }

    private void HandleIdle(ConnectionIdle msg)
    {
        var conn = Find(msg.Connection);
        conn?.MarkIdle();
        DrainPending();
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

    private void HandleStreamComplete(StreamComplete msg)
    {
        var conn = Find(msg.Connection);
        conn?.ReleaseStream();
    }

    private void HandleMarkNoReuse(MarkConnectionNoReuse msg)
    {
        var conn = Find(msg.Connection);
        conn?.MarkNoReuse();
    }

    private ConnectionState? Find(IActorRef actor)
        => _connections.Find(x => x.Actor.Equals(actor));
}