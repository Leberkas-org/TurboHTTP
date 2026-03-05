using System.Collections.Generic;
using System.Net.Http;
using Akka.Streams;
using Akka.Streams.Stage;

namespace TurboHttp.Streams.Stages;

internal sealed class
    CorrelationHttp1XStage : GraphStage<FanInShape<HttpRequestMessage, HttpResponseMessage, HttpResponseMessage>>
{
    private readonly Inlet<HttpRequestMessage> _requestIn = new("correlation.request.in");
    private readonly Inlet<HttpResponseMessage> _responseIn = new("correlation.response.in");
    private readonly Outlet<HttpResponseMessage> _out = new("correlation.out");

    public override FanInShape<HttpRequestMessage, HttpResponseMessage, HttpResponseMessage> Shape { get; }

    public CorrelationHttp1XStage()
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

        public Logic(CorrelationHttp1XStage http1XStage) : base(http1XStage.Shape)
        {
            SetHandler(http1XStage._requestIn,
                onPush: () =>
                {
                    _pending.Enqueue(Grab(http1XStage._requestIn));
                    TryCorrelateAndEmit(http1XStage);
                    Pull(http1XStage._requestIn);
                },
                onUpstreamFinish: () =>
                {
                    if (_pending.Count == 0 && _waiting.Count == 0)
                    {
                        CompleteStage();
                    }
                });

            SetHandler(http1XStage._responseIn,
                onPush: () =>
                {
                    _waiting.Enqueue(Grab(http1XStage._responseIn));
                    TryCorrelateAndEmit(http1XStage);
                },
                onUpstreamFinish: () =>
                {
                    if (_pending.Count == 0 && _waiting.Count == 0)
                    {
                        CompleteStage();
                    }
                });

            SetHandler(http1XStage._out,
                onPull: () =>
                {
                    if (!HasBeenPulled(http1XStage._responseIn))
                    {
                        Pull(http1XStage._responseIn);
                    }

                    if (!HasBeenPulled(http1XStage._requestIn))
                    {
                        Pull(http1XStage._requestIn);
                    }
                });
        }

        private void TryCorrelateAndEmit(CorrelationHttp1XStage http1XStage)
        {
            while (_pending.Count > 0 && _waiting.Count > 0 && IsAvailable(http1XStage._out))
            {
                var response = _waiting.Dequeue();
                response.RequestMessage = _pending.Dequeue();
                Push(http1XStage._out, response);
            }
        }
    }
}