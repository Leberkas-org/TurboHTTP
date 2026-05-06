using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using Servus.Akka.Transport;
using TurboHTTP.Diagnostics;
using TurboHTTP.Protocol.Http2;
using static Servus.Core.Servus;

namespace TurboHTTP.Streams.Stages;

internal sealed class Http20ConnectionStage : GraphStage<ConnectionShape>
{
    private readonly Inlet<ITransportInbound> _inServer = new("Http20Connection.In.Server");
    private readonly Outlet<HttpResponseMessage> _outResponse = new("Http20Connection.Out.Response");
    private readonly Inlet<HttpRequestMessage> _inApp = new("Http20Connection.In.App");
    private readonly Outlet<ITransportOutbound> _outNetwork = new("Http20Connection.Out.Network");
    private readonly TurboClientOptions _options;

    public override ConnectionShape Shape => new(_inServer, _outResponse, _inApp, _outNetwork);

    public Http20ConnectionStage(TurboClientOptions options)
    {
        _options = options;
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : TimerGraphStageLogic, IStageOperations
    {
        private const string KeepAlivePingTimerKey = "keep-alive-ping";
        private const string KeepAlivePingTimeoutKey = "keep-alive-ping-timeout";

        private readonly Http20ConnectionStage _stage;
        private readonly StateMachine _sm;
        private readonly Queue<ITransportOutbound> _outboundQueue = new();
        private readonly Queue<HttpResponseMessage> _responseQueue = new();
        private bool _reconnectFailed;
        private readonly bool _keepAliveEnabled;

        public Logic(Http20ConnectionStage stage) : base(stage.Shape)
        {
            _stage = stage;
            _sm = new StateMachine(stage._options, this);
            _keepAliveEnabled = stage._options.Http2.KeepAlivePingDelay != Timeout.InfiniteTimeSpan;

            SetHandler(stage._inServer, onPush: OnServerPush,
                onUpstreamFinish: () =>
                {
                    if (_sm.IsReconnecting)
                    {
                        FailStage(new HttpRequestException(
                            "TurboHTTP: HTTP/2 transport closed during reconnect."));
                        return;
                    }

                    Log.Debug("Http20ConnectionStage: Completing stage due to server inlet upstream finish.");
                    CompleteStage();
                },
                onUpstreamFailure: ex =>
                {
                    Log.Warning("Http20ConnectionStage: Server inlet upstream failure: {0}", ex.Message);
                    FailStage(ex);
                });

            SetHandler(stage._outResponse, onPull: () =>
            {
                if (_responseQueue.Count > 0)
                {
                    Push(stage._outResponse, _responseQueue.Dequeue());
                    return;
                }

                if (!HasBeenPulled(stage._inServer) && !IsClosed(stage._inServer))
                {
                    Pull(stage._inServer);
                }
            });

            SetHandler(stage._inApp, onPush: OnAppPush,
                onUpstreamFinish: () =>
                {
                    if (_sm is { HasInFlightRequests: false, IsReconnecting: false }
                        && _outboundQueue.Count == 0)
                    {
                        CompleteStage();
                    }
                },
                onUpstreamFailure: ex =>
                {
                    Log.Warning("Http20ConnectionStage: App inlet upstream failure: {0}", ex.Message);
                    FailStage(ex);
                });

            SetHandler(stage._outNetwork, onPull: OnNetworkPull);
        }

        void IStageOperations.OnResponse(HttpResponseMessage response)
        {
            Tracing.For("Protocol").Debug(this, "H2 ← {0}", (int)response.StatusCode);
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
            Log.Warning("Http20ConnectionStage: {0}", message);
        }

        void IStageOperations.OnReconnectFailed()
        {
            _reconnectFailed = true;
        }

        void IStageOperations.OnScheduleTimer(string name, TimeSpan duration) { }
        void IStageOperations.OnCancelTimer(string name) { }
        void IStageOperations.OnComplete() => CompleteStage();
        void IStageOperations.OnFail(Exception exception) => FailStage(exception);

        ILoggingAdapter IStageOperations.Log => Log;

        private void OnServerPush()
        {
            var item = Grab(_stage._inServer);

            switch (item)
            {
                // Reconnect: new connection ready — replay buffered requests
                case TransportConnected:
                {
                    Tracing.For("Protocol").Debug(this, "H2 connected");
                    _sm.OnConnectionRestored();
                    ScheduleKeepAlivePing();
                    TryPullRequest();
                    if (!HasBeenPulled(_stage._inServer) && !IsClosed(_stage._inServer))
                    {
                        Pull(_stage._inServer);
                    }

                    return;
                }
                // Reconnect: connection dropped again while already reconnecting
                case TransportDisconnected when _sm.IsReconnecting:
                {
                    _sm.OnReconnectAttemptFailed();
                    if (_reconnectFailed)
                    {
                        FailStage(new HttpRequestException(
                            "TurboHTTP: HTTP/2 reconnect failed after max attempts."));
                        return;
                    }

                    if (!HasBeenPulled(_stage._inServer) && !IsClosed(_stage._inServer))
                    {
                        Pull(_stage._inServer);
                    }

                    return;
                }
                // Reconnect: abrupt close with in-flight requests (no GOAWAY)
                case TransportDisconnected when _sm.HasInFlightRequests:
                {
                    Tracing.For("Protocol").Warning(this, "H2 closed, in-flight requests");
                    _sm.OnConnectionLost(lastStreamId: 0);
                    if (!HasBeenPulled(_stage._inServer) && !IsClosed(_stage._inServer))
                    {
                        Pull(_stage._inServer);
                    }

                    return;
                }
                // TransportDisconnected with no in-flight — complete normally
                case TransportDisconnected:
                    CompleteStage();
                    return;
            }

            if (item is not TransportData { Buffer: var buffer })
            {
                Pull(_stage._inServer);
                return;
            }

            var frames = _sm.DecodeServerData(buffer);

            var anyProcessed = false;
            for (var i = 0; i < frames.Count; i++)
            {
                var frame = frames[i];

                Tracing.For("Protocol").Trace(this,
                    $"Frame received: {frame.Type} stream={frame.StreamId} length={frame.SerializedSize}");

                anyProcessed = true;
                var ok = _sm.ProcessFrame(frame);

                if (!ok)
                {
                    break;
                }
            }

            if (!anyProcessed)
            {
                Pull(_stage._inServer);
                return;
            }

            TryPushResponse();
            ResetKeepAliveTimer();
            TryPullRequest();

            if (!HasBeenPulled(_stage._inServer) && !IsClosed(_stage._inServer))
            {
                Pull(_stage._inServer);
            }
        }

        private void OnAppPush()
        {
            var request = Grab(_stage._inApp);
            Tracing.For("Protocol").Debug(this, "H2 → {0} {1}", request.Method, request.RequestUri);
            _sm.EncodeRequest(request);
            TryPullRequest();
        }

        private void OnNetworkPull()
        {
            var preface = _sm.TryBuildPreface();
            if (preface is not null)
            {
                Push(_stage._outNetwork, preface);
                ScheduleKeepAlivePing();
                return;
            }

            if (_outboundQueue.Count > 0)
            {
                Push(_stage._outNetwork, _outboundQueue.Dequeue());
                return;
            }

            if (CanComplete)
            {
                CompleteStage();
                return;
            }

            TryPullRequest();
        }

        protected override void OnTimer(object timerKey)
        {
            switch (timerKey)
            {
                case KeepAlivePingTimerKey:
                {
                    var policy = _stage._options.Http2.KeepAlivePingPolicy;
                    if (policy == HttpKeepAlivePingPolicy.WithActiveRequests && !_sm.HasInFlightRequests)
                    {
                        return;
                    }

                    _sm.SendKeepAlivePing();
                    ScheduleKeepAlivePingTimeout();
                    break;
                }
                case KeepAlivePingTimeoutKey:
                {
                    if (_sm.IsKeepAliveTimedOut(_stage._options.Http2.KeepAlivePingTimeout))
                    {
                        Log.Warning("Http20ConnectionStage: Keep-alive PING timeout — closing connection.");
                        if (_sm.HasInFlightRequests)
                        {
                            _sm.OnConnectionLost(lastStreamId: 0);
                        }
                        else
                        {
                            CompleteStage();
                        }
                    }

                    break;
                }
            }
        }

        private void ScheduleKeepAlivePing()
        {
            if (_keepAliveEnabled)
            {
                ScheduleOnce(KeepAlivePingTimerKey, _stage._options.Http2.KeepAlivePingDelay);
            }
        }

        private void ScheduleKeepAlivePingTimeout()
        {
            if (_keepAliveEnabled)
            {
                ScheduleOnce(KeepAlivePingTimeoutKey, _stage._options.Http2.KeepAlivePingTimeout);
            }
        }

        private void ResetKeepAliveTimer()
        {
            if (_keepAliveEnabled)
            {
                CancelTimer(KeepAlivePingTimeoutKey);
                ScheduleKeepAlivePing();
            }
        }

        private bool CanComplete =>
            IsClosed(_stage._inApp) && !_sm.HasInFlightRequests
            && _outboundQueue.Count == 0 && _responseQueue.Count == 0;

        private void TryPushResponse()
        {
            if (_responseQueue.Count > 0 && IsAvailable(_stage._outResponse))
            {
                Push(_stage._outResponse, _responseQueue.Dequeue());
            }
        }

        private void TryPushOutbound()
        {
            if (_outboundQueue.Count > 0 && IsAvailable(_stage._outNetwork))
            {
                Push(_stage._outNetwork, _outboundQueue.Dequeue());
            }
        }

        private void TryPullRequest()
        {
            if (_sm.CanAcceptRequest
                && !HasBeenPulled(_stage._inApp)
                && !IsClosed(_stage._inApp))
            {
                Pull(_stage._inApp);
            }
        }
    }
}