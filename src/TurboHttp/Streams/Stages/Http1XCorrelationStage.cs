using System.Collections.Generic;
using System.Collections.Immutable;
using System.Net.Http;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.IO.Stages;

namespace TurboHttp.Streams.Stages;

public sealed class Http1XCorrelationShape : Shape
{
    public Inlet<HttpRequestMessage> RequestIn { get; }
    public Inlet<HttpResponseMessage> ResponseIn { get; }
    public Outlet<HttpResponseMessage> Out { get; }
    public Outlet<IControlItem> OutletSignal { get; }

    public Http1XCorrelationShape(
        Inlet<HttpRequestMessage> requestIn,
        Inlet<HttpResponseMessage> responseIn,
        Outlet<HttpResponseMessage> @out,
        Outlet<IControlItem> outletSignal)
    {
        RequestIn = requestIn;
        ResponseIn = responseIn;
        Out = @out;
        OutletSignal = outletSignal;
    }

    public override ImmutableArray<Inlet> Inlets =>
        ImmutableArray.Create<Inlet>(RequestIn, ResponseIn);

    public override ImmutableArray<Outlet> Outlets =>
        ImmutableArray.Create<Outlet>(Out, OutletSignal);

    public override Shape DeepCopy()
    {
        return new Http1XCorrelationShape(
            (Inlet<HttpRequestMessage>)RequestIn.CarbonCopy(),
            (Inlet<HttpResponseMessage>)ResponseIn.CarbonCopy(),
            (Outlet<HttpResponseMessage>)Out.CarbonCopy(),
            (Outlet<IControlItem>)OutletSignal.CarbonCopy());
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
    private readonly Inlet<HttpRequestMessage> _requestIn = new("correlation.request.in");
    private readonly Inlet<HttpResponseMessage> _responseIn = new("correlation.response.in");
    private readonly Outlet<HttpResponseMessage> _out = new("correlation.out");
    private readonly Outlet<IControlItem> _outletSignal = new("correlation.signal.out");

    public override Http1XCorrelationShape Shape { get; }


    public Http1XCorrelationStage()
    {
        Shape = new Http1XCorrelationShape(_requestIn, _responseIn, _out, _outletSignal);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private readonly Queue<HttpRequestMessage> _pending = new();

        private readonly Queue<HttpResponseMessage> _waiting = new();

        public Logic(Http1XCorrelationStage stage) : base(stage.Shape)
        {
            SetHandler(stage._requestIn,
                onPush: () =>
                {
                    if (_pending.Count == 0)
                    {
                        var request = Grab(stage._requestIn);
                        _pending.Enqueue(request);
                        var key = RequestEndpoint.FromRequest(request);
                        Emit(stage._outletSignal, new StreamAcquireItem { Key = key });
                        TryCorrelateAndEmit(stage);
                    }

                    if (!HasBeenPulled(stage._requestIn))
                    {
                        Pull(stage._requestIn);
                    }
                },
                onUpstreamFinish: () =>
                {
                    if (_pending.Count == 0 && _waiting.Count == 0)
                    {
                        CompleteStage();
                    }
                });

            SetHandler(stage._responseIn,
                onPush: () =>
                {
                    _waiting.Enqueue(Grab(stage._responseIn));
                    TryCorrelateAndEmit(stage);
                    if (!HasBeenPulled(stage._responseIn))
                    {
                        Pull(stage._responseIn);
                    }
                },
                onUpstreamFinish: () =>
                {
                    if (_pending.Count == 0 && _waiting.Count == 0)
                    {
                        CompleteStage();
                    }
                });

            SetHandler(stage._out,
                onPull: () =>
                {
                    if (!IsClosed(stage._responseIn) && !HasBeenPulled(stage._responseIn))
                    {
                        Pull(stage._responseIn);
                    }

                    if (!IsClosed(stage._requestIn) && !HasBeenPulled(stage._requestIn))
                    {
                        Pull(stage._requestIn);
                    }
                });

            SetHandler(stage._outletSignal, onPull: () =>
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