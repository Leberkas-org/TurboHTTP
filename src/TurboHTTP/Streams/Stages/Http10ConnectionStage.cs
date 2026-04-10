using System.Collections.Immutable;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHTTP.Internal;
using TurboHTTP.Protocol.Http10;

namespace TurboHTTP.Streams.Stages;

public sealed class Http10ConnectionShape : Shape
{
    public Inlet<IInputItem> InServer { get; }
    public Outlet<HttpResponseMessage> OutResponse { get; }
    public Inlet<HttpRequestMessage> InApp { get; }
    public Outlet<IOutputItem> OutNetwork { get; }

    public Http10ConnectionShape(
        Inlet<IInputItem> inServer,
        Outlet<HttpResponseMessage> outResponse,
        Inlet<HttpRequestMessage> inApp,
        Outlet<IOutputItem> outNetwork)
    {
        InServer = inServer;
        OutResponse = outResponse;
        InApp = inApp;
        OutNetwork = outNetwork;
    }

    public override ImmutableArray<Inlet> Inlets => [InServer, InApp];

    public override ImmutableArray<Outlet> Outlets => [OutResponse, OutNetwork];

    public override Shape DeepCopy()
    {
        return new Http10ConnectionShape(
            (Inlet<IInputItem>)InServer.CarbonCopy(),
            (Outlet<HttpResponseMessage>)OutResponse.CarbonCopy(),
            (Inlet<HttpRequestMessage>)InApp.CarbonCopy(),
            (Outlet<IOutputItem>)OutNetwork.CarbonCopy());
    }

    public override Shape CopyFromPorts(ImmutableArray<Inlet> inlets, ImmutableArray<Outlet> outlets)
    {
        return new Http10ConnectionShape(
            (Inlet<IInputItem>)inlets[0],
            (Outlet<HttpResponseMessage>)outlets[0],
            (Inlet<HttpRequestMessage>)inlets[1],
            (Outlet<IOutputItem>)outlets[1]);
    }
}

public sealed class Http10ConnectionStage : GraphStage<Http10ConnectionShape>
{
    private readonly Inlet<IInputItem> _inServer = new("Http10Connection.In.Server");
    private readonly Outlet<HttpResponseMessage> _outResponse = new("Http10Connection.Out.Response");
    private readonly Inlet<HttpRequestMessage> _inApp = new("Http10Connection.In.App");
    private readonly Outlet<IOutputItem> _outNetwork = new("Http10Connection.Out.Network");

    public override Http10ConnectionShape Shape => new(_inServer, _outResponse, _inApp, _outNetwork);

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this, inheritedAttributes);

    private sealed class Logic : GraphStageLogic, IHttp10StageOperations
    {
        private readonly Http10ConnectionStage _stage;
        private readonly Http10StateMachine _sm;
        private readonly List<IOutputItem> _pendingOutbound = [];
        private readonly List<HttpResponseMessage> _pendingResponses = [];
        private bool _serverFinished;

        public Logic(Http10ConnectionStage stage, Attributes inheritedAttributes) : base(stage.Shape)
        {
            _stage = stage;

            var memoryBuffer = inheritedAttributes.GetAttribute(new TurboAttributes.MemoryBuffer(4 * 1024, 256 * 1024));
            _sm = new Http10StateMachine(this, memoryBuffer.Initial, memoryBuffer.Max);

            SetHandler(stage._inServer, onPush: OnServerPush,
                onUpstreamFinish: () =>
                {
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
                    // App upstream finished — no more requests, but keep processing responses
                },
                onUpstreamFailure: ex =>
                {
                    Log.Warning("Http10ConnectionStage: App inlet upstream failure: {0}", ex.Message);
                    FailStage(ex);
                });

            SetHandler(stage._outNetwork, onPull: OnNetworkPull);
        }

        // ─── IHttp10StageOperations ───

        void IHttp10StageOperations.OnResponse(HttpResponseMessage response)
        {
            _pendingResponses.Add(response);
        }

        void IHttp10StageOperations.OnOutbound(IOutputItem item)
        {
            _pendingOutbound.Add(item);
        }

        void IHttp10StageOperations.OnWarning(string message)
        {
            Log.Warning("Http10ConnectionStage: {0}", message);
        }

        // ─── Handlers ───

        private void OnServerPush()
        {
            var item = Grab(_stage._inServer);

            try
            {
                _sm.DecodeServerData(item);
            }
            catch (HttpRequestException ex)
            {
                // AbruptClose with Content-Length mismatch — fail the stage
                FailStage(ex);
                return;
            }

            FlushOutbound();

            if (_pendingResponses.Count > 0)
            {
                FlushResponses();
            }
            else if (!_serverFinished)
            {
                // No response yet — pull more server data
                if (!HasBeenPulled(_stage._inServer) && !IsClosed(_stage._inServer))
                {
                    Pull(_stage._inServer);
                }
            }

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
            TryPullRequest();
        }

        private void FlushResponses()
        {
            if (_pendingResponses.Count == 0)
            {
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
            // Return any pending pooled items
            foreach (var item in _pendingOutbound)
            {
                switch (item)
                {
                    case ConnectionReuseItem reuseItem:
                        reuseItem.Return();
                        break;
                    case StreamAcquireItem acquireItem:
                        acquireItem.Return();
                        break;
                    case NetworkBuffer buffer:
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
