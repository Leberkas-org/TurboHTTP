using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using Servus.Akka.Transport;

namespace TurboHTTP.Streams.Stages;

internal sealed class HttpConnectionStageLogic<TSM> : TimerGraphStageLogic, IStageOperations
    where TSM : IHttpStateMachine
{
    private readonly Inlet<ITransportInbound> _inServer;
    private readonly Outlet<HttpResponseMessage> _outResponse;
    private readonly Inlet<HttpRequestMessage> _inApp;
    private readonly Outlet<ITransportOutbound> _outNetwork;

    private readonly TSM _sm;
    private readonly Queue<ITransportOutbound> _outboundQueue = new();
    private readonly Queue<HttpResponseMessage> _responseQueue = new();

    public HttpConnectionStageLogic(
        GraphStage<ConnectionShape> stage,
        Func<IStageOperations, TSM> smFactory) : base(stage.Shape)
    {
        var shape = stage.Shape;
        _inServer = shape.InServer;
        _outResponse = shape.OutResponse;
        _inApp = shape.InApp;
        _outNetwork = shape.OutNetwork;

        _sm = smFactory(this);

        SetHandler(_inServer, onPush: OnServerPush,
            onUpstreamFinish: () => _sm.OnUpstreamFinished(),
            onUpstreamFailure: ex =>
            {
                _sm.OnUpstreamFinished();
            });

        SetHandler(_outResponse, onPull: () =>
        {
            if (_responseQueue.Count > 0)
            {
                Push(_outResponse, _responseQueue.Dequeue());
                return;
            }

            if (!HasBeenPulled(_inServer) && !IsClosed(_inServer))
            {
                Pull(_inServer);
            }
        });

        SetHandler(_inApp, onPush: () =>
        {
            var request = Grab(_inApp);
            _sm.OnRequest(request);
            TryPullRequest();
        },
        onUpstreamFinish: () =>
        {
            if (!_sm.HasInFlightRequests && !_sm.IsReconnecting)
            {
                CompleteStage();
            }
        },
        onUpstreamFailure: ex =>
        {
            _sm.OnUpstreamFinished();
        });

        SetHandler(_outNetwork, onPull: OnNetworkPull);
    }

    public override void PreStart()
    {
        _sm.PreStart();
    }

    private void OnServerPush()
    {
        var item = Grab(_inServer);
        _sm.DecodeServerData(item);

        if (_responseQueue.Count > 0)
        {
            TryPushResponse();
        }

        if (!HasBeenPulled(_inServer) && !IsClosed(_inServer))
        {
            Pull(_inServer);
        }

        TryPullRequest();
    }

    private void OnNetworkPull()
    {
        if (_outboundQueue.Count > 0)
        {
            Push(_outNetwork, _outboundQueue.Dequeue());
            return;
        }

        TryPullRequest();
    }

    protected override void OnTimer(object timerKey)
    {
        if (timerKey is string name)
        {
            _sm.OnTimerFired(name);
        }
    }

    // --- IStageOperations implementation ---

    void IStageOperations.OnResponse(HttpResponseMessage response)
    {
        _responseQueue.Enqueue(response);
        TryPushResponse();
    }

    void IStageOperations.OnOutbound(ITransportOutbound item)
    {
        _outboundQueue.Enqueue(item);
        TryPushOutbound();
    }

    void IStageOperations.OnWarning(string message)
    {
        Log.Warning(message);
    }

    void IStageOperations.OnReconnectFailed()
    {
        // Temporary — will be removed in Task 10.
        // SMs that have been migrated call OnFail() directly instead.
    }

    void IStageOperations.OnScheduleTimer(string name, TimeSpan duration)
    {
        ScheduleOnce(name, duration);
    }

    void IStageOperations.OnCancelTimer(string name)
    {
        CancelTimer(name);
    }

    void IStageOperations.OnComplete()
    {
        CompleteStage();
    }

    void IStageOperations.OnFail(Exception exception)
    {
        FailStage(exception);
    }

    ILoggingAdapter IStageOperations.Log => Log;

    // --- Mechanical helpers ---

    private void TryPushResponse()
    {
        if (_responseQueue.Count > 0 && IsAvailable(_outResponse))
        {
            Push(_outResponse, _responseQueue.Dequeue());
        }
    }

    private void TryPushOutbound()
    {
        if (_outboundQueue.Count > 0 && IsAvailable(_outNetwork))
        {
            Push(_outNetwork, _outboundQueue.Dequeue());
        }
    }

    private void TryPullRequest()
    {
        if (_sm.CanAcceptRequest
            && !HasBeenPulled(_inApp)
            && !IsClosed(_inApp))
        {
            Pull(_inApp);
        }
    }

    public override void PostStop()
    {
        while (_outboundQueue.Count > 0)
        {
            if (_outboundQueue.Dequeue() is TransportData { Buffer: var buffer })
            {
                buffer.Dispose();
            }
        }

        while (_responseQueue.Count > 0)
        {
            _responseQueue.Dequeue().Dispose();
        }

        _sm.Cleanup();
    }
}
