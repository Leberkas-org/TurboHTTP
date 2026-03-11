using System.Collections.Generic;
using System.Net.Http;
using Akka.Streams;
using Akka.Streams.Stage;

namespace TurboHttp.Streams.Stages;

internal sealed class
    Http1XCorrelationStage : GraphStage<FanInShape<HttpRequestMessage, HttpResponseMessage, HttpResponseMessage>>
{
    private readonly Inlet<HttpRequestMessage> _requestIn = new("correlation.request.in");
    private readonly Inlet<HttpResponseMessage> _responseIn = new("correlation.response.in");
    private readonly Outlet<HttpResponseMessage> _out = new("correlation.out");

    public override FanInShape<HttpRequestMessage, HttpResponseMessage, HttpResponseMessage> Shape { get; }

    public Http1XCorrelationStage()
    {
        Shape = new FanInShape<HttpRequestMessage, HttpResponseMessage, HttpResponseMessage>(
            _out, _requestIn, _responseIn);
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
                        _pending.Enqueue(Grab(stage._requestIn));
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