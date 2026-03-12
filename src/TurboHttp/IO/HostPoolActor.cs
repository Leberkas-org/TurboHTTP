using System;
using System.Buffers;
using System.Collections.Generic;
using Akka.Actor;
using TurboHttp.IO.Stages;

namespace TurboHttp.IO;

public sealed class HostPoolActor : ReceiveActor
{
    public sealed record ConnectionIdle(IActorRef Connection);

    public sealed record ConnectionFailed(IActorRef Connection);

    public sealed record ConnectionResponse(IActorRef Connection, IMemoryOwner<byte> Memory, int Length);

    public sealed record IdleCheck;

    public sealed record Reconnect(IActorRef Connection);

    private readonly TcpOptions _options;
    private readonly PoolConfig _config;

    private readonly List<ConnectionState> _connections = [];
    private readonly Queue<PendingItem> _pending = new();
    private readonly Dictionary<IActorRef, Queue<PendingReplyTo>> _replyToMap = new();

    private int _roundRobinIndex;

    public HostPoolActor(
        TcpOptions options,
        PoolConfig config)
    {
        _options = options;
        _config = config;

        Receive<PoolRouterActor.SendRequest>(HandleRequest);
        Receive<ConnectionIdle>(HandleIdle);
        Receive<ConnectionResponse>(HandleResponse);
        Receive<ConnectionFailed>(HandleFailure);
        Receive<IdleCheck>(_ => EvictIdleConnections());
        Receive<Reconnect>(HandleReconnect);
    }

    protected override void PreStart()
    {
        Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(
            _config.IdleCheckInterval,
            _config.IdleCheckInterval,
            Self,
            new IdleCheck(),
            Self);
    }

    private void HandleRequest(PoolRouterActor.SendRequest msg)
    {
        var pending = new PendingReplyTo(msg.ReplyTo, msg.PoolKey);
        var conn = SelectConnection();

        if (conn != null)
        {
            SendToConnection(conn, msg.Data, pending);
            return;
        }

        if (_connections.Count < _config.MaxConnectionsPerHost)
        {
            conn = SpawnConnection();
            SendToConnection(conn, msg.Data, pending);
            return;
        }

        _pending.Enqueue(new PendingItem(msg.Data, pending));
    }

    private ConnectionState? SelectConnection()
    {
        foreach (var conn in _connections)
        {
            if (conn is { Active: true, Idle: true })
            {
                return conn;
            }
        }

        return null;
    }

    private void SendToConnection(ConnectionState conn, DataItem data, PendingReplyTo pending)
    {
        conn.MarkBusy();
        conn.Actor.Tell(data);

        if (!_replyToMap.TryGetValue(conn.Actor, out var queue))
        {
            queue = new Queue<PendingReplyTo>();
            _replyToMap[conn.Actor] = queue;
        }

        queue.Enqueue(pending);
    }

    private ConnectionState SpawnConnection()
    {
        var actor = Context.ActorOf(
            Props.Create(() =>
                new ConnectionActor(_options, Self)));

        Context.Watch(actor);

        var state = new ConnectionState(actor);

        _connections.Add(state);

        return state;
    }

    private void HandleIdle(ConnectionIdle msg)
    {
        var conn = Find(msg.Connection);

        if (conn == null)
            return;

        conn.MarkIdle();

        DrainPending();
    }

    private void HandleResponse(ConnectionResponse msg)
    {
        if (!_replyToMap.TryGetValue(msg.Connection, out var queue) || queue.Count == 0)
        {
            return;
        }

        var pending = queue.Dequeue();
        pending.ReplyTo.Tell(new PoolRouterActor.Response(pending.PoolKey, msg.Memory, msg.Length));
    }

    private void HandleFailure(ConnectionFailed msg)
    {
        var conn = Find(msg.Connection);

        if (conn == null)
            return;

        conn.MarkDead();

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
            return;

        _replyToMap.Remove(msg.Connection);
        _connections.Remove(conn);
        SpawnConnection();
    }

    private void EvictIdleConnections()
    {
        var now = DateTime.UtcNow;

        foreach (var conn in _connections.ToArray())
        {
            if (!conn.Idle)
                continue;

            if (now - conn.LastActivity > _config.IdleTimeout &&
                _connections.Count > 1)
            {
                Context.Unwatch(conn.Actor);
                conn.Actor.Tell(PoisonPill.Instance);
                _replyToMap.Remove(conn.Actor);
                _connections.Remove(conn);
            }
        }
    }

    private void DrainPending()
    {
        while (_pending.Count > 0)
        {
            var conn = SelectConnection();

            if (conn == null)
                break;

            var item = _pending.Dequeue();
            SendToConnection(conn, item.Data, item.Pending);
        }
    }

    private ConnectionState? Find(IActorRef actor)
        => _connections.Find(x => x.Actor.Equals(actor));

    private readonly record struct PendingReplyTo(IActorRef ReplyTo, string PoolKey);

    private readonly record struct PendingItem(DataItem Data, PendingReplyTo Pending);
}
