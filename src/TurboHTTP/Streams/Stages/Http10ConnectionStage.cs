using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using Servus.Akka.Transport;
using TurboHTTP.Diagnostics;
using TurboHTTP.Protocol.Http10;
using static Servus.Core.Servus;

namespace TurboHTTP.Streams.Stages;

internal sealed class Http10ConnectionStage : GraphStage<ConnectionShape>
{
    private readonly Inlet<ITransportInbound> _inServer = new("Http10Connection.In.Server");
    private readonly Outlet<HttpResponseMessage> _outResponse = new("Http10Connection.Out.Response");
    private readonly Inlet<HttpRequestMessage> _inApp = new("Http10Connection.In.App");
    private readonly Outlet<ITransportOutbound> _outNetwork = new("Http10Connection.Out.Network");
    private readonly TurboClientOptions _options;

    public Http10ConnectionStage(TurboClientOptions options)
    {
        _options = options;
    }

    public override ConnectionShape Shape => new(_inServer, _outResponse, _inApp, _outNetwork);

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this, inheritedAttributes);

    private sealed class Logic : GraphStageLogic, IStageOperations
    {
        private readonly Http10ConnectionStage _stage;
        private readonly StateMachine _sm;
        private readonly Queue<ITransportOutbound> _outboundQueue = new();
        private readonly Queue<HttpResponseMessage> _responseQueue = new();
        private bool _serverFinished;
        private bool _reconnectFailed;

        public Logic(Http10ConnectionStage stage, Attributes inheritedAttributes) : base(stage.Shape)
        {
            _stage = stage;

            var memoryBuffer = inheritedAttributes.GetAttribute(new TurboAttributes.MemoryBuffer(4 * 1024, 256 * 1024));
            _sm = new StateMachine(this, _stage._options, memoryBuffer.Initial, memoryBuffer.Max);

            SetHandler(stage._inServer, onPush: OnServerPush,
                onUpstreamFinish: () =>
                {
                    if (_sm.IsReconnecting)
                    {
                        Log.Warning(
                            "Http10ConnectionStage: Transport closed during reconnect — discarding {0} buffered request(s).",
                            _sm.PendingRequestCount);
                        CompleteStage();
                        return;
                    }

                    _serverFinished = true;

                    if (_sm.TryDecodeEof())
                    {
                        TryPushResponse();
                        return;
                    }

                    _sm.HandleOrphanedRequest();
                    CompleteStage();
                },
                onUpstreamFailure: ex =>
                {
                    Log.Warning("Http10ConnectionStage: Server inlet upstream failure: {0}", ex.Message);

                    _sm.HandleOrphanedRequest();
                    CompleteStage();
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
                    // App upstream finished — complete immediately if nothing is in-flight
                    if (_sm is { HasInFlightRequest: false, IsReconnecting: false })
                    {
                        CompleteStage();
                    }
                },
                onUpstreamFailure: ex =>
                {
                    Log.Warning("Http10ConnectionStage: App inlet upstream failure: {0}", ex.Message);
                    CompleteStage();
                });

            SetHandler(stage._outNetwork, onPull: OnNetworkPull);
        }

        void IStageOperations.OnResponse(HttpResponseMessage response)
        {
            Tracing.For("Protocol").Debug(this, "HTTP/1.0 ← {0}", (int)response.StatusCode);
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
            Log.Warning("Http10ConnectionStage: {0}", message);
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

            if (item is TransportConnected)
            {
                Tracing.For("Protocol").Debug(this, "HTTP/1.0 connected");
                _sm.OnConnectionRestored();
                TryPullRequest();
                if (!HasBeenPulled(_stage._inServer) && !IsClosed(_stage._inServer))
                {
                    Pull(_stage._inServer);
                }

                return;
            }

            if (item is TransportDisconnected && _sm.IsReconnecting)
            {
                _sm.OnReconnectAttemptFailed();
                if (_reconnectFailed)
                {
                    Log.Warning(
                        "Http10ConnectionStage: Reconnect failed after max attempts — discarding {0} in-flight request(s).",
                        _sm.PendingRequestCount);
                    CompleteStage();
                    return;
                }

                if (!HasBeenPulled(_stage._inServer) && !IsClosed(_stage._inServer))
                {
                    Pull(_stage._inServer);
                }

                return;
            }

            if (item is TransportDisconnected && _sm.HasInFlightRequest)
            {
                Tracing.For("Protocol").Warning(this, "HTTP/1.0 closed, {0} pending", _sm.PendingRequestCount);
                _sm.StartReconnect();
                if (!HasBeenPulled(_stage._inServer) && !IsClosed(_stage._inServer))
                {
                    Pull(_stage._inServer);
                }

                return;
            }

            if (item is TransportDisconnected)
            {
                CompleteStage();
                return;
            }

            try
            {
                _sm.DecodeServerData(item);
            }
            catch (HttpRequestException ex)
            {
                Log.Warning("Http10ConnectionStage: {0}", ex.Message);
                CompleteStage();
                return;
            }

            if (_responseQueue.Count > 0)
            {
                TryPushResponse();
            }
            else if (!_serverFinished && !HasBeenPulled(_stage._inServer) && !IsClosed(_stage._inServer))
            {
                Pull(_stage._inServer);
            }

            TryPullRequest();
        }

        private void OnAppPush()
        {
            var request = Grab(_stage._inApp);
            Tracing.For("Protocol").Debug(this, "HTTP/1.0 → {0} {1}", request.Method, request.RequestUri);
            _sm.EncodeRequest(request);
            TryPullRequest();
        }

        private void OnNetworkPull()
        {
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
}