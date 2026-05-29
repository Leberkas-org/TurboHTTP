using Akka.Actor;
using Akka.Event;

namespace TurboHTTP.Streams.Lifecycle;

internal sealed class ServerSupervisorActor : ReceiveActor
{
    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly Dictionary<string, IActorRef> _activeConnections = new();
    private readonly List<IActorRef> _listeners = [];
    private readonly List<int> _boundPorts = [];
    private IActorRef _startRequester = ActorRefs.Nobody;
    private int _pendingListenerCount;

    public sealed record StartListeners(IReadOnlyList<Props> ListenerProps);
    public sealed record ListenersReady(IReadOnlyList<int> BoundPorts);
    public sealed record StopAccepting;
    public sealed record BeginDrain(TimeSpan Timeout);
    public sealed record DrainComplete;
    public sealed record GetConnectionCount;

    public ServerSupervisorActor()
    {
        Receive<StartListeners>(OnStartListeners);
        Receive<ListenerActor.ListeningStarted>(msg => OnListenerReady(msg.BoundPort));
        Receive<StopAccepting>(_ => OnStopAccepting());
        Receive<BeginDrain>(OnBeginDrain);
        Receive<ListenerActor.ConnectionStarted>(OnConnectionStarted);
        Receive<ConnectionActor.ConnectionCompleted>(OnConnectionCompleted);
        Receive<GetConnectionCount>(_ => Sender.Tell(_activeConnections.Count));
    }

    private void OnStartListeners(StartListeners msg)
    {
        _startRequester = Sender;
        _pendingListenerCount = msg.ListenerProps.Count;

        if (_pendingListenerCount == 0)
        {
            _startRequester.Tell(new ListenersReady([]));
            return;
        }

        for (var i = 0; i < msg.ListenerProps.Count; i++)
        {
            var name = string.Concat("listener-", i);
            var listener = Context.ActorOf(msg.ListenerProps[i], name);
            listener.Tell(new ListenerActor.StartListening());
            _listeners.Add(listener);
        }
    }

    private void OnListenerReady(int boundPort)
    {
        _boundPorts.Add(boundPort);
        _pendingListenerCount--;
        if (_pendingListenerCount <= 0)
        {
            _log.Info("All {0} listener(s) ready", _listeners.Count);
            _startRequester.Tell(new ListenersReady(_boundPorts));
            _startRequester = ActorRefs.Nobody;
        }
    }

    private void OnStopAccepting()
    {
        _log.Info("Supervisor: stop accepting on all listeners");
        foreach (var listener in _listeners)
        {
            listener.Tell(new ListenerActor.StopAccepting());
        }
    }

    private void OnBeginDrain(BeginDrain msg)
    {
        _log.Info("Supervisor: draining {0} connections (timeout: {1})", _activeConnections.Count, msg.Timeout);
        foreach (var listener in _listeners)
        {
            listener.Tell(new ListenerActor.GracefulStop(msg.Timeout));
        }

        if (_activeConnections.Count == 0)
        {
            Sender.Tell(new DrainComplete());
        }
    }

    private void OnConnectionStarted(ListenerActor.ConnectionStarted msg)
    {
        _activeConnections[msg.ConnectionId] = msg.ConnectionActor;
        _log.Debug("Connection {0} started, active={1}", msg.ConnectionId, _activeConnections.Count);
    }

    private void OnConnectionCompleted(ConnectionActor.ConnectionCompleted msg)
    {
        _activeConnections.Remove(msg.ConnectionId);
        _log.Debug("Connection {0} completed ({1}), active={2}", msg.ConnectionId, msg.Reason, _activeConnections.Count);
    }

    protected override SupervisorStrategy SupervisorStrategy()
    {
        return new OneForOneStrategy(
            maxNrOfRetries: 3,
            withinTimeRange: TimeSpan.FromMinutes(1),
            localOnlyDecider: ex => ex switch
            {
                _ => Directive.Restart
            });
    }
}
