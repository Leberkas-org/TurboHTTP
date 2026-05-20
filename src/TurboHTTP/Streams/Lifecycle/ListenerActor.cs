using Akka;
using Akka.Actor;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Dsl;
using Servus.Akka.Transport;
using TurboHTTP.Routing;
using TurboHTTP.Server;
using TurboHTTP.Server.Middleware;

namespace TurboHTTP.Streams.Lifecycle;

internal sealed class ListenerActor : ReceiveActor
{
    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly IListenerFactory _factory;
    private readonly ListenerOptions _listenerOptions;
    private readonly TurboServerOptions _serverOptions;
    private readonly TurboRequestDelegate _pipeline;
    private readonly RouteTable _routeTable;
    private readonly IServiceProvider _services;
    private readonly IMaterializer _materializer;
    private readonly string? _connectionLoggingCategory;

    private UniqueKillSwitch? _listenerKillSwitch;
    private int _connectionCounter;
    private readonly HashSet<IActorRef> _activeConnections = [];

    public sealed record StartListening;

    public sealed record StopAccepting;

    public sealed record GracefulStop(TimeSpan Timeout);

    internal sealed record ConnectionStarted(string ConnectionId, IActorRef ConnectionActor);

    internal sealed record IncomingConnection(Flow<ITransportOutbound, ITransportInbound, NotUsed> ConnectionFlow);

    internal sealed record ListeningStarted;

    internal sealed record ListenerStopped;

    internal sealed record ListenerFailed(Exception? Error);

    public ListenerActor(
        IListenerFactory factory,
        ListenerOptions listenerOptions,
        TurboServerOptions serverOptions,
        TurboRequestDelegate pipeline,
        RouteTable routeTable,
        IServiceProvider services,
        IMaterializer materializer,
        string? connectionLoggingCategory = null)
    {
        _factory = factory;
        _listenerOptions = listenerOptions;
        _serverOptions = serverOptions;
        _pipeline = pipeline;
        _routeTable = routeTable;
        _services = services;
        _materializer = materializer;
        _connectionLoggingCategory = connectionLoggingCategory;

        Receive<StartListening>(_ => OnStartListening());
        Receive<IncomingConnection>(OnIncomingConnection);
        Receive<StopAccepting>(_ => OnStopAccepting());
        Receive<GracefulStop>(OnGracefulStop);
        Receive<ConnectionActor.ConnectionCompleted>(OnConnectionCompleted);
        Receive<ListenerStopped>(_ =>
            _log.Info("Listener on {0}:{1} stopped", _listenerOptions.Host, _listenerOptions.Port));
        Receive<ListenerFailed>(OnListenerFailed);
        Receive<Terminated>(OnChildTerminated);
    }

    private void OnStartListening()
    {
        _log.Info("Listener starting on {0}:{1}", _listenerOptions.Host, _listenerOptions.Port);

        var listenerSource = _factory.Bind(_listenerOptions);
        var self = Self;

        var (killSwitch, completionTask) = listenerSource
            .Select(flow => new IncomingConnection(flow))
            .ViaMaterialized(KillSwitches.Single<IncomingConnection>(), Keep.Right)
            .ToMaterialized(
                Sink.ForEach<IncomingConnection>(msg => self.Tell(msg)),
                Keep.Both)
            .Run(_materializer);

        _listenerKillSwitch = killSwitch;

        Sender.Tell(new ListeningStarted());

        completionTask.PipeTo(Self,
            success: () => new ListenerStopped(),
            failure: ex => new ListenerFailed(ex));
    }

    private void OnIncomingConnection(IncomingConnection msg)
    {
        var limit = _serverOptions.MaxConcurrentConnections;
        if (limit > 0 && _activeConnections.Count >= limit)
        {
            _log.Warning("Connection rejected: limit {0} reached ({1} active)",
                limit, _activeConnections.Count);
            RejectConnection(msg.ConnectionFlow);
            return;
        }

        var connectionId = string.Concat("conn-", ++_connectionCounter);
        var connectionInfo = new TurboConnectionInfo(connectionId, null, 0, null, _listenerOptions.Port);

        var engine = ResolveEngineForListener();

        var child = Context.ActorOf(ConnectionActor.Create(connectionId), connectionId);
        Context.Watch(child);
        _activeConnections.Add(child);

        child.Tell(new ConnectionActor.Materialize(
            msg.ConnectionFlow,
            engine,
            _pipeline,
            _routeTable,
            connectionInfo,
            _services,
            _materializer,
            _connectionLoggingCategory));

        Context.Parent.Tell(new ConnectionStarted(connectionId, child));
    }

    private void OnStopAccepting()
    {
        _log.Info("Listener stopping accept loop");
        _listenerKillSwitch?.Shutdown();
    }

    private void OnGracefulStop(GracefulStop msg)
    {
        OnStopAccepting();

        foreach (var child in _activeConnections)
        {
            child.Tell(new ConnectionActor.GracefulStop(msg.Timeout));
        }
    }

    private void OnConnectionCompleted(ConnectionActor.ConnectionCompleted msg)
    {
        Context.Parent.Tell(msg);
    }

    private void OnListenerFailed(ListenerFailed msg)
    {
        if (msg.Error is not null)
        {
            _log.Error(msg.Error, "Listener on {0}:{1} failed", _listenerOptions.Host, _listenerOptions.Port);
        }
    }

    private void OnChildTerminated(Terminated msg)
    {
        _activeConnections.Remove(msg.ActorRef);
    }

    private void RejectConnection(Flow<ITransportOutbound, ITransportInbound, NotUsed> connectionFlow)
    {
        var killSwitch = KillSwitches.Shared("reject");

        Source.Empty<ITransportOutbound>()
            .Via(connectionFlow)
            .Via(killSwitch.Flow<ITransportInbound>())
            .RunWith(Sink.Ignore<ITransportInbound>().MapMaterializedValue(_ => NotUsed.Instance), _materializer);

        killSwitch.Shutdown();
    }

    private IServerProtocolEngine ResolveEngineForListener()
    {
        if (_listenerOptions is QuicListenerOptions)
        {
            return ProtocolRouter.ResolveEngine(new Version(3, 0), _serverOptions);
        }

        if (_listenerOptions is TcpListenerOptions { ApplicationProtocols: [var preferred, ..] })
        {
            return ProtocolRouter.ResolveEngine(preferred, _serverOptions);
        }

        return ProtocolRouter.ResolveEngine(new Version(1, 1), _serverOptions);
    }

    public static Props Create(
        IListenerFactory factory,
        ListenerOptions listenerOptions,
        TurboServerOptions serverOptions,
        TurboRequestDelegate pipeline,
        RouteTable routeTable,
        IServiceProvider services,
        IMaterializer materializer,
        string? connectionLoggingCategory = null)
        => Props.Create(() => new ListenerActor(
            factory, listenerOptions, serverOptions,
            pipeline, routeTable, services, materializer,
            connectionLoggingCategory));
}