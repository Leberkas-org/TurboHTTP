using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using Servus.Akka.Transport;
using TurboHTTP.Diagnostics;
using TurboHTTP.Protocol.Http3;
using static Servus.Core.Servus;

namespace TurboHTTP.Streams.Stages;

internal sealed class Http30ConnectionStage : GraphStage<ConnectionShape>
{
    private readonly Inlet<ITransportInbound> _inServer = new("Http30Connection.In.Server");
    private readonly Outlet<HttpResponseMessage> _outResponse = new("Http30Connection.Out.Response");
    private readonly Inlet<HttpRequestMessage> _inApp = new("Http30Connection.In.App");
    private readonly Outlet<ITransportOutbound> _outNetwork = new("Http30Connection.Out.Network");

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
        private readonly List<ITransportOutbound> _pendingOutbound = [];
        private readonly Queue<ITransportOutbound> _outboundQueue = new();
        private readonly Queue<HttpResponseMessage> _responseQueue = new();
        private bool _transportConnected;
        private bool _reconnectFailed;

        public Logic(Http30ConnectionStage stage) : base(stage.Shape)
        {
            _stage = stage;
            _sm = new StateMachine(stage._options, this);

            SetHandler(stage._inServer, onPush: OnServerPush,
                onUpstreamFinish: () =>
                {
                    _sm.FlushPendingResponse();
                    TryPushResponse();

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
            _pendingOutbound.Add(new OpenStream(-2, StreamDirection.Unidirectional));
            _pendingOutbound.Add(new OpenStream(-3, StreamDirection.Unidirectional));
            _pendingOutbound.Add(new OpenStream(-4, StreamDirection.Unidirectional));

            var preface = _sm.TryBuildControlPreface();
            if (preface is not null)
            {
                _pendingOutbound.Add(preface);
            }

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
                var buf = TransportBuffer.Rent(goAway.SerializedSize);
                var span = buf.FullMemory.Span;
                goAway.WriteTo(ref span);
                buf.Length = goAway.SerializedSize;
                _pendingOutbound.Add(new MultiplexedData(buf, -2));
                FlushOutbound();
                CompleteStage();
                return;
            }

            ScheduleIdleCheck();
        }

        void IStageOperations.OnResponse(HttpResponseMessage response)
        {
            Tracing.For("Protocol").Debug(this, "H3 ← {0}", (int)response.StatusCode);
            _responseQueue.Enqueue(response);
            TryPushResponse();
        }

        void IStageOperations.OnOutbound(ITransportOutbound item)
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
                case TransportConnected:
                case TransportDisconnected:
                case StreamClosed:
                case StreamReadCompleted:
                    HandleSignalItem(item);
                    return;
                case ServerStreamAccepted accepted:
                    _sm.OnServerStreamOpened(accepted.StreamId);
                    Pull(_stage._inServer);
                    return;
                case StreamOpened:
                    Pull(_stage._inServer);
                    return;
                case MultiplexedData multiplexed:
                    HandleTaggedStreamData(multiplexed);
                    return;
                case TransportData rawData:
                    Log.Warning(
                        "Http30ConnectionStage: Received untagged TransportData — dropping to prevent stream ID misrouting.");
                    rawData.Buffer.Dispose();
                    Pull(_stage._inServer);
                    return;
                default:
                    Pull(_stage._inServer);
                    break;
            }
        }

        private void HandleSignalItem(ITransportInbound item)
        {
            switch (item)
            {
                case TransportConnected:
                {
                    Tracing.For("Protocol").Debug(this, "H3 connected");
                    _transportConnected = true;
                    _sm.OnConnectionRestored();
                    FlushOutbound();
                    TryPullRequest();
                    if (!HasBeenPulled(_stage._inServer) && !IsClosed(_stage._inServer))
                    {
                        Pull(_stage._inServer);
                    }

                    return;
                }
                case StreamReadCompleted { StreamId: >= 0 } readCompleted:
                {
                    _sm.FlushPendingResponse(readCompleted.StreamId);
                    TryPushResponse();
                    TryPullRequest();
                    return;
                }
                case StreamReadCompleted:
                {
                    TryPullServer();
                    return;
                }
                case StreamClosed { StreamId: >= 0 } streamClosed:
                {
                    if (streamClosed.Reason == DisconnectReason.Error)
                    {
                        _sm.FailInflightRequest(streamClosed.StreamId,
                            new HttpRequestException("HTTP/3 stream aborted by transport."));
                    }
                    else
                    {
                        _sm.FlushPendingResponse(streamClosed.StreamId);
                    }

                    TryPushResponse();
                    TryPullRequest();
                    return;
                }
                case StreamClosed:
                {
                    _sm.FlushPendingResponse();
                    TryPushResponse();
                    TryPullRequest();
                    return;
                }
                case TransportDisconnected when _sm.IsReconnecting:
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
                case TransportDisconnected when _sm.HasInFlightRequests:
                {
                    Tracing.For("Protocol").Warning(this, "H3 closed, in-flight requests");
                    _sm.OnConnectionLost();
                    FlushOutbound();
                    if (!HasBeenPulled(_stage._inServer) && !IsClosed(_stage._inServer))
                    {
                        Pull(_stage._inServer);
                    }

                    return;
                }
                case TransportDisconnected:
                    CompleteStage();
                    return;
            }
        }

        private void HandleTaggedStreamData(MultiplexedData tagged)
        {
            var (streamId, buffer) = _sm.ResolveStreamId(tagged.StreamId, tagged.Buffer);

            if (buffer is null)
            {
                Pull(_stage._inServer);
                return;
            }

            switch (streamId)
            {
                case -4:
                {
                    _sm.ProcessQpackDecoderBytes(buffer.Memory);
                    buffer.Dispose();
                    Pull(_stage._inServer);
                    return;
                }
                case -3:
                {
                    _sm.ProcessQpackEncoderBytes(buffer.Memory);
                    buffer.Dispose();
                    Pull(_stage._inServer);
                    return;
                }
                case -2:
                    ProcessFrameData(buffer, streamId: ControlStreamDecoderId);
                    return;
                default:
                {
                    ProcessFrameData(buffer, streamId);
                    return;
                }
            }
        }

        private void ProcessFrameData(TransportBuffer buffer, long streamId)
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
            TryPushResponse();
            TryPullRequest();
            TryPullServer();
        }

        private void OnAppPush()
        {
            var request = Grab(_stage._inApp);
            Tracing.For("Protocol").Debug(this, "H3 → {0} {1}", request.Method, request.RequestUri);
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

            if (_outboundQueue.Count > 0)
            {
                Push(_stage._outNetwork, _outboundQueue.Dequeue());
                return;
            }

            TryPullRequest();
        }

        private void TryPushResponse()
        {
            if (_responseQueue.Count > 0 && IsAvailable(_stage._outResponse))
            {
                Push(_stage._outResponse, _responseQueue.Dequeue());
            }
        }

        private void FlushOutbound()
        {
            if (_pendingOutbound.Count == 0)
            {
                return;
            }

            if (!_transportConnected)
            {
                for (var i = _pendingOutbound.Count - 1; i >= 0; i--)
                {
                    if (_pendingOutbound[i] is ConnectTransport)
                    {
                        _outboundQueue.Enqueue(_pendingOutbound[i]);
                        _pendingOutbound.RemoveAt(i);
                        TryPushOutbound();
                        return;
                    }
                }

                return;
            }

            for (var i = 0; i < _pendingOutbound.Count; i++)
            {
                _outboundQueue.Enqueue(_pendingOutbound[i]);
            }

            _pendingOutbound.Clear();
            TryPushOutbound();
        }

        private void TryPushOutbound()
        {
            if (_outboundQueue.Count > 0 && IsAvailable(_stage._outNetwork))
            {
                Push(_stage._outNetwork, _outboundQueue.Dequeue());
            }
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
            while (_outboundQueue.Count > 0)
            {
                if (_outboundQueue.Dequeue() is TransportData { Buffer: var buffer })
                {
                    buffer.Dispose();
                }
            }

            foreach (var item in _pendingOutbound)
            {
                if (item is TransportData { Buffer: var buffer })
                {
                    buffer.Dispose();
                }
            }

            _pendingOutbound.Clear();

            while (_responseQueue.Count > 0)
            {
                _responseQueue.Dequeue().Dispose();
            }

            _sm.Dispose();
        }
    }
}