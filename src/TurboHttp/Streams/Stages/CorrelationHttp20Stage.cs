using System.Collections.Generic;
using System.Net.Http;
using Akka.Streams;
using Akka.Streams.Stage;

namespace TurboHttp.Streams.Stages;

internal sealed class
    CorrelationHttp20Stage :
    GraphStage<FanInShape<(HttpRequestMessage, int), (HttpResponseMessage, int), HttpResponseMessage>>
{
    private readonly Inlet<(HttpRequestMessage, int)> _requestIn = new("correlation.request.in");
    private readonly Inlet<(HttpResponseMessage, int)> _responseIn = new("correlation.response.in");
    private readonly Outlet<HttpResponseMessage> _out = new("correlation.out");

    public override FanInShape<(HttpRequestMessage, int), (HttpResponseMessage, int), HttpResponseMessage> Shape
    {
        get;
    }

    public CorrelationHttp20Stage()
    {
        Shape = new FanInShape<(HttpRequestMessage, int), (HttpResponseMessage, int), HttpResponseMessage>(
            _out, _requestIn, _responseIn);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private readonly Dictionary<int, HttpRequestMessage> _pending = new();
        private readonly Dictionary<int, HttpResponseMessage> _waiting = new();

        public Logic(CorrelationHttp20Stage stage) : base(stage.Shape)
        {
            SetHandler(stage._requestIn,
                onPush: () =>
                {
                    var (request, streamId) = Grab(stage._requestIn);

                    _pending[streamId] = request;
                    TryCorrelateAndEmit(stage);

                    if (!HasBeenPulled(stage._requestIn))
                    {
                        Pull(stage._requestIn);
                    }
                },
                onUpstreamFinish: TryComplete);

            SetHandler(stage._responseIn,
                onPush: () =>
                {
                    var (response, streamId) = Grab(stage._responseIn);

                    _waiting[streamId] = response;
                    TryCorrelateAndEmit(stage);

                    if (!HasBeenPulled(stage._responseIn))
                    {
                        Pull(stage._responseIn);
                    }
                },
                onUpstreamFinish: TryComplete);

            SetHandler(stage._out,
                onPull: () =>
                {
                    TryCorrelateAndEmit(stage);

                    if (!HasBeenPulled(stage._requestIn))
                    {
                        Pull(stage._requestIn);
                    }

                    if (!HasBeenPulled(stage._responseIn))
                    {
                        Pull(stage._responseIn);
                    }
                });
        }

        private void TryCorrelateAndEmit(CorrelationHttp20Stage stage)
        {
            if (!IsAvailable(stage._out))
            {
                return;
            }

            foreach (var (streamId, response) in _waiting)
            {
                if (_pending.Remove(streamId, out var request))
                {
                    _waiting.Remove(streamId);

                    response.RequestMessage = request;

                    Push(stage._out, response);
                    return;
                }
            }
        }

        private void TryComplete()
        {
            if (_pending.Count == 0 && _waiting.Count == 0)
            {
                CompleteStage();
            }
        }
    }
}