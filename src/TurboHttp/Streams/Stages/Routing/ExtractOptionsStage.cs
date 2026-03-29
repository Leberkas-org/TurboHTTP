using System.Collections.Immutable;
using System.Net;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.Internal;
using TurboHttp.Transport;

namespace TurboHttp.Streams.Stages.Routing;

/// <summary>
/// Custom shape for <see cref="ExtractOptionsStage"/>: two inlets (request + reuse feedback),
/// two outlets (request passthrough + connection signal).
/// </summary>
internal sealed class ExtractOptionsShape : Shape
{
    public Inlet<HttpRequestMessage> In { get; }
    public Inlet<IControlItem> InReuse { get; }
    public Outlet<HttpRequestMessage> OutRequest { get; }
    public Outlet<IOutputItem> OutSignal { get; }

    public ExtractOptionsShape(
        Inlet<HttpRequestMessage> @in,
        Inlet<IControlItem> inReuse,
        Outlet<HttpRequestMessage> outRequest,
        Outlet<IOutputItem> outSignal)
    {
        In = @in;
        InReuse = inReuse;
        OutRequest = outRequest;
        OutSignal = outSignal;
    }

    public override ImmutableArray<Inlet> Inlets => [In, InReuse];

    public override ImmutableArray<Outlet> Outlets => [OutRequest, OutSignal];

    public override Shape DeepCopy()
    {
        return new ExtractOptionsShape(
            (Inlet<HttpRequestMessage>)In.CarbonCopy(),
            (Inlet<IControlItem>)InReuse.CarbonCopy(),
            (Outlet<HttpRequestMessage>)OutRequest.CarbonCopy(),
            (Outlet<IOutputItem>)OutSignal.CarbonCopy());
    }

    public override Shape CopyFromPorts(ImmutableArray<Inlet> inlets, ImmutableArray<Outlet> outlets)
    {
        return new ExtractOptionsShape(
            (Inlet<HttpRequestMessage>)inlets[0],
            (Inlet<IControlItem>)inlets[1],
            (Outlet<HttpRequestMessage>)outlets[0],
            (Outlet<IOutputItem>)outlets[1]);
    }
}

internal sealed class ExtractOptionsStage : GraphStage<ExtractOptionsShape>
{
    private readonly TurboClientOptions _clientOptions;
    private readonly Inlet<HttpRequestMessage> _in = new("ExtractOptions.In");
    private readonly Inlet<IControlItem> _inReuse = new("ExtractOptions.In.Reuse");
    private readonly Outlet<HttpRequestMessage> _outRequest = new("ExtractOptions.Out.Request");
    private readonly Outlet<IOutputItem> _outSignal = new("ExtractOptions.Out.Signal");

    public override ExtractOptionsShape Shape { get; }

    public ExtractOptionsStage(TurboClientOptions? clientOptions = null)
    {
        _clientOptions = clientOptions ?? new TurboClientOptions();
        Shape = new ExtractOptionsShape(_in, _inReuse, _outRequest, _outSignal);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private readonly ExtractOptionsStage _stage;
        private bool _connectItemSent;
        private bool _needsReconnect;
        private HttpRequestMessage? _pending;

        public Logic(ExtractOptionsStage stage) : base(stage.Shape)
        {
            _stage = stage;
            SetHandler(stage._in,
                onPush: () =>
                {
                    var request = Grab(stage._in);
                    Log.Debug("ExtractOptionsStage: onPush request={0} {1}, connectItemSent={2}, needsReconnect={3}",
                        request.Method, request.RequestUri, _connectItemSent, _needsReconnect);

                    // HTTP/1.0 always closes after each response (RFC 1945 §6.1),
                    // so every request needs a fresh connection — no feedback wait.
                    var isHttp10 = request.Version == HttpVersion.Version10;

                    if (!_connectItemSent || _needsReconnect || isHttp10)
                    {
                        // First request, reconnect needed, or HTTP/1.0: emit ConnectItem
                        var options = TcpOptionsFactory.Build(request.RequestUri!, stage._clientOptions, request.Version);
                        _pending = request;
                        _connectItemSent = true;
                        _needsReconnect = false;
                        Log.Debug("ExtractOptionsStage: emitting ConnectItem for {0}", request.RequestUri?.Authority);
                        Push(stage._outSignal, new ConnectItem(options) { Key = RequestEndpoint.FromRequest(request) });

                        // The downstream may have already pulled _outRequest
                        // before the first element arrived (pull propagation is synchronous
                        // while Source.Queue delivery is async). Serve that demand now.
                        if (IsAvailable(stage._outRequest))
                        {
                            Log.Debug("ExtractOptionsStage: outRequest available — pushing pending request immediately");
                            Push(stage._outRequest, _pending);
                            _pending = null;
                        }
                    }
                    else
                    {
                        Push(stage._outRequest, request);
                    }
                },
                onUpstreamFinish: CompleteStage,
                onUpstreamFailure: ex =>
                {
                    // Absorb the failure (don't crash the pipeline) but complete the stage
                    // so downstream stages can drain and the substream shuts down cleanly.
                    Log.Warning("ExtractOptionsStage: Upstream failure absorbed: {0}", ex.Message);
                    CompleteStage();
                });

            SetHandler(stage._inReuse,
                onPush: () =>
                {
                    var item = Grab(stage._inReuse);
                    if (item is ConnectionReuseItem reuse)
                    {
                        Log.Debug("ExtractOptionsStage: InReuse received canReuse={0}, _needsReconnect before={1}", reuse.Decision.CanReuse, _needsReconnect);
                        if (!reuse.Decision.CanReuse)
                        {
                            _needsReconnect = true;
                            Log.Debug("ExtractOptionsStage: _needsReconnect set to true");
                        }
                    }
                    else
                    {
                        Log.Debug("ExtractOptionsStage: InReuse received non-reuse item {0}", item.GetType().Name);
                    }

                    Pull(stage._inReuse);
                },
                onUpstreamFinish: () => { },
                onUpstreamFailure: ex => Log.Warning("ExtractOptionsStage: Reuse feedback failure absorbed: {0}", ex.Message));

            SetHandler(stage._outSignal,
                onPull: () =>
                {
                    if (!_connectItemSent && !HasBeenPulled(stage._in))
                    {
                        Pull(stage._in);
                    }
                }, onDownstreamFinish: _ => { });

            SetHandler(stage._outRequest,
                onPull: () =>
                {
                    if (_pending is not null)
                    {
                        Push(stage._outRequest, _pending);
                        _pending = null;
                    }
                    else if (!HasBeenPulled(stage._in))
                    {
                        Pull(stage._in);
                    }
                }, onDownstreamFinish: _ => CompleteStage());
        }

        public override void PreStart()
        {
            // Prime the feedback inlet so it is ready to receive reuse signals
            Pull(_stage._inReuse);
        }
    }
}
