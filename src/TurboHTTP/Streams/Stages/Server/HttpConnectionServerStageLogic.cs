using Akka.Actor;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using TurboHTTP.Context.Features;
using TurboHTTP.Protocol;
using TurboHTTP.Server;
using static Servus.Core.Servus;

namespace TurboHTTP.Streams.Stages.Server;

internal sealed class HttpConnectionServerStageLogic<TSM> : TimerGraphStageLogic, IServerStageOperations
    where TSM : IServerStateMachine
{
    private readonly Inlet<ITransportInbound> _inNetwork;
    private readonly Outlet<TurboHttpContext> _outRequest;
    private readonly Inlet<TurboHttpContext> _inResponse;
    private readonly Outlet<ITransportOutbound> _outNetwork;

    private readonly TSM _sm;
    private readonly Queue<TurboHttpContext> _requestQueue = new();
    private readonly Queue<ITransportOutbound> _outboundQueue = new();
    private IActorRef _stageActor = ActorRefs.Nobody;
    private readonly IServiceProvider? _services;
    private TurboConnectionInfo? _connectionInfo;
    private TlsHandshakeFeature? _tlsHandshakeFeature;

    public HttpConnectionServerStageLogic(
        GraphStage<ServerConnectionShape> stage,
        Func<IServerStageOperations, TSM> smFactory,
        IServiceProvider? services = null) : base(stage.Shape)
    {
        var shape = stage.Shape;
        _inNetwork = shape.InNetwork;
        _outRequest = shape.OutRequest;
        _inResponse = shape.InResponse;
        _outNetwork = shape.OutNetwork;
        _services = services;

        _sm = smFactory(this);

        SetHandler(_inNetwork,
            onPush: OnNetworkPush,
            onUpstreamFinish: () =>
            {
                Tracing.For("Stage").Debug(this, "network upstream finished");
                _sm.OnDownstreamFinished();
                CompleteStage();
            },
            onUpstreamFailure: ex =>
            {
                Tracing.For("Stage").Info(this, "network upstream failure: {0}", ex.Message);
                _sm.OnDownstreamFinished();
                CompleteStage();
            });

        SetHandler(_outRequest, onPull: () =>
        {
            if (_requestQueue.Count > 0)
            {
                Push(_outRequest, _requestQueue.Dequeue());
                return;
            }

            if (!HasBeenPulled(_inNetwork) && !IsClosed(_inNetwork))
            {
                Pull(_inNetwork);
            }
        });

        SetHandler(_inResponse,
            onPush: () =>
            {
                var response = Grab(_inResponse);
                try
                {
                    _sm.OnResponse(response);
                }
                catch (Exception ex)
                {
                    Tracing.For("Stage").Error(this, "OnResponse threw: {0}", ex.Message);
                }

                if (_sm.ShouldComplete)
                {
                    CompleteStage();
                    return;
                }

                var bodyFeature = response.TurboResponse.HttpContext.Features.Get<IHttpResponseBodyFeature>();
                var hasBody = bodyFeature is not null;
                if (!hasBody)
                {
                    ServerContextFactory.Return(response);
                }

                TryPullResponse();
            },
            onUpstreamFinish: () =>
            {
                Tracing.For("Stage").Debug(this, "response upstream finished");
                CompleteStage();
            },
            onUpstreamFailure: _ =>
            {
                _sm.OnDownstreamFinished();
                CompleteStage();
            });

        SetHandler(_outNetwork, onPull: OnNetworkPull);
    }

    public override void PreStart()
    {
        _stageActor = GetStageActor(OnStageActorMessage).Ref;
        _sm.PreStart();
        Pull(_inNetwork);
    }

    private void OnStageActorMessage((IActorRef sender, object message) args) => _sm.OnBodyMessage(args.message);

    private void OnNetworkPush()
    {
        var item = Grab(_inNetwork);

        if (item is TransportConnected connected)
        {
            var info = connected.Info;
            if (info.Remote is System.Net.IPEndPoint remoteEp)
            {
                _connectionInfo = new TurboConnectionInfo(
                    Guid.NewGuid().ToString("N"),
                    remoteEp.Address, remoteEp.Port,
                    (info.Local as System.Net.IPEndPoint)?.Address,
                    (info.Local as System.Net.IPEndPoint)?.Port ?? 0);

                if (info.Security is { } security)
                {
                    _connectionInfo.SetSecurityInfo(security);
                    _connectionInfo.SetNegotiatedProtocol(security.ApplicationProtocol);

                    _tlsHandshakeFeature = new TlsHandshakeFeature
                    {
                        Protocol = security.Protocol,
                        NegotiatedCipherSuite = security.NegotiatedCipherSuite,
                        HostName = security.HostName,
                        NegotiatedApplicationProtocol = security.ApplicationProtocol,
                    };

                    if (security.SslStream is not null)
                    {
                        _connectionInfo.SetClientCertificateFromHandshake(security.SslStream);
                        _connectionInfo.SetTlsState(security.SslStream, security.AllowDelayedNegotiation);
                    }
                }
            }
        }

        try
        {
            _sm.DecodeClientData(item);
        }
        catch (Exception ex)
        {
            Tracing.For("Stage").Warning(this, "DecodeClientData threw: {0}", ex.Message);
        }

        if (_requestQueue.Count > 0)
        {
            TryPushRequest();
        }

        if (!HasBeenPulled(_inNetwork) && !IsClosed(_inNetwork))
        {
            Pull(_inNetwork);
        }

        TryPullResponse();
    }

    private void OnNetworkPull()
    {
        if (_outboundQueue.Count > 0)
        {
            Push(_outNetwork, _outboundQueue.Dequeue());
            return;
        }

        TryPullResponse();
    }

    protected override void OnTimer(object timerKey)
    {
        if (timerKey is string name)
        {
            _sm.OnTimerFired(name);
        }
    }

    void IServerStageOperations.OnRequest(TurboHttpContext context)
    {
        if (_requestQueue.Count >= _sm.MaxQueuedRequests)
        {
            Log.Warning("Request queue exceeded {0}, closing connection", _sm.MaxQueuedRequests);
            CompleteStage();
            return;
        }

        context.Materializer = Materializer;
        _requestQueue.Enqueue(context);
        TryPushRequest();
    }

    void IServerStageOperations.OnOutbound(ITransportOutbound item)
    {
        _outboundQueue.Enqueue(item);
        TryPushOutbound();
    }

    void IServerStageOperations.OnScheduleTimer(string name, TimeSpan delay)
        => ScheduleOnce(name, delay);

    void IServerStageOperations.OnCancelTimer(string name)
        => CancelTimer(name);

    ILoggingAdapter IServerStageOperations.Log => Log;

    IActorRef IServerStageOperations.StageActor => _stageActor;

    Akka.Streams.IMaterializer IServerStageOperations.Materializer => Materializer;

    IServiceProvider? IServerStageOperations.Services => _services;

    TurboConnectionInfo? IServerStageOperations.ConnectionInfo => _connectionInfo;

    TlsHandshakeFeature? IServerStageOperations.TlsHandshakeFeature => _tlsHandshakeFeature;

    private void TryPushRequest()
    {
        if (_requestQueue.Count > 0 && IsAvailable(_outRequest))
        {
            Push(_outRequest, _requestQueue.Dequeue());
        }
    }

    private void TryPushOutbound()
    {
        if (_outboundQueue.Count > 0 && IsAvailable(_outNetwork))
        {
            Push(_outNetwork, _outboundQueue.Dequeue());
        }
    }

    private void TryPullResponse()
    {
        if (_sm.CanAcceptResponse
            && !HasBeenPulled(_inResponse)
            && !IsClosed(_inResponse))
        {
            Pull(_inResponse);
        }
    }

    public override void PostStop()
    {
        Tracing.For("Stage").Debug(this, "PostStop: draining {0} outbound, {1} requests",
            _outboundQueue.Count, _requestQueue.Count);

        while (_outboundQueue.Count > 0)
        {
            if (_outboundQueue.Dequeue() is TransportData { Buffer: var buffer })
            {
                buffer.Dispose();
            }
        }

        while (_requestQueue.Count > 0)
        {
            _requestQueue.Dequeue();
        }

        _sm.Cleanup();
    }
}