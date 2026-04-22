using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHTTP.Diagnostics;
using TurboHTTP.Internal;
using TurboHTTP.Protocol.Http2;

namespace TurboHTTP.Streams.Stages;

internal sealed class Http20ConnectionStage : GraphStage<ConnectionShape>
{
    private readonly Inlet<IInputItem> _inServer = new("Http20Connection.In.Server");
    private readonly Outlet<HttpResponseMessage> _outResponse = new("Http20Connection.Out.Response");
    private readonly Inlet<HttpRequestMessage> _inApp = new("Http20Connection.In.App");
    private readonly Outlet<IOutputItem> _outNetwork = new("Http20Connection.Out.Network");
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
        private readonly List<IOutputItem> _pendingOutbound = [];
        private readonly List<HttpResponseMessage> _pendingResponses = [];
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
                if (!HasBeenPulled(stage._inServer) && !IsClosed(stage._inServer))
                {
                    Pull(stage._inServer);
                }
            });

            SetHandler(stage._inApp, onPush: OnAppPush,
                onUpstreamFinish: () =>
                {
                    if (_sm is { HasInFlightRequests: false, IsReconnecting: false })
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
            _pendingResponses.Add(response);
        }

        void IStageOperations.OnOutbound(IOutputItem item)
        {
            _pendingOutbound.Add(item);
        }

        void IStageOperations.OnWarning(string message)
        {
            Log.Warning("Http20ConnectionStage: {0}", message);
        }

        void IStageOperations.OnReconnectFailed()
        {
            _reconnectFailed = true;
        }

        private void OnServerPush()
        {
            var item = Grab(_stage._inServer);

            switch (item)
            {
                // Reconnect: new connection ready — replay buffered requests
                case ConnectedSignalItem:
                {
                    _sm.OnConnectionRestored();
                    FlushOutbound();
                    ScheduleKeepAlivePing();
                    TryPullRequest();
                    if (!HasBeenPulled(_stage._inServer) && !IsClosed(_stage._inServer))
                    {
                        Pull(_stage._inServer);
                    }

                    return;
                }
                // Reconnect: connection dropped again while already reconnecting
                case CloseSignalItem when _sm.IsReconnecting:
                {
                    _sm.OnReconnectAttemptFailed();
                    if (_reconnectFailed)
                    {
                        FailStage(new HttpRequestException(
                            "TurboHTTP: HTTP/2 reconnect failed after max attempts."));
                        return;
                    }

                    FlushOutbound();
                    if (!HasBeenPulled(_stage._inServer) && !IsClosed(_stage._inServer))
                    {
                        Pull(_stage._inServer);
                    }

                    return;
                }
                // Reconnect: abrupt close with in-flight requests (no GOAWAY)
                case CloseSignalItem when _sm.HasInFlightRequests:
                {
                    _sm.OnConnectionLost(lastStreamId: 0);
                    FlushOutbound();
                    if (!HasBeenPulled(_stage._inServer) && !IsClosed(_stage._inServer))
                    {
                        Pull(_stage._inServer);
                    }

                    return;
                }
                // CloseSignalItem with no in-flight — complete normally
                case CloseSignalItem:
                    CompleteStage();
                    return;
            }

            if (item is not NetworkBuffer buffer)
            {
                Pull(_stage._inServer);
                return;
            }

            var frames = _sm.DecodeServerData(buffer);

            var anyProcessed = false;
            for (var i = 0; i < frames.Count; i++)
            {
                var frame = frames[i];

                TurboTrace.Protocol.Trace(this,
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

            FlushOutbound();
            FlushResponses();
            ResetKeepAliveTimer();
            TryPullRequest();
        }

        private void OnAppPush()
        {
            var request = Grab(_stage._inApp);
            _sm.EncodeRequest(request);
            FlushOutbound();
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
                    FlushOutbound();
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
                            FlushOutbound();
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

        private void FlushResponses()
        {
            if (_pendingResponses.Count == 0)
            {
                if (IsClosed(_stage._inApp) && !_sm.HasInFlightRequests)
                {
                    CompleteStage();
                    return;
                }

                Pull(_stage._inServer);
                return;
            }

            if (_pendingResponses.Count == 1 && IsAvailable(_stage._outResponse))
            {
                var response = _pendingResponses[0];
                _pendingResponses.Clear();
                Push(_stage._outResponse, response);

                if (IsClosed(_stage._inApp) && !_sm.HasInFlightRequests)
                {
                    CompleteStage();
                    return;
                }

                Pull(_stage._inServer);
                return;
            }

            EmitMultiple(_stage._outResponse, _pendingResponses.ToArray(),
                () =>
                {
                    if (IsClosed(_stage._inApp) && !_sm.HasInFlightRequests)
                    {
                        CompleteStage();
                        return;
                    }

                    Pull(_stage._inServer);
                });
            _pendingResponses.Clear();
        }

        private void FlushOutbound()
        {
            if (_pendingOutbound.Count == 0)
            {
                return;
            }

            if (_pendingOutbound.Count == 1 && IsAvailable(_stage._outNetwork))
            {
                var item = _pendingOutbound[0];
                _pendingOutbound.Clear();
                Push(_stage._outNetwork, item);
                return;
            }

            EmitMultiple(_stage._outNetwork, _pendingOutbound.ToArray());
            _pendingOutbound.Clear();
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