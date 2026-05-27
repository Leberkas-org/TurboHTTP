using Akka.Actor;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using Servus.Akka.Transport;
using TurboHTTP.Protocol;
using static Servus.Core.Servus;

namespace TurboHTTP.Streams.Stages.Client;

internal sealed class HttpConnectionStageLogic<TSM> : TimerGraphStageLogic, IClientStageOperations
    where TSM : IClientStateMachine
{
    private readonly Inlet<ITransportInbound> _inServer;
    private readonly Outlet<HttpResponseMessage> _outResponse;
    private readonly Inlet<HttpRequestMessage> _inApp;
    private readonly Outlet<ITransportOutbound> _outNetwork;

    private readonly TSM _sm;
    private readonly Queue<ITransportOutbound> _outboundQueue = new(64);
    private readonly Queue<HttpResponseMessage> _responseQueue = new(64);
    private IActorRef _stageActor = ActorRefs.Nobody;

    public HttpConnectionStageLogic(
        GraphStage<ClientConnectionShape> stage,
        Func<IClientStageOperations, TSM> smFactory) : base(stage.Shape)
    {
        var shape = stage.Shape;
        _inServer = shape.InNetwork;
        _outResponse = shape.OutResponse;
        _inApp = shape.InRequest;
        _outNetwork = shape.OutNetwork;

        _sm = smFactory(this);

        SetHandler(_inServer, onPush: OnServerPush,
            onUpstreamFinish: () =>
            {
                Tracing.For("Stage").Debug(this, "server upstream finished");
                _sm.OnUpstreamFinished();
                CompleteStage();
            },
            onUpstreamFailure: ex =>
            {
                Tracing.For("Stage").Info(this, "server upstream failure: {0}", ex.Message);
                _sm.OnUpstreamFinished();
                CompleteStage();
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
            try
            {
                _sm.OnRequest(request);
            }
            catch (Exception ex)
            {
                Tracing.For("Stage").Error(this, "OnRequest threw: {0}", ex.Message);
                request.Fail(ex);
            }

            TryPullRequest();
        },
        onUpstreamFinish: () =>
        {
            Tracing.For("Stage").Debug(this, "request upstream finished (inFlight={0}, reconnecting={1})", _sm.HasInFlightRequests, _sm.IsReconnecting);
            if (!_sm.HasInFlightRequests && !_sm.IsReconnecting)
            {
                CompleteStage();
            }
        },
        onUpstreamFailure: _ =>
        {
            _sm.OnUpstreamFinished();
        });

        SetHandler(_outNetwork, onPull: OnNetworkPull);
    }

    public override void PreStart()
    {
        _stageActor = GetStageActor(OnStageActorMessage).Ref;
        _sm.PreStart();
    }

    private void OnStageActorMessage((IActorRef sender, object message) args)
    {
        _sm.OnBodyMessage(args.message);
        TryPullRequest();
        TryCompleteAfterAllResponses();
    }

    private void OnServerPush()
    {
        var item = Grab(_inServer);
        try
        {
            _sm.DecodeServerData(item);
        }
        catch (Exception ex)
        {
            Tracing.For("Stage").Warning(this, "DecodeServerData threw: {0}", ex.Message);
        }

        if (_responseQueue.Count > 0)
        {
            TryPushResponse();
        }

        if (!HasBeenPulled(_inServer) && !IsClosed(_inServer))
        {
            Pull(_inServer);
        }

        TryPullRequest();
        TryCompleteAfterAllResponses();
    }

    private void OnNetworkPull()
    {
        if (_outboundQueue.Count > 0)
        {
            Push(_outNetwork, _outboundQueue.Dequeue());
            TryCompleteAfterAllResponses();
            return;
        }

        TryPullRequest();
    }

    protected override void OnTimer(object timerKey)
    {
        if (timerKey is not string name)
        {
            return;
        }

        if (name == DrainCompleteTimerKey)
        {
            if (IsClosed(_inApp)
                && !_sm.HasInFlightRequests
                && !_sm.IsReconnecting
                && _responseQueue.Count == 0
                && _outboundQueue.Count == 0)
            {
                CompleteStage();
            }

            return;
        }

        _sm.OnTimerFired(name);
    }

    // --- IClientStageOperations implementation ---

    void IClientStageOperations.OnResponse(HttpResponseMessage response)
    {
        Tracing.For("Protocol").Debug(this, "← {0}", (int)response.StatusCode);
        if (IsAvailable(_outResponse))
        {
            Push(_outResponse, response);
            return;
        }
        _responseQueue.Enqueue(response);
    }

    void IClientStageOperations.OnOutbound(ITransportOutbound item)
    {
        if (IsAvailable(_outNetwork))
        {
            Push(_outNetwork, item);
            return;
        }
        _outboundQueue.Enqueue(item);
    }

    void IClientStageOperations.OnScheduleTimer(string name, TimeSpan duration)
    {
        ScheduleOnce(name, duration);
    }

    void IClientStageOperations.OnCancelTimer(string name)
    {
        CancelTimer(name);
    }

    ILoggingAdapter IClientStageOperations.Log => Log;

    IActorRef IClientStageOperations.StageActor => _stageActor;

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

    private const string DrainCompleteTimerKey = "drain-complete";

    private void TryCompleteAfterAllResponses()
    {
        if (IsClosed(_inApp)
            && !_sm.HasInFlightRequests
            && !_sm.IsReconnecting
            && _responseQueue.Count == 0
            && _outboundQueue.Count == 0
            && !IsTimerActive(DrainCompleteTimerKey))
        {
            ScheduleOnce(DrainCompleteTimerKey, TimeSpan.FromMilliseconds(100));
        }
    }

    public override void PostStop()
    {
        Tracing.For("Stage").Debug(this, "PostStop: draining {0} outbound, {1} responses", _outboundQueue.Count, _responseQueue.Count);
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
