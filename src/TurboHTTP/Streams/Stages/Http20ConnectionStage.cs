using System.Collections.Immutable;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHTTP.Diagnostics;
using TurboHTTP.Internal;
using TurboHTTP.Protocol.Http2;

namespace TurboHTTP.Streams.Stages;

public sealed class Http20ConnectionShape : Shape
{
    public Inlet<IInputItem> InServer { get; }
    public Outlet<HttpResponseMessage> OutResponse { get; }
    public Inlet<HttpRequestMessage> InApp { get; }
    public Outlet<IOutputItem> OutNetwork { get; }

    public Http20ConnectionShape(
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
        return new Http20ConnectionShape(
            (Inlet<IInputItem>)InServer.CarbonCopy(),
            (Outlet<HttpResponseMessage>)OutResponse.CarbonCopy(),
            (Inlet<HttpRequestMessage>)InApp.CarbonCopy(),
            (Outlet<IOutputItem>)OutNetwork.CarbonCopy());
    }

    public override Shape CopyFromPorts(ImmutableArray<Inlet> inlets, ImmutableArray<Outlet> outlets)
    {
        return new Http20ConnectionShape(
            (Inlet<IInputItem>)inlets[0],
            (Outlet<HttpResponseMessage>)outlets[0],
            (Inlet<HttpRequestMessage>)inlets[1],
            (Outlet<IOutputItem>)outlets[1]);
    }
}

public sealed class Http20ConnectionStage : GraphStage<Http20ConnectionShape>
{
    private readonly Inlet<IInputItem> _inServer = new("Http20Connection.In.Server");
    private readonly Outlet<HttpResponseMessage> _outResponse = new("Http20Connection.Out.Response");
    private readonly Inlet<HttpRequestMessage> _inApp = new("Http20Connection.In.App");
    private readonly Outlet<IOutputItem> _outNetwork = new("Http20Connection.Out.Network");

    private readonly Http2ConnectionConfig _config;

    public Http20ConnectionStage(Http2ConnectionConfig? config = null)
    {
        _config = config ?? new Http2ConnectionConfig();
    }

    public override Http20ConnectionShape Shape => new(_inServer, _outResponse, _inApp, _outNetwork);

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : GraphStageLogic, IHttp2StageOperations
    {
        private readonly Http20ConnectionStage _stage;
        private readonly StateMachine _sm;
        private readonly List<IOutputItem> _pendingOutbound = [];
        private readonly List<HttpResponseMessage> _pendingResponses = [];

        public Logic(Http20ConnectionStage stage) : base(stage.Shape)
        {
            _stage = stage;
            _sm = new StateMachine(stage._config, this);

            SetHandler(stage._inServer, onPush: OnServerPush,
                onUpstreamFinish: () =>
                {
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
                onUpstreamFinish: () => { },
                onUpstreamFailure: ex =>
                {
                    Log.Warning("Http20ConnectionStage: App inlet upstream failure: {0}", ex.Message);
                    FailStage(ex);
                });

            SetHandler(stage._outNetwork, onPull: OnNetworkPull);
        }

        // ─── IHttp2StageOperations ───

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

        // ─── Handlers ───

        private void OnServerPush()
        {
            var item = Grab(_stage._inServer);

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
                Pull(_stage._inServer);
                return;
            }

            EmitMultiple(_stage._outResponse, _pendingResponses.ToArray(),
                () => Pull(_stage._inServer));
            _pendingResponses.Clear();
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
    }
}