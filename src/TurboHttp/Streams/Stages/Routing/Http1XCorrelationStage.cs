using System.Collections.Generic;
using System.Collections.Immutable;
using System.Net.Http;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.Internal;

namespace TurboHttp.Streams.Stages.Routing;

public sealed class Http1XCorrelationShape : Shape
{
    public Inlet<HttpRequestMessage> InRequest { get; }
    public Inlet<HttpResponseMessage> InResponse { get; }
    public Outlet<HttpResponseMessage> Out { get; }
    public Outlet<IControlItem> OutSignal { get; }

    public Http1XCorrelationShape(
        Inlet<HttpRequestMessage> inRequest,
        Inlet<HttpResponseMessage> inResponse,
        Outlet<HttpResponseMessage> @out,
        Outlet<IControlItem> outSignal)
    {
        InRequest = inRequest;
        InResponse = inResponse;
        Out = @out;
        OutSignal = outSignal;
    }

    public override ImmutableArray<Inlet> Inlets =>
        [InRequest, InResponse];

    public override ImmutableArray<Outlet> Outlets =>
        [Out, OutSignal];

    public override Shape DeepCopy()
    {
        return new Http1XCorrelationShape(
            (Inlet<HttpRequestMessage>)InRequest.CarbonCopy(),
            (Inlet<HttpResponseMessage>)InResponse.CarbonCopy(),
            (Outlet<HttpResponseMessage>)Out.CarbonCopy(),
            (Outlet<IControlItem>)OutSignal.CarbonCopy());
    }

    public override Shape CopyFromPorts(ImmutableArray<Inlet> inlets, ImmutableArray<Outlet> outlets)
    {
        return new Http1XCorrelationShape(
            (Inlet<HttpRequestMessage>)inlets[0],
            (Inlet<HttpResponseMessage>)inlets[1],
            (Outlet<HttpResponseMessage>)outlets[0],
            (Outlet<IControlItem>)outlets[1]);
    }
}

internal sealed class Http1XCorrelationStage : GraphStage<Http1XCorrelationShape>
{
    private readonly Inlet<HttpRequestMessage> _inRequest = new("H1XCorrelation.In.Request");
    private readonly Inlet<HttpResponseMessage> _inResponse = new("H1XCorrelation.In.Response");
    private readonly Outlet<HttpResponseMessage> _out = new("H1XCorrelation.Out");
    private readonly Outlet<IControlItem> _outSignal = new("H1XCorrelation.Out.Signal");

    public override Http1XCorrelationShape Shape { get; }


    public Http1XCorrelationStage()
    {
        Shape = new Http1XCorrelationShape(_inRequest, _inResponse, _out, _outSignal);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private readonly Queue<HttpRequestMessage> _pending = new();

        private readonly Queue<HttpResponseMessage> _waiting = new();

        private bool _requestUpstreamFinished;
        private bool _responseUpstreamFinished;

        public Logic(Http1XCorrelationStage stage) : base(stage.Shape)
        {
            SetHandler(stage._inRequest,
                onPush: () =>
                {
                    if (_pending.Count == 0)
                    {
                        var request = Grab(stage._inRequest);
                        _pending.Enqueue(request);
                        var key = RequestEndpoint.FromRequest(request);
                        Emit(stage._outSignal, new StreamAcquireItem { Key = key });
                        TryCorrelateAndEmit(stage);
                    }

                    if (!HasBeenPulled(stage._inRequest))
                    {
                        Pull(stage._inRequest);
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
                    TryCorrelateAndEmit(stage);
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

            SetHandler(stage._out,
                onPull: () =>
                {
                    if (!IsClosed(stage._inResponse) && !HasBeenPulled(stage._inResponse))
                    {
                        Pull(stage._inResponse);
                    }

                    if (!IsClosed(stage._inRequest) && !HasBeenPulled(stage._inRequest))
                    {
                        Pull(stage._inRequest);
                    }
                });

            SetHandler(stage._outSignal, onPull: () =>
            {
                // Demand-driven by Emit; no action needed.
            });
        }

        private void TryCorrelateAndEmit(Http1XCorrelationStage stage)
        {
            while (_pending.Count > 0 && _waiting.Count > 0 && IsAvailable(stage._out))
            {
                var response = _waiting.Dequeue();
                response.RequestMessage = _pending.Dequeue();
                Push(stage._out, response);
            }
        }
    }
}