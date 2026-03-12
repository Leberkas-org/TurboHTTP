using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.Stage;
using Servus.Akka;
using TurboHttp.IO.Stages;

namespace TurboHttp.IO;

public sealed class PoolRouterActor : ReceiveActor
{
    public sealed record RegisterHost(string PoolKey, TcpOptions Options);

    public sealed record SendRequest(string PoolKey, DataItem Data, IActorRef ReplyTo);

    public sealed record Response(string PoolKey, IMemoryOwner<byte> Memory, int Length);

    public sealed record ConnectionIdle(IActorRef Connection);

    public sealed record ConnectionFailed(IActorRef Connection, Exception? Cause);

    private readonly Dictionary<string, IActorRef> _hosts = new();

    public PoolRouterActor()
    {
        Receive<RegisterHost>(msg =>
        {
            if (_hosts.ContainsKey(msg.PoolKey))
            {
                return;
            }

            var host = Context.ResolveChildActor<HostPoolActor>(msg.PoolKey, msg.PoolKey, msg.Options);
            _hosts[msg.PoolKey] = host;
        });

        Receive<SendRequest>(msg =>
        {
            if (!_hosts.TryGetValue(msg.PoolKey, out var host))
            {
                Sender.Tell(new Status.Failure(new InvalidOperationException("Unknown host")));
                return;
            }

            host.Forward(msg);
        });
    }
}

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
        conn.Idle = false;
        conn.PendingRequests++;
        conn.LastActivity = DateTime.UtcNow;

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

        conn.PendingRequests--;

        if (conn.PendingRequests == 0)
            conn.Idle = true;

        conn.LastActivity = DateTime.UtcNow;

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

        conn.Active = false;

        Context.System.Scheduler.ScheduleTellOnceCancelable(
            _config.ReconnectInterval,
            Self,
            new Reconnect(msg.Connection),
            Self);
    }

    private void HandleReconnect(Reconnect msg)
    {
        var conn = Find(msg.Connection);

        if (conn != null)
            return;

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

    private sealed class ConnectionState
    {
        public IActorRef Actor { get; }

        public bool Active = true;
        public bool Idle = true;
        public int PendingRequests;
        public DateTime LastActivity = DateTime.UtcNow;

        public ConnectionState(IActorRef actor)
        {
            Actor = actor;
        }
    }
}

public sealed class ConnectionActor : ReceiveActor
{
    public sealed record GetStreamRefs;

    public sealed record StreamRefsResponse(ISinkRef<IDataItem> Requests, ISourceRef<IDataItem> Responses);

    private readonly TcpOptions _options;
    private readonly IActorRef _clientManager;

    private ChannelWriter<(IMemoryOwner<byte>, int)>? _outbound;
    private ChannelReader<(IMemoryOwner<byte>, int)>? _inbound;

    private readonly CancellationTokenSource _cts = new();

    private readonly ILoggingAdapter _log = Context.GetLogger();

    private IActorRef? _runner;

    private ISourceQueueWithComplete<IDataItem>? _responseQueue;

    public ConnectionActor(TcpOptions options, IActorRef clientManager)
    {
        _options = options;
        _clientManager = clientManager;

        Receive<ClientRunner.ClientConnected>(HandleConnected);
        Receive<ClientRunner.ClientDisconnected>(HandleDisconnected);
        Receive<DataItem>(HandleSend);
        ReceiveAsync<GetStreamRefs>(HandleGetStreamRefs);
        Receive<Terminated>(HandleTerminated);
    }

    protected override void PreStart()
    {
        Connect();
    }

    private void Connect()
    {
        _clientManager.Tell(new ClientManager.CreateTcpRunner(_options, Self));
    }

    private void HandleConnected(ClientRunner.ClientConnected msg)
    {
        _log.Debug("Connected {0}", msg.RemoteEndPoint);

        _inbound = msg.InboundReader;
        _outbound = msg.OutboundWriter;
        _runner = Sender;

        Context.Watch(_runner);

        _ = PumpInbound(_cts.Token);
    }

    private async Task PumpInbound(CancellationToken token)
    {
        try
        {
            await foreach (var item in _inbound!.ReadAllAsync(token))
            {
                if (_responseQueue != null)
                {
                    await _responseQueue.OfferAsync(new DataItem(item.Item1, item.Item2));
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Inbound pump failed");
        }
    }

    private void HandleSend(DataItem data)
    {
        if (_outbound == null)
        {
            data.Memory.Dispose();
            return;
        }

        if (!_outbound.TryWrite((data.Memory, data.Length)))
        {
            data.Memory.Dispose();
        }
    }

    private void HandleDisconnected(ClientRunner.ClientDisconnected msg)
    {
        _log.Warning("Disconnected {0}", msg.RemoteEndPoint);
        Reconnect();
    }

    private void HandleTerminated(Terminated msg)
    {
        if (msg.ActorRef.Equals(_runner))
        {
            _log.Warning("ClientRunner terminated");
            Reconnect();
        }
    }

    private void Reconnect()
    {
        _runner = null;
        _outbound = null;
        _inbound = null;

        Connect();
    }

    private async Task HandleGetStreamRefs(GetStreamRefs _)
    {
        var mat = Context.System.Materializer();

        // ---------- RESPONSE STREAM ----------
        var responseMat =
            Source.Queue<IDataItem>(1024, OverflowStrategy.Backpressure)
                .PreMaterialize(mat);

        _responseQueue = responseMat.Item1;

        var sourceRef = await responseMat.Item2
            .RunWith(StreamRefs.SourceRef<IDataItem>(), mat);


        // ---------- REQUEST STREAM ----------
        var requestSink = Sink.ForEachAsync<IDataItem>(1, async x => await _outbound!.WriteAsync((x.Memory, x.Length)));

        var sinkRef = await requestSink
            .RunWith(StreamRefs.SinkRef<IDataItem>(), mat);


        Sender.Tell(new StreamRefsResponse(sinkRef, sourceRef));
    }

    protected override void PostStop()
    {
        _cts.Cancel();

        try
        {
            _runner?.Tell(new DoClose());
        }
        catch
        {
        }

        _responseQueue?.Complete();
    }
}

public sealed class ConnectionPoolStageTest : GraphStage<FlowShape<RoutedTransportItem, RoutedDataItem>>
{
    private readonly IActorRef _router;

    private readonly Inlet<RoutedTransportItem> _inlet = new("connectionpool.in");
    private readonly Outlet<RoutedDataItem> _outlet = new("connectionpool.out");

    public override FlowShape<RoutedTransportItem, RoutedDataItem> Shape { get; }

    public ConnectionPoolStageTest(IActorRef router)
    {
        _router = router;
        Shape = new FlowShape<RoutedTransportItem, RoutedDataItem>(_inlet, _outlet);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
    {
        return new Logic(this);
    }

    private sealed class Logic : GraphStageLogic
    {
        private readonly ConnectionPoolStageTest _stage;

        private readonly Queue<RoutedDataItem> _responses = new();

        public Logic(ConnectionPoolStageTest stage) : base(stage.Shape)
        {
            _stage = stage;
        }

        public override void PreStart()
        {
            var stageActor = GetStageActor(OnMessage);

            SetHandler(_stage._inlet, onPush: () =>
            {
                var item = Grab(_stage._inlet);

                if (item.Item is DataItem data)
                {
                    _stage._router.Tell(
                        new PoolRouterActor.SendRequest(
                            item.PoolKey,
                            data,
                            stageActor.Ref));
                }

                Pull(_stage._inlet);
            });

            SetHandler(_stage._outlet, onPull: PushIfAvailable);
        }

        private void OnMessage((IActorRef sender, object msg) args)
        {
            if (args.msg is PoolRouterActor.Response resp)
            {
                _responses.Enqueue(
                    new RoutedDataItem(
                        resp.PoolKey,
                        resp.Memory,
                        resp.Length));

                PushIfAvailable();
            }
        }

        private void PushIfAvailable()
        {
            if (_responses.Count > 0 && IsAvailable(_stage._outlet))
            {
                Push(_stage._outlet, _responses.Dequeue());
            }
        }
    }
}