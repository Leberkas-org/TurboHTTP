using System.Diagnostics;
using System.Runtime.CompilerServices;
using Akka;
using Akka.Actor;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Dsl;
using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using TurboHTTP.Diagnostics;
using TurboHTTP.Server;
using static Servus.Core.Servus;

namespace TurboHTTP.Streams.Lifecycle;

internal sealed class ListenerActor : ReceiveActor
{
    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly IListenerFactory _factory;
    private readonly ListenerOptions _listenerOptions;
    private readonly TurboServerOptions _serverOptions;
    private readonly Flow<IFeatureCollection, IFeatureCollection, NotUsed> _bridgeFlow;
    private readonly IServiceProvider _services;
    private readonly IMaterializer _materializer;
    private readonly string? _connectionLoggingCategory;

    private UniqueKillSwitch? _listenerKillSwitch;
    private int _connectionCounter;
    private readonly HashSet<IActorRef> _activeConnections = [];
    private readonly Dictionary<IActorRef, (long Timestamp, Activity? Activity)> _connectionMetrics = new();
    private bool _draining;

    public sealed record StartListening;

    public sealed record StopAccepting;

    public sealed record GracefulStop(TimeSpan Timeout);

    internal sealed record ConnectionStarted(string ConnectionId, IActorRef ConnectionActor);

    internal sealed record IncomingConnection(Flow<ITransportOutbound, ITransportInbound, NotUsed> ConnectionFlow);

    internal sealed record ListeningStarted;

    private sealed record BindCompleted(IActorRef ReplyTo);

    internal sealed record ListenerStopped;

    internal sealed record ListenerFailed(Exception? Error);

    public ListenerActor(
        IListenerFactory factory,
        ListenerOptions listenerOptions,
        TurboServerOptions serverOptions,
        Flow<IFeatureCollection, IFeatureCollection, NotUsed> bridgeFlow,
        IServiceProvider services,
        IMaterializer materializer,
        string? connectionLoggingCategory = null)
    {
        _factory = factory;
        _listenerOptions = listenerOptions;
        _serverOptions = serverOptions;
        _bridgeFlow = bridgeFlow;
        _services = services;
        _materializer = materializer;
        _connectionLoggingCategory = connectionLoggingCategory;

        Receive<StartListening>(_ => OnStartListening());
        Receive<BindCompleted>(OnBindCompleted);
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
        var sender = Sender;

        var ((boundTask, killSwitch), completionTask) = listenerSource
            .Select(flow => new IncomingConnection(flow))
            .ViaMaterialized(KillSwitches.Single<IncomingConnection>(), Keep.Both)
            .ToMaterialized(
                Sink.ForEach<IncomingConnection>(msg => self.Tell(msg)),
                Keep.Both)
            .Run(_materializer);

        _listenerKillSwitch = killSwitch;

        boundTask.PipeTo(Self,
            success: () => new BindCompleted(sender),
            failure: ex => new ListenerFailed(ex));

        completionTask.PipeTo(Self,
            success: () => new ListenerStopped(),
            failure: ex => new ListenerFailed(ex));
    }

    private void OnBindCompleted(BindCompleted msg)
    {
        msg.ReplyTo.Tell(new ListeningStarted());
    }

    private void OnIncomingConnection(IncomingConnection msg)
    {
        var limit = _serverOptions.Limits.MaxConcurrentConnections;
        if (limit > 0 && _activeConnections.Count >= limit)
        {
            _log.Warning("Connection rejected: limit {0} reached ({1} active)",
                limit, _activeConnections.Count);
            if (Metrics.RejectedConnections().Enabled)
            {
                RecordRejectedConnection();
            }
            RejectConnection(msg.ConnectionFlow);
            return;
        }

        var connectionId = string.Concat("conn-", ++_connectionCounter);
        var engine = ResolveEngineForListener();

        long timestamp = 0;
        Activity? connectionActivity = null;

        if (Metrics.ActiveConnections().Enabled || Tracing.IsServerTracingActive())
        {
            OnIncomingConnectionInstrumented(out timestamp, out connectionActivity);
        }

        var child = Context.ActorOf(ConnectionActor.Create(connectionId), connectionId);
        Context.Watch(child);
        _activeConnections.Add(child);
        _connectionMetrics[child] = (timestamp, connectionActivity);

        child.Tell(new ConnectionActor.Materialize(
            msg.ConnectionFlow,
            engine,
            _bridgeFlow,
            _services,
            _materializer,
            _connectionLoggingCategory,
            timestamp,
            connectionActivity));

        Context.Parent.Tell(new ConnectionStarted(connectionId, child));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void OnIncomingConnectionInstrumented(out long timestamp, out Activity? connectionActivity)
    {
        timestamp = Stopwatch.GetTimestamp();
        var host = _listenerOptions.Host ?? "localhost";
        var port = _listenerOptions.Port;
        var transport = _listenerOptions is QuicListenerOptions ? "udp" : "tcp";

        var tags = new TagList();
        TurboServerInstrumentationExtensions.InjectConnectionTags(ref tags, host, port);
        tags.Add("network.transport", transport);
        Metrics.ActiveConnections().Add(1, in tags);

        connectionActivity = Tracing.StartConnectionActivity(host, port, transport);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void RecordRejectedConnection()
    {
        var host = _listenerOptions.Host ?? "localhost";
        Metrics.RejectedConnections().Add(1,
            new KeyValuePair<string, object?>("server.address", host),
            new KeyValuePair<string, object?>("server.port", _listenerOptions.Port));
    }

    private void OnStopAccepting()
    {
        _log.Info("Listener stopping accept loop");
        _listenerKillSwitch?.Shutdown();
    }

    private void OnGracefulStop(GracefulStop msg)
    {
        OnStopAccepting();

        _draining = true;
        if (Metrics.DrainActive().Enabled)
        {
            Metrics.DrainActive().Add(1);
        }

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

        if (_connectionMetrics.Remove(msg.ActorRef, out var metrics))
        {
            if (Metrics.ActiveConnections().Enabled || Metrics.ConnectionDuration().Enabled || metrics.Activity is not null)
            {
                OnConnectionEndInstrumented(metrics.Timestamp, metrics.Activity);
            }
        }

        if (_draining && _activeConnections.Count == 0)
        {
            if (Metrics.DrainActive().Enabled)
            {
                Metrics.DrainActive().Add(-1);
            }
            _draining = false;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void OnConnectionEndInstrumented(long timestamp, Activity? connectionActivity)
    {
        var host = _listenerOptions.Host ?? "localhost";
        var port = _listenerOptions.Port;
        var transport = _listenerOptions is QuicListenerOptions ? "udp" : "tcp";

        var tags = new TagList();
        TurboServerInstrumentationExtensions.InjectConnectionTags(ref tags, host, port);
        tags.Add("network.transport", transport);

        if (Metrics.ActiveConnections().Enabled)
        {
            Metrics.ActiveConnections().Add(-1, in tags);
        }

        if (Metrics.ConnectionDuration().Enabled && timestamp > 0)
        {
            var elapsed = Stopwatch.GetElapsedTime(timestamp);
            Metrics.ConnectionDuration().Record(elapsed.TotalSeconds, in tags);
        }

        if (connectionActivity is not null)
        {
            Tracing.StopConnectionActivity(connectionActivity, null);
        }
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

        return ProtocolRouter.ResolveNegotiating(_serverOptions);
    }

    public static Props Create(
        IListenerFactory factory,
        ListenerOptions listenerOptions,
        TurboServerOptions serverOptions,
        Flow<IFeatureCollection, IFeatureCollection, NotUsed> bridgeFlow,
        IServiceProvider services,
        IMaterializer materializer,
        string? connectionLoggingCategory = null)
        => Props.Create(() => new ListenerActor(
            factory, listenerOptions, serverOptions,
            bridgeFlow, services, materializer,
            connectionLoggingCategory));
}