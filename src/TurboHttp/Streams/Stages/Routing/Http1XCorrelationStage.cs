using System.Collections.Generic;
using System.Collections.Immutable;
using System.Net.Http;
using Akka;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.Internal;

namespace TurboHttp.Streams.Stages.Routing;

public sealed class Http1XCorrelationShape : Shape
{
    public Inlet<HttpRequestMessage> InRequest { get; }
    public Inlet<HttpResponseMessage> InResponse { get; }
    public Inlet<NotUsed> InReset { get; }
    public Outlet<HttpResponseMessage> OutResponse { get; }
    public Outlet<IControlItem> OutControl { get; }

    public Http1XCorrelationShape(
        Inlet<HttpRequestMessage> inRequest,
        Inlet<HttpResponseMessage> inResponse,
        Inlet<NotUsed> inReset,
        Outlet<HttpResponseMessage> outResponse,
        Outlet<IControlItem> outControl)
    {
        InRequest = inRequest;
        InResponse = inResponse;
        InReset = inReset;
        OutResponse = outResponse;
        OutControl = outControl;
    }

    public override ImmutableArray<Inlet> Inlets =>
        [InRequest, InResponse, InReset];

    public override ImmutableArray<Outlet> Outlets =>
        [OutResponse, OutControl];

    public override Shape DeepCopy()
    {
        return new Http1XCorrelationShape(
            (Inlet<HttpRequestMessage>)InRequest.CarbonCopy(),
            (Inlet<HttpResponseMessage>)InResponse.CarbonCopy(),
            (Inlet<NotUsed>)InReset.CarbonCopy(),
            (Outlet<HttpResponseMessage>)OutResponse.CarbonCopy(),
            (Outlet<IControlItem>)OutControl.CarbonCopy());
    }

    public override Shape CopyFromPorts(ImmutableArray<Inlet> inlets, ImmutableArray<Outlet> outlets)
    {
        return new Http1XCorrelationShape(
            (Inlet<HttpRequestMessage>)inlets[0],
            (Inlet<HttpResponseMessage>)inlets[1],
            (Inlet<NotUsed>)inlets[2],
            (Outlet<HttpResponseMessage>)outlets[0],
            (Outlet<IControlItem>)outlets[1]);
    }
}

internal sealed class Http1XCorrelationStage : GraphStage<Http1XCorrelationShape>
{
    private readonly Inlet<HttpRequestMessage> _inRequest = new("Http1XCorrelation.In.Request");
    private readonly Inlet<HttpResponseMessage> _inResponse = new("Http1XCorrelation.In.Response");
    private readonly Inlet<NotUsed> _inReset = new("Http1XCorrelation.In.Reset");
    private readonly Outlet<HttpResponseMessage> _out = new("Http1XCorrelation.Out");
    private readonly Outlet<IControlItem> _outSignal = new("Http1XCorrelation.Out.Signal");

    public override Http1XCorrelationShape Shape { get; }

    public Http1XCorrelationStage()
    {
        Shape = new Http1XCorrelationShape(_inRequest, _inResponse, _inReset, _out, _outSignal);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private readonly Http1XCorrelationStage _stage;
        private readonly Queue<HttpRequestMessage> _pending = new();
        private readonly Queue<HttpResponseMessage> _waiting = new();

        private bool _requestUpstreamFinished;
        private bool _responseUpstreamFinished;
        private bool _pipelineUnlocked;

        public Logic(Http1XCorrelationStage stage) : base(stage.Shape)
        {
            _stage = stage;

            SetHandler(stage._inRequest,
                onPush: () =>
                {
                    var request = Grab(stage._inRequest);
                    var wasEmpty = _pending.Count == 0;
                    _pending.Enqueue(request);

                    if (wasEmpty)
                    {
                        var key = RequestEndpoint.FromRequest(request);
                        Emit(stage._outSignal, new StreamAcquireItem { Key = key });
                    }

                    TryCorrelateAndEmit();

                    if (_pipelineUnlocked || _pending.Count == 0)
                    {
                        if (!HasBeenPulled(stage._inRequest))
                        {
                            Pull(stage._inRequest);
                        }
                    }
                },
                onUpstreamFinish: () =>
                {
                    _requestUpstreamFinished = true;
                    if (_responseUpstreamFinished)
                    {
                        CompleteStage();
                    }
                });

            SetHandler(stage._inResponse,
                onPush: () =>
                {
                    _waiting.Enqueue(Grab(stage._inResponse));
                    TryCorrelateAndEmit();
                    if (!HasBeenPulled(stage._inResponse))
                    {
                        Pull(stage._inResponse);
                    }
                },
                onUpstreamFinish: () =>
                {
                    _responseUpstreamFinished = true;
                    if (_requestUpstreamFinished)
                    {
                        CompleteStage();
                    }
                });

            SetHandler(stage._inReset,
                onPush: () =>
                {
                    Grab(stage._inReset);
                    _pipelineUnlocked = false;
                    if (!HasBeenPulled(stage._inReset))
                    {
                        Pull(stage._inReset);
                    }
                },
                onUpstreamFinish: () =>
                {
                    // InReset upstream finishing does not affect stage completion.
                });

            SetHandler(stage._out,
                onPull: () =>
                {
                    if (!IsClosed(stage._inResponse) && !HasBeenPulled(stage._inResponse))
                    {
                        Pull(stage._inResponse);
                    }

                    if (!IsClosed(stage._inRequest) && !HasBeenPulled(stage._inRequest))
                    {
                        if (_pipelineUnlocked || _pending.Count == 0)
                        {
                            Pull(stage._inRequest);
                        }
                    }
                });

            SetHandler(stage._outSignal, onPull: () =>
            {
                // Demand-driven by Emit; no action needed.
            });
        }

        public override void PreStart()
        {
            Pull(_stage._inReset);
        }

        private void TryCorrelateAndEmit()
        {
            while (_pending.Count > 0 && _waiting.Count > 0 && IsAvailable(_stage._out))
            {
                var response = _waiting.Dequeue();
                response.RequestMessage = _pending.Dequeue();
                Push(_stage._out, response);

                if (!_pipelineUnlocked)
                {
                    _pipelineUnlocked = true;

                    if (!IsClosed(_stage._inRequest) && !HasBeenPulled(_stage._inRequest))
                    {
                        Pull(_stage._inRequest);
                    }
                }
            }
        }
    }
}
