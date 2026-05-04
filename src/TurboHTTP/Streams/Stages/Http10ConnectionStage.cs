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
        private readonly List<ITransportOutbound> _pendingOutbound = [];
        private readonly List<HttpResponseMessage> _pendingResponses = [];
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

                    // Try to flush any EOF-delimited response
                    if (_sm.TryDecodeEof())
                    {
                        FlushOutbound();
                        FlushResponses();
                        return;
                    }

                    // Emit retry for orphaned request
                    _sm.HandleOrphanedRequest();
                    FlushOutbound();

                    CompleteStage();
                },
                onUpstreamFailure: ex =>
                {
                    Log.Warning("Http10ConnectionStage: Server inlet upstream failure: {0}", ex.Message);

                    _sm.HandleOrphanedRequest();
                    FlushOutbound();

                    CompleteStage();
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
            _pendingResponses.Add(response);
        }

        void IStageOperations.OnOutbound(ITransportOutbound item)
        {
            _pendingOutbound.Add(item);
        }

        void IStageOperations.OnWarning(string message)
        {
            Log.Warning("Http10ConnectionStage: {0}", message);
        }

        void IStageOperations.OnReconnectFailed()
        {
            _reconnectFailed = true;
        }

        ILoggingAdapter IStageOperations.Log => Log;

        private void OnServerPush()
        {
            var item = Grab(_stage._inServer);

            if (item is TransportConnected)
            {
                Tracing.For("Protocol").Debug(this, "HTTP/1.0 connected");
                _sm.OnConnectionRestored();
                FlushOutbound();
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

                FlushOutbound();
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
                FlushOutbound();
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

            FlushOutbound();

            if (_pendingResponses.Count > 0)
            {
                FlushResponses();
            }
            else if (!_serverFinished && !HasBeenPulled(_stage._inServer) && !IsClosed(_stage._inServer))
            {
                // No response yet — pull more server data
                Pull(_stage._inServer);
            }

            TryPullRequest();
        }

        private void OnAppPush()
        {
            var request = Grab(_stage._inApp);
            Tracing.For("Protocol").Debug(this, "HTTP/1.0 → {0} {1}", request.Method, request.RequestUri);
            _sm.EncodeRequest(request);
            FlushOutbound();
            TryPullRequest();
        }

        private void OnNetworkPull()
        {
            TryPullRequest();
        }

        private void FlushResponses()
        {
            if (_pendingResponses.Count == 0)
            {
                if (IsClosed(_stage._inApp) && !_sm.HasInFlightRequest)
                {
                    CompleteStage();
                    return;
                }

                if (!HasBeenPulled(_stage._inServer) && !IsClosed(_stage._inServer))
                {
                    Pull(_stage._inServer);
                }

                return;
            }

            var responses = _pendingResponses.ToArray();
            _pendingResponses.Clear();

            if (_serverFinished)
            {
                EmitMultiple(_stage._outResponse, responses, CompleteStage);
            }
            else
            {
                EmitMultiple(_stage._outResponse, responses,
                    () =>
                    {
                        // App upstream finished and no more in-flight request: complete now.
                        // HTTP/1.0 server will close, but we may as well not wait.
                        if (IsClosed(_stage._inApp) && !_sm.HasInFlightRequest)
                        {
                            CompleteStage();
                            return;
                        }

                        if (!HasBeenPulled(_stage._inServer) && !IsClosed(_stage._inServer))
                        {
                            Pull(_stage._inServer);
                        }
                    });
            }
        }

        private void FlushOutbound()
        {
            if (_pendingOutbound.Count == 0)
            {
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

        public override void PostStop()
        {
            foreach (var item in _pendingOutbound)
            {
                switch (item)
                {
                    case TransportData { Buffer: var buffer }:
                        buffer.Dispose();
                        break;
                }
            }

            _pendingOutbound.Clear();

            foreach (var response in _pendingResponses)
            {
                response.Dispose();
            }

            _pendingResponses.Clear();

            _sm.Cleanup();
        }
    }
}