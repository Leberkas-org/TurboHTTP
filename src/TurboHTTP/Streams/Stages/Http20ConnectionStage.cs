using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHTTP.Diagnostics;
using TurboHTTP.Internal;
using TurboHTTP.Protocol.Http2;

namespace TurboHTTP.Streams.Stages;

public sealed class Http20ConnectionStage : GraphStage<ConnectionShape>
{
    private readonly Inlet<IInputItem> _inServer = new("Http20Connection.In.Server");
    private readonly Outlet<HttpResponseMessage> _outResponse = new("Http20Connection.Out.Response");
    private readonly Inlet<HttpRequestMessage> _inApp = new("Http20Connection.In.App");
    private readonly Outlet<IOutputItem> _outNetwork = new("Http20Connection.Out.Network");

    private readonly Http2ConnectionConfig _config;

    public Http20ConnectionStage(Http2ConnectionConfig? config = null, int maxReconnectAttempts = 3)
    {
        _config = config ?? new Http2ConnectionConfig(MaxReconnectAttempts: maxReconnectAttempts);
    }

    public override ConnectionShape Shape => new(_inServer, _outResponse, _inApp, _outNetwork);

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : GraphStageLogic, IHttp2StageOperations
    {
        private readonly Http20ConnectionStage _stage;
        private readonly StateMachine _sm;
        private readonly List<IOutputItem> _pendingOutbound = [];
        private readonly List<HttpResponseMessage> _pendingResponses = [];
        private bool _reconnectFailed;
        public Logic(Http20ConnectionStage stage) : base(stage.Shape)
        {
            _stage = stage;
            _sm = new StateMachine(stage._config, this);

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

        void IHttp2StageOperations.OnResponse(HttpResponseMessage response)
        {
            _pendingResponses.Add(response);
        }

        void IHttp2StageOperations.OnOutbound(IOutputItem item)
        {
            _pendingOutbound.Add(item);
        }

        void IHttp2StageOperations.OnWarning(string message)
        {
            Log.Warning("Http20ConnectionStage: {0}", message);
        }

        void IHttp2StageOperations.OnReconnectFailed()
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
                    _sm.HandleConnectedSignal();
                    FlushOutbound();
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
                    _sm.HandleReconnectAttempt();
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
                    _sm.BufferOrphanedRequests(lastStreamId: 0);
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
                if (frame is UnknownFrame)
                {
                    continue;
                }

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
                return;
            }

            TryPullRequest();
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