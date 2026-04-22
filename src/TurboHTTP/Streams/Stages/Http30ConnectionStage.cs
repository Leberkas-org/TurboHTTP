using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHTTP.Internal;
using TurboHTTP.Protocol.Http3;

namespace TurboHTTP.Streams.Stages;

internal sealed class Http30ConnectionStage : GraphStage<ConnectionShape>
{
    private readonly Inlet<IInputItem> _inServer = new("Http30Connection.In.Server");
    private readonly Outlet<HttpResponseMessage> _outResponse = new("Http30Connection.Out.Response");
    private readonly Inlet<HttpRequestMessage> _inApp = new("Http30Connection.In.App");
    private readonly Outlet<IOutputItem> _outNetwork = new("Http30Connection.Out.Network");

    private readonly TurboClientOptions _options;

    public Http30ConnectionStage(TurboClientOptions options)
    {
        _options = options;
    }

    public override ConnectionShape Shape => new(_inServer, _outResponse, _inApp, _outNetwork);

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

    private sealed class Logic : TimerGraphStageLogic, IStageOperations
    {
        private const string IdleCheckTimerKey = "idle-timeout-check";

        /// <summary>
        /// Synthetic stream ID used as the per-stream decoder key for the H3 control stream.
        /// Negative to avoid collision with real QUIC stream IDs (which are non-negative).
        /// </summary>
        private const long ControlStreamDecoderId = -2;

        private readonly Http30ConnectionStage _stage;
        private readonly StateMachine _sm;
        private readonly List<IOutputItem> _pendingOutbound = [];
        private readonly List<HttpResponseMessage> _pendingResponses = [];
        private bool _reconnectFailed;

        public Logic(Http30ConnectionStage stage) : base(stage.Shape)
        {
            _stage = stage;
            _sm = new StateMachine(stage._options, this);

            SetHandler(stage._inServer, onPush: OnServerPush,
                onUpstreamFinish: () =>
                {
                    // Flush any partially assembled response (QUIC FIN)
                    _sm.FlushPendingResponse();
                    FlushResponses();

                    if (_sm.IsReconnecting)
                    {
                        FailStage(new HttpRequestException(
                            "TurboHTTP: HTTP/3 transport closed during reconnect."));
                        return;
                    }

                    Log.Debug("Http30ConnectionStage: Completing stage due to server inlet upstream finish.");
                    CompleteStage();
                },
                onUpstreamFailure: ex =>
                {
                    Log.Warning("Http30ConnectionStage: Server inlet upstream failure: {0}", ex.Message);
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
                    Log.Warning("Http30ConnectionStage: App inlet upstream failure: {0}", ex.Message);
                    FailStage(ex);
                });

            SetHandler(stage._outNetwork, onPull: OnNetworkPull);
        }

        public override void PreStart()
        {
            EmitMultiple<IOutputItem>(_stage._outNetwork, [
                new OpenTypedStreamItem(0x00, -2, Outbound: true),
                new OpenTypedStreamItem(0x02, -3, Outbound: true),
                new OpenTypedStreamItem(0x03, -4, Outbound: false),
                new ProtocolReadyItem(),
            ]);
            ScheduleIdleCheck();
        }

        protected override void OnTimer(object timerKey)
        {
            if (timerKey is not string key || key != IdleCheckTimerKey)
            {
                return;
            }

            var goAway = _sm.CheckIdleTimeout();
            if (goAway is not null)
            {
                // Serialize and emit the GOAWAY frame
                var buf = RoutedNetworkBuffer.Rent(goAway.SerializedSize);
                var span = buf.FullMemory.Span;
                goAway.WriteTo(ref span);
                buf.Length = goAway.SerializedSize;
                buf.StreamTypeValue = (long)StreamType.Control;
                _pendingOutbound.Add(buf);
                FlushOutbound();
                CompleteStage();
                return;
            }

            ScheduleIdleCheck();
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
            Log.Warning("Http30ConnectionStage: {0}", message);
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
                case ConnectedSignalItem:
                case QuicCloseItem:
                    HandleSignalItem(item);
                    return;
                case RoutedNetworkBuffer tagged:
                    HandleTaggedStreamData(tagged);
                    return;
                case NetworkBuffer rawBuffer:
                    Log.Warning(
                        "Http30ConnectionStage: Received untagged NetworkBuffer — dropping to prevent stream ID misrouting.");
                    rawBuffer.Dispose();
                    Pull(_stage._inServer);
                    return;
                default:
                    Pull(_stage._inServer);
                    break;
            }
        }

        private void HandleSignalItem(IInputItem item)
        {
            switch (item)
            {
                // Reconnect: new connection ready — replay buffered requests
                case ConnectedSignalItem:
                {
                    _sm.OnConnectionRestored();
                    FlushOutbound();
                    TryPullRequest();
                    if (!HasBeenPulled(_stage._inServer) && !IsClosed(_stage._inServer))
                    {
                        Pull(_stage._inServer);
                    }

                    return;
                }
                // Request stream FIN — server finished sending the response.
                case QuicCloseItem { Kind: QuicCloseKind.RequestStreamComplete } close:
                {
                    if (close.StreamId >= 0)
                    {
                        _sm.FlushPendingResponse(close.StreamId);
                    }
                    else
                    {
                        _sm.FlushPendingResponse();
                    }

                    FlushResponses();
                    TryPullRequest();
                    return;
                }
                // Reconnect: connection dropped again while already reconnecting
                case QuicCloseItem when _sm.IsReconnecting:
                {
                    _sm.OnReconnectAttemptFailed();
                    if (_reconnectFailed)
                    {
                        FailStage(new HttpRequestException(
                            "TurboHTTP: HTTP/3 reconnect failed after max attempts."));
                        return;
                    }

                    FlushOutbound();
                    if (!HasBeenPulled(_stage._inServer) && !IsClosed(_stage._inServer))
                    {
                        Pull(_stage._inServer);
                    }

                    return;
                }
                // Abrupt close with in-flight requests — reconnect
                case QuicCloseItem when _sm.HasInFlightRequests:
                {
                    _sm.OnConnectionLost();
                    FlushOutbound();
                    if (!HasBeenPulled(_stage._inServer) && !IsClosed(_stage._inServer))
                    {
                        Pull(_stage._inServer);
                    }

                    return;
                }
                // QuicCloseItem with no in-flight — complete normally
                case QuicCloseItem:
                    CompleteStage();
                    return;
            }
        }

        private void HandleTaggedStreamData(RoutedNetworkBuffer tagged)
        {
            StreamType? type = tagged switch
            {
                { StreamTypeValue: (long)StreamType.Control } => StreamType.Control,
                { StreamTypeValue: (long)StreamType.QpackEncoder } => StreamType.QpackEncoder,
                { StreamTypeValue: (long)StreamType.QpackDecoder } => StreamType.QpackDecoder,
                { StreamTypeValue: null } => null,
                _ => throw new ArgumentOutOfRangeException(nameof(tagged), tagged, null)
            };

            switch (type)
            {
                case StreamType.QpackDecoder:
                {
                    _sm.ProcessQpackDecoderBytes(tagged.Memory);
                    tagged.Dispose();
                    Pull(_stage._inServer);
                    return;
                }
                case StreamType.QpackEncoder:
                {
                    _sm.ProcessQpackEncoderBytes(tagged.Memory);
                    tagged.Dispose();
                    Pull(_stage._inServer);
                    return;
                }
                case StreamType.Control:
                    ProcessFrameData(tagged, streamId: ControlStreamDecoderId);
                    return;
                case StreamType.Push:
                    break;
                default:
                {
                    ProcessFrameData(tagged, tagged.StreamId!.Value);
                    return;
                }
            }
        }

        private void ProcessFrameData(NetworkBuffer buffer, long streamId)
        {
            var frames = _sm.DecodeServerData(buffer, streamId);

            var anyProcessed = false;
            for (var i = 0; i < frames.Count; i++)
            {
                var frame = frames[i];
                anyProcessed = true;

                var forwarded = _sm.ProcessFrame(frame);
                if (forwarded is not null)
                {
                    _sm.AssembleResponse(forwarded, streamId);
                }
            }

            if (!anyProcessed)
            {
                TryPullServer();
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
            var preface = _sm.TryBuildControlPreface();
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

                TryPullServer();
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

                TryPullRequest();
                TryPullServer();
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

                    TryPullRequest();
                    TryPullServer();
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
                var outItem = _pendingOutbound[0];
                _pendingOutbound.Clear();
                Push(_stage._outNetwork, outItem);
                return;
            }

            EmitMultiple(_stage._outNetwork, _pendingOutbound.ToArray());
            _pendingOutbound.Clear();
        }

        private void TryPullServer()
        {
            if (!HasBeenPulled(_stage._inServer) && !IsClosed(_stage._inServer))
            {
                Pull(_stage._inServer);
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

        private void ScheduleIdleCheck()
        {
            if (_sm.IsTimeoutDisabled)
            {
                return;
            }

            var remaining = _sm.TimeUntilExpiry();
            var checkInterval = remaining > TimeSpan.Zero ? remaining : TimeSpan.FromSeconds(1);
            ScheduleOnce(IdleCheckTimerKey, checkInterval);
        }

        public override void PostStop()
        {
            _sm.Dispose();
        }
    }
}