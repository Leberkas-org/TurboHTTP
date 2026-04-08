using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;

namespace TurboHTTP.Streams.Stages.Routing;

internal sealed class
    Http30CorrelationStage :
    GraphStage<FanInShape<HttpRequestMessage, HttpResponseMessage, HttpResponseMessage>>
{
    private readonly Inlet<HttpRequestMessage> _inRequest = new("Http30Correlation.In.Request");
    private readonly Inlet<HttpResponseMessage> _inResponse = new("Http30Correlation.In.Response");
    private readonly Outlet<HttpResponseMessage> _out = new("Http30Correlation.Out");

    public override FanInShape<HttpRequestMessage, HttpResponseMessage, HttpResponseMessage> Shape
    {
        get;
    }


    public Http30CorrelationStage()
    {
        Shape = new FanInShape<HttpRequestMessage, HttpResponseMessage, HttpResponseMessage>(
            _out, _inRequest, _inResponse);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private readonly Queue<HttpRequestMessage> _pendingRequests = new();
        private readonly Queue<HttpResponseMessage> _pendingResponses = new();

        private bool _requestUpstreamFinished;
        private bool _responseUpstreamFinished;

        public Logic(Http30CorrelationStage stage) : base(stage.Shape)
        {
            SetHandler(stage._inRequest,
                onPush: () =>
                {
                    var request = Grab(stage._inRequest);

                    _pendingRequests.Enqueue(request);
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
                },
                onUpstreamFailure: ex =>
                {
                    Log.Warning("Http30CorrelationStage: Request inlet upstream failure: {0}", ex.Message);
                    Log.Debug("Http30CorrelationStage: Failing stage due to request inlet upstream failure.");
                    FailStage(ex);
                });

            SetHandler(stage._inResponse,
                onPush: () =>
                {
                    var response = Grab(stage._inResponse);

                    _pendingResponses.Enqueue(response);
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
                },
                onUpstreamFailure: ex =>
                {
                    Log.Warning("Http30CorrelationStage: Response inlet upstream failure: {0}", ex.Message);
                    Log.Debug("Http30CorrelationStage: Failing stage due to response inlet upstream failure.");
                    FailStage(ex);
                });

            SetHandler(stage._out,
                onPull: () =>
                {
                    TryCorrelateAndEmit(stage);

                    if (!IsClosed(stage._inRequest) && !HasBeenPulled(stage._inRequest))
                    {
                        Pull(stage._inRequest);
                    }

                    if (!IsClosed(stage._inResponse) && !HasBeenPulled(stage._inResponse))
                    {
                        Pull(stage._inResponse);
                    }
                });
        }

        private void TryCorrelateAndEmit(Http30CorrelationStage stage)
        {
            if (!IsAvailable(stage._out))
            {
                return;
            }

            if (_pendingRequests.Count > 0 && _pendingResponses.Count > 0)
            {
                var request = _pendingRequests.Dequeue();
                var response = _pendingResponses.Dequeue();

                response.RequestMessage = request;

                Push(stage._out, response);
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
