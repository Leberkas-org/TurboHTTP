using System.Collections.Immutable;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHTTP.Internal;
using TurboHTTP.Protocol.Http11;

namespace TurboHTTP.Streams.Stages.Routing;

public sealed class Http1XCorrelationShape : Shape
{
    public Inlet<HttpRequestMessage> InRequest { get; }
    public Inlet<HttpResponseMessage> InResponse { get; }
    public Outlet<HttpResponseMessage> OutResponse { get; }
    public Outlet<IOutputItem> OutControl { get; }

    public Http1XCorrelationShape(
        Inlet<HttpRequestMessage> inRequest,
        Inlet<HttpResponseMessage> inResponse,
        Outlet<HttpResponseMessage> outResponse,
        Outlet<IOutputItem> outControl)
    {
        InRequest = inRequest;
        InResponse = inResponse;
        OutResponse = outResponse;
        OutControl = outControl;
    }

    public override ImmutableArray<Inlet> Inlets =>
        [InRequest, InResponse];

    public override ImmutableArray<Outlet> Outlets =>
        [OutResponse, OutControl];

    public override Shape DeepCopy()
    {
        return new Http1XCorrelationShape(
            (Inlet<HttpRequestMessage>)InRequest.CarbonCopy(),
            (Inlet<HttpResponseMessage>)InResponse.CarbonCopy(),
            (Outlet<HttpResponseMessage>)OutResponse.CarbonCopy(),
            (Outlet<IOutputItem>)OutControl.CarbonCopy());
    }

    public override Shape CopyFromPorts(ImmutableArray<Inlet> inlets, ImmutableArray<Outlet> outlets)
    {
        return new Http1XCorrelationShape(
            (Inlet<HttpRequestMessage>)inlets[0],
            (Inlet<HttpResponseMessage>)inlets[1],
            (Outlet<HttpResponseMessage>)outlets[0],
            (Outlet<IOutputItem>)outlets[1]);
    }
}

/// <summary>
/// Simplified request-response correlation for HTTP/1.0 (RFC 1945).
/// No pipelining — exactly one request in flight at a time.
/// Signals (StreamAcquireItem, ConnectionReuseItem) are emitted inline
/// without a queue, eliminating the signal-gate bottleneck.
/// </summary>
internal sealed class Http10CorrelationStage : GraphStage<Http1XCorrelationShape>
{
    private readonly Inlet<HttpRequestMessage> _inRequest = new("Http10Correlation.In.Request");
    private readonly Inlet<HttpResponseMessage> _inResponse = new("Http10Correlation.In.Response");
    private readonly Outlet<HttpResponseMessage> _out = new("Http10Correlation.Out");
    private readonly Outlet<IOutputItem> _outSignal = new("Http10Correlation.Out.Signal");

    public override Http1XCorrelationShape Shape { get; }

    public Http10CorrelationStage()
    {
        Shape = new Http1XCorrelationShape(_inRequest, _inResponse, _out, _outSignal);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private readonly Http10CorrelationStage _stage;

        private HttpRequestMessage? _inFlightRequest;
        private HttpResponseMessage? _pendingResponse;
        private IOutputItem? _pendingSignal;
        private bool _requestUpstreamFinished;
        private bool _responseUpstreamFinished;

        public Logic(Http10CorrelationStage stage) : base(stage.Shape)
        {
            _stage = stage;

            SetHandler(stage._inRequest,
                onPush: () =>
                {
                    var request = Grab(stage._inRequest);
                    _inFlightRequest = request;

                    // Emit StreamAcquireItem immediately — no queue, no gate.
                    var signal = StreamAcquireItem.Rent(RequestEndpoint.FromRequest(request));
                    TryPushSignal(signal);

                    if (_responseUpstreamFinished)
                    {
                        CompleteStage();
                        return;
                    }

                    TryPullResponse();
                    // Do NOT pull next request — HTTP/1.0 is strictly 1-in-flight.
                },
                onUpstreamFinish: () =>
                {
                    _requestUpstreamFinished = true;
                    TryComplete();
                },
                onUpstreamFailure: ex =>
                {
                    Log.Warning("Http10CorrelationStage: Upstream failure absorbed: {0}", ex.Message);
                    CompleteStage();
                });

            SetHandler(stage._inResponse,
                onPush: () =>
                {
                    var response = Grab(stage._inResponse);
                    var request = _inFlightRequest!;
                    _inFlightRequest = null;
                    response.RequestMessage = request;

                    // HTTP/1.0 default is Connection: close (RFC 1945).
                    var endpoint = response.RequestMessage is { RequestUri: not null }
                        ? RequestEndpoint.FromRequest(response.RequestMessage)
                        : RequestEndpoint.Default;
                    var decision = ConnectionReuseEvaluator.Evaluate(response, response.Version);
                    var signal = ConnectionReuseItem.Rent(endpoint, decision);
                    TryPushSignal(signal);

                    if (IsAvailable(stage._out))
                    {
                        Push(stage._out, response);
                    }
                    else
                    {
                        _pendingResponse = response;
                    }
                },
                onUpstreamFinish: () =>
                {
                    _responseUpstreamFinished = true;

                    if (_inFlightRequest is not null)
                    {
                        Log.Warning(
                            "Http10CorrelationStage: Connection closed with orphaned request — emitting PipelineRetryItem");
                        var retrySignal = new PipelineRetryItem(_inFlightRequest);
                        _inFlightRequest = null;
                        TryPushSignal(retrySignal);
                    }

                    TryComplete();
                },
                onUpstreamFailure: ex =>
                {
                    Log.Warning("Http10CorrelationStage: Response upstream failure absorbed: {0}", ex.Message);

                    if (_inFlightRequest is not null)
                    {
                        var retrySignal = new PipelineRetryItem(_inFlightRequest);
                        _inFlightRequest = null;
                        TryPushSignal(retrySignal);
                    }

                    CompleteStage();
                });

            SetHandler(stage._out,
                onPull: () =>
                {
                    if (_pendingResponse is not null)
                    {
                        var resp = _pendingResponse;
                        _pendingResponse = null;
                        Push(stage._out, resp);
                    }

                    TryPullResponse();
                    TryPullRequest();
                });

            SetHandler(stage._outSignal, onPull: () =>
            {
                if (_pendingSignal is not null)
                {
                    var sig = _pendingSignal;
                    _pendingSignal = null;
                    Push(stage._outSignal, sig);
                    TryPullRequest();
                }
            });
        }

        private void TryPushSignal(IOutputItem signal)
        {
            if (IsAvailable(_stage._outSignal))
            {
                Push(_stage._outSignal, signal);
            }
            else
            {
                _pendingSignal = signal;
            }
        }

        private void TryPullRequest()
        {
            if (_inFlightRequest is null
                && !_requestUpstreamFinished
                && !IsClosed(_stage._inRequest)
                && !HasBeenPulled(_stage._inRequest))
            {
                Pull(_stage._inRequest);
            }
        }

        private void TryPullResponse()
        {
            if (_pendingResponse is not null)
            {
                return;
            }

            if (_inFlightRequest is not null
                && !IsClosed(_stage._inResponse)
                && !HasBeenPulled(_stage._inResponse))
            {
                Pull(_stage._inResponse);
            }
        }

        private void TryComplete()
        {
            if (_requestUpstreamFinished && _responseUpstreamFinished && _inFlightRequest is null)
            {
                CompleteStage();
            }
        }

        public override void PostStop()
        {
            if (_pendingSignal is ConnectionReuseItem reuseItem)
            {
                reuseItem.Return();
            }
            else if (_pendingSignal is StreamAcquireItem acquireItem)
            {
                acquireItem.Return();
            }

            if (_inFlightRequest is not null)
            {
                Log.Warning(
                    "Http10CorrelationStage: PostStop with orphaned request — connection terminated abnormally");
                _inFlightRequest = null;
            }

            _pendingResponse?.Dispose();
            _pendingResponse = null;
        }
    }
}