using System;
using System.Buffers;
using System.Collections.Generic;
using Akka.Actor;
using TurboHttp.IO.Stages;

namespace TurboHttp.IO;

public sealed class HostPoolActor : ReceiveActor
{
    public sealed record Incoming(DataItem Item);

    public sealed record ConnectionIdle(IActorRef Connection);

    public sealed record ConnectionFailed(IActorRef Connection);

    public sealed record ConnectionResponse(IActorRef Connection, IMemoryOwner<byte> Memory, int Length);

    public sealed record IdleCheck;

    public sealed record Reconnect(IActorRef Connection);

    private readonly TcpOptions _options;
    private readonly PoolConfig _config;

    private readonly List<ConnectionState> _connections = [];
    private readonly Queue<DataItem> _pending = new();

    private int _roundRobinIndex;

    private readonly IActorRef _streamPublisher;

    public HostPoolActor(
        TcpOptions options,
        PoolConfig config,
        IActorRef streamPublisher)
    {
        _options = options;
        _config = config;
        _streamPublisher = streamPublisher;

        Receive<Incoming>(HandleRequest);
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

    private void HandleRequest(Incoming msg)
    {
        var conn = SelectConnection();

        if (conn != null)
        {
            SendToConnection(conn, msg.Item);
            return;
        }

        if (_connections.Count < _config.MaxConnectionsPerHost)
        {
            conn = SpawnConnection();
            SendToConnection(conn, msg.Item);
            return;
        }

        _pending.Enqueue(msg.Item);
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

    private void SendToConnection(ConnectionState conn, DataItem data)
    {
        conn.MarkBusy();
        conn.Actor.Tell(data);
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
        _streamPublisher.Tell((msg.Memory, msg.Length));
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

            SendToConnection(conn, _pending.Dequeue());
        }
    }

    private ConnectionState? Find(IActorRef actor)
        => _connections.Find(x => x.Actor.Equals(actor));
}
