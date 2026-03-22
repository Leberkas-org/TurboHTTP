using System.Collections.Generic;
using System.Net.Http;
using Akka.Streams;
using Akka.Streams.Stage;

namespace TurboHttp.Streams.Stages.Routing;

internal sealed class
    Http20CorrelationStage :
    GraphStage<FanInShape<(HttpRequestMessage, int), (HttpResponseMessage, int), HttpResponseMessage>>
{
    private readonly Inlet<(HttpRequestMessage, int)> _inRequest = new("Http20Correlation.In.Request");
    private readonly Inlet<(HttpResponseMessage, int)> _inResponse = new("Http20Correlation.In.Response");
    private readonly Outlet<HttpResponseMessage> _out = new("Http20Correlation.Out");

    public override FanInShape<(HttpRequestMessage, int), (HttpResponseMessage, int), HttpResponseMessage> Shape
    {
        get;
    }


    public Http20CorrelationStage()
    {
        Shape = new FanInShape<(HttpRequestMessage, int), (HttpResponseMessage, int), HttpResponseMessage>(
            _out, _inRequest, _inResponse);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private readonly Dictionary<int, HttpRequestMessage> _pending = new();
        private readonly Dictionary<int, HttpResponseMessage> _waiting = new();

        private bool _requestUpstreamFinished;
        private bool _responseUpstreamFinished;

        public Logic(Http20CorrelationStage stage) : base(stage.Shape)
        {
            SetHandler(stage._inRequest,
                onPush: () =>
                {
                    var (request, streamId) = Grab(stage._inRequest);

                    _pending[streamId] = request;
                    TryCorrelateAndEmit(stage);

                    if (!HasBeenPulled(stage._inRequest))
                    {
                        Pull(stage._inRequest);
                    }
                },
                onUpstreamFinish: () =>
                {
                    _requestUpstreamFinished = true;
                    TryComplete();
                });

            SetHandler(stage._inResponse,
                onPush: () =>
                {
                    var (response, streamId) = Grab(stage._inResponse);

                    _waiting[streamId] = response;
                    TryCorrelateAndEmit(stage);

                    if (!HasBeenPulled(stage._inResponse))
                    {
                        Pull(stage._inResponse);
                    }
                },
                onUpstreamFinish: () =>
                {
                    _responseUpstreamFinished = true;
                    TryComplete();
                });

            SetHandler(stage._out,
                onPull: () =>
                {
                    TryCorrelateAndEmit(stage);

                    if (!HasBeenPulled(stage._inRequest))
                    {
                        Pull(stage._inRequest);
                    }

                    if (!HasBeenPulled(stage._inResponse))
                    {
                        Pull(stage._inResponse);
                    }
                });
        }

        private void TryCorrelateAndEmit(Http20CorrelationStage stage)
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
            if (_requestUpstreamFinished && _responseUpstreamFinished)
            {
                CompleteStage();
            }
        }
    }
}