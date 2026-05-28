using System.Diagnostics;
using System.Runtime.CompilerServices;
using Akka.Actor;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using TurboHTTP.Diagnostics;
using TurboHTTP.Protocol;
using TurboHTTP.Server;
using TurboHTTP.Server.Context.Features;
using static Servus.Core.Servus;

namespace TurboHTTP.Streams.Stages.Server;

internal sealed class HttpConnectionServerStageLogic<TSM> : TimerGraphStageLogic, IServerStageOperations
    where TSM : IServerStateMachine
{
    private readonly Inlet<ITransportInbound> _inNetwork;
    private readonly Outlet<IFeatureCollection> _outRequest;
    private readonly Inlet<IFeatureCollection> _inResponse;
    private readonly Outlet<ITransportOutbound> _outNetwork;

    private readonly TSM _sm;
    private readonly Queue<IFeatureCollection> _requestQueue = new();
    private readonly Queue<ITransportOutbound> _outboundQueue = new();
    private IActorRef _stageActor = ActorRefs.Nobody;
    private readonly IServiceProvider? _services;
    private TurboHttpConnectionFeature? _connectionFeature;
    private TlsHandshakeFeature? _tlsHandshakeFeature;
    private readonly bool _metricsEnabled;

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
        _metricsEnabled = Metrics.ServerActiveRequests().Enabled
            || Metrics.ServerRequestDuration().Enabled
            || Tracing.IsServerTracingActive();

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
                    if (_metricsEnabled)
                    {
                        OnResponseInstrumented(response);
                    }
                    CompleteStage();
                    return;
                }

                if (_metricsEnabled)
                {
                    OnResponseInstrumented(response);
                }

                var bodyFeature = response.Get<IHttpResponseBodyFeature>();
                var hasBody = bodyFeature is not null;
                if (!hasBody)
                {
                    FeatureCollectionFactory.Return(response);
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
                var connectionFeature = new TurboHttpConnectionFeature
                {
                    ConnectionId = Guid.NewGuid().ToString("N"),
                    RemoteIpAddress = remoteEp.Address,
                    RemotePort = remoteEp.Port,
                    LocalIpAddress = (info.Local as System.Net.IPEndPoint)?.Address,
                    LocalPort = (info.Local as System.Net.IPEndPoint)?.Port ?? 0,
                };

                if (info.Security is { } security)
                {
                    _tlsHandshakeFeature = new TlsHandshakeFeature
                    {
                        Protocol = security.Protocol,
                        NegotiatedCipherSuite = security.NegotiatedCipherSuite,
                        HostName = security.HostName,
                        NegotiatedApplicationProtocol = security.ApplicationProtocol,
                    };
                }

                _connectionFeature = connectionFeature;
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

    void IServerStageOperations.OnRequest(IFeatureCollection features)
    {
        if (_requestQueue.Count >= _sm.MaxQueuedRequests)
        {
            Log.Warning("Request queue exceeded {0}, closing connection", _sm.MaxQueuedRequests);
            CompleteStage();
            return;
        }

        if (_metricsEnabled)
        {
            OnRequestInstrumented(features);
        }

        _requestQueue.Enqueue(features);
        TryPushRequest();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void OnRequestInstrumented(IFeatureCollection features)
    {
        var requestFeature = features.Get<IHttpRequestFeature>();
        if (requestFeature is null)
        {
            return;
        }

        var method = requestFeature.Method;
        var path = requestFeature.Path;
        var scheme = requestFeature.Scheme ?? "http";

        if (Metrics.ServerActiveRequests().Enabled)
        {
            Metrics.ServerActiveRequests().Add(1,
                new KeyValuePair<string, object?>("url.scheme", scheme),
                new KeyValuePair<string, object?>("http.request.method",
                    TurboHttpInstrumentationExtensions.NormalizeMethod(method)));
        }

        if (features is TurboFeatureCollection turbo)
        {
            turbo.RequestTimestamp = Stopwatch.GetTimestamp();
            turbo.RequestActivity = Tracing.StartRequestActivity(method, path, scheme);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void OnResponseInstrumented(IFeatureCollection features)
    {
        var responseFeature = features.Get<IHttpResponseFeature>();
        var requestFeature = features.Get<IHttpRequestFeature>();
        var statusCode = responseFeature?.StatusCode ?? 0;

        if (requestFeature is not null && Metrics.ServerActiveRequests().Enabled)
        {
            var scheme = requestFeature.Scheme ?? "http";
            Metrics.ServerActiveRequests().Add(-1,
                new KeyValuePair<string, object?>("url.scheme", scheme),
                new KeyValuePair<string, object?>("http.request.method",
                    TurboHttpInstrumentationExtensions.NormalizeMethod(requestFeature.Method)));
        }

        if (features is TurboFeatureCollection turbo)
        {
            if (turbo.RequestActivity is { } activity)
            {
                Tracing.SetServerResponse(activity, statusCode);
                activity.Stop();
            }

            if (turbo.RequestTimestamp > 0 && Metrics.ServerRequestDuration().Enabled && requestFeature is not null)
            {
                var elapsed = Stopwatch.GetElapsedTime(turbo.RequestTimestamp);
                Metrics.ServerRequestDuration().Record(elapsed.TotalSeconds,
                    new KeyValuePair<string, object?>("http.request.method",
                        TurboHttpInstrumentationExtensions.NormalizeMethod(requestFeature.Method)),
                    new KeyValuePair<string, object?>("http.response.status_code", statusCode),
                    new KeyValuePair<string, object?>("url.scheme", requestFeature.Scheme ?? "http"));
            }
        }
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

    TurboHttpConnectionFeature? IServerStageOperations.ConnectionFeature => _connectionFeature;

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