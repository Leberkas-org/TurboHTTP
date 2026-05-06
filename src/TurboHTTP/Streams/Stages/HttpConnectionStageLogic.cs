using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using Servus.Akka.Transport;
using TurboHTTP.Diagnostics;
using TurboHTTP.Protocol;
using static Servus.Core.Servus;

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
                Tracing.For("Stage").Warning(this, "OnRequest threw: {0}", ex.Message);
                RequestFault.Fail(request, ex);
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
        Tracing.For("Protocol").Debug(this, "← {0}", (int)response.StatusCode);
        _responseQueue.Enqueue(response);
        TryPushResponse();
    }

    void IStageOperations.OnOutbound(ITransportOutbound item)
    {
        _outboundQueue.Enqueue(item);
        TryPushOutbound();
    }

    void IStageOperations.OnScheduleTimer(string name, TimeSpan duration)
    {
        ScheduleOnce(name, duration);
    }

    void IStageOperations.OnCancelTimer(string name)
    {
        CancelTimer(name);
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
