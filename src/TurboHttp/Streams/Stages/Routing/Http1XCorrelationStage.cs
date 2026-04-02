using System.Collections.Immutable;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.Internal;
using TurboHttp.Protocol.Http11;

namespace TurboHttp.Streams.Stages.Routing;

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

internal sealed class Http1XCorrelationStage : GraphStage<Http1XCorrelationShape>
{
    private readonly Inlet<HttpRequestMessage> _inRequest = new("Http1XCorrelation.In.Request");
    private readonly Inlet<HttpResponseMessage> _inResponse = new("Http1XCorrelation.In.Response");
    private readonly Outlet<HttpResponseMessage> _out = new("Http1XCorrelation.Out");
    private readonly Outlet<IOutputItem> _outSignal = new("Http1XCorrelation.Out.Signal");

    internal readonly int MaxPipelineDepth;

    public override Http1XCorrelationShape Shape { get; }

    public Http1XCorrelationStage(int maxPipelineDepth = 8)
    {
        MaxPipelineDepth = maxPipelineDepth;
        Shape = new Http1XCorrelationShape(_inRequest, _inResponse, _out, _outSignal);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private readonly Http1XCorrelationStage _stage;
        private readonly Queue<HttpRequestMessage> _inFlightQueue = new();
        private readonly Queue<IOutputItem> _signalQueue = new();
        private int _effectivePipelineDepth;
        private bool _requestUpstreamFinished;
        private bool _responseUpstreamFinished;

        /// <summary>
        /// Buffers a response when <c>_out</c> is not available (downstream hasn't pulled yet).
        /// Without this, responses pulled from <c>_inResponse</c> by the request handler or
        /// signal handler would crash with "Cannot push port twice" under streaming load.
        /// </summary>
        private HttpResponseMessage? _pendingResponse;

        public Logic(Http1XCorrelationStage stage) : base(stage.Shape)
        {
            _stage = stage;
            _effectivePipelineDepth = stage.MaxPipelineDepth;

            SetHandler(stage._inRequest,
                onPush: () =>
                {
                    var request = Grab(stage._inRequest);
                    _inFlightQueue.Enqueue(request);
                    _signalQueue.Enqueue(StreamAcquireItem.Rent(RequestEndpoint.FromRequest(request)));
                    TryPushSignal();

                    if (_responseUpstreamFinished)
                    {
                        CompleteStage();
                        return;
                    }

                    TryPullResponse();
                    TryPullRequest();
                },
                onUpstreamFinish: () =>
                {
                    _requestUpstreamFinished = true;
                    TryComplete();
                },
                onUpstreamFailure: ex =>
                {
                    Log.Warning("Http1XCorrelationStage: Upstream failure absorbed: {0}", ex.Message);
                    CompleteStage();
                });

            SetHandler(stage._inResponse,
                onPush: () =>
                {
                    var response = Grab(stage._inResponse);
                    var queueCountBeforeDequeue = _inFlightQueue.Count;
                    var request = _inFlightQueue.Dequeue();
                    response.RequestMessage = request;

                    // Detect server pipelining support via Connection: close.
                    // If the server sent Connection: close while we had multiple
                    // pipelined requests in-flight, it doesn't support pipelining.
                    if (HasConnectionClose(response))
                    {
                        if (queueCountBeforeDequeue > 1)
                        {
                            Log.Warning(
                                "Http1XCorrelationStage: Server sent Connection: close with {0} pipelined requests in-flight — disabling pipelining",
                                queueCountBeforeDequeue);
                            _effectivePipelineDepth = 1;
                        }
                    }

                    // Evaluate connection reuse (RFC 9112 §9) and emit signal to transport.
                    // This replaces the former external ConnectionReuseStage, saving 4 graph
                    // stages per substream (ConnectionReuseStage + Select + Buffer + MergePreferred).
                    var endpoint = response.RequestMessage is { RequestUri: not null, Version: not null }
                        ? RequestEndpoint.FromRequest(response.RequestMessage)
                        : RequestEndpoint.Default;
                    var decision = ConnectionReuseEvaluator.Evaluate(response, response.Version);
                    _signalQueue.Enqueue(ConnectionReuseItem.Rent(endpoint, decision));

                    if (IsAvailable(stage._out))
                    {
                        Push(stage._out, response);
                    }
                    else
                    {
                        _pendingResponse = response;
                    }

                    TryPushSignal();
                },
                onUpstreamFinish: () =>
                {
                    _responseUpstreamFinished = true;
                    EmitOrphanedRequests();
                    TryComplete();
                },
                onUpstreamFailure: ex =>
                {
                    Log.Warning("Http1XCorrelationStage: Upstream failure absorbed: {0}", ex.Message);
                    EmitOrphanedRequests();
                    CompleteStage();
                });

            SetHandler(stage._out,
                onPull: () =>
                {
                    // Flush buffered response first
                    if (_pendingResponse is not null)
                    {
                        var resp = _pendingResponse;
                        _pendingResponse = null;
                        Push(stage._out, resp);
                        TryPullResponse();
                        return;
                    }

                    TryPullResponse();
                    TryPullRequest();
                });

            SetHandler(stage._outSignal, onPull: () =>
            {
                TryPushSignal();
            });
        }

        private void TryPushSignal()
        {
            if (_signalQueue.Count == 0 || !IsAvailable(_stage._outSignal))
            {
                return;
            }

            var signal = _signalQueue.Dequeue();
            Push(_stage._outSignal, signal);

            if (signal is PipelineRetryItem)
            {
                EmitNextOrphan();
            }
            else
            {
                TryPullResponse();
                TryPullRequest();
            }
        }

        /// <summary>
        /// Pulls <c>_inResponse</c> only when there are in-flight requests awaiting a response
        /// AND no response is already buffered. This prevents "Cannot push port twice" by
        /// ensuring at most one response is in flight between the inlet and the buffered slot.
        /// </summary>
        private void TryPullResponse()
        {
            if (_pendingResponse is not null)
            {
                return; // Already have a buffered response — wait for downstream to consume it
            }

            if (_inFlightQueue.Count > 0
                && !IsClosed(_stage._inResponse)
                && !HasBeenPulled(_stage._inResponse))
            {
                Pull(_stage._inResponse);
            }
        }

        private void TryPullRequest()
        {
            if (_signalQueue.Count > 0)
            {
                return;
            }
            if (_inFlightQueue.Count < _effectivePipelineDepth
                && !IsClosed(_stage._inRequest)
                && !HasBeenPulled(_stage._inRequest))
            {
                Pull(_stage._inRequest);
            }
        }

        private void TryComplete()
        {
            if (_requestUpstreamFinished && _responseUpstreamFinished && _inFlightQueue.Count == 0)
            {
                CompleteStage();
            }
        }

        /// <summary>
        /// Re-emits orphaned in-flight requests as <see cref="PipelineRetryItem"/> signals
        /// via the <c>OutControl</c> outlet so that upstream layers can re-issue them on a
        /// fresh connection. Called when the response upstream finishes or fails while
        /// requests are still queued.
        /// </summary>
        private void EmitOrphanedRequests()
        {
            if (_inFlightQueue.Count == 0)
            {
                return;
            }

            Log.Warning(
                "Http1XCorrelationStage: Connection closed with {0} orphaned pipelined request(s) — emitting for retry",
                _inFlightQueue.Count);

            // Lower pipeline depth since the server dropped our pipelined requests.
            _effectivePipelineDepth = 1;

            EmitNextOrphan();
        }

        private void EmitNextOrphan()
        {
            if (_inFlightQueue.Count == 0)
            {
                TryComplete();
                return;
            }

            _signalQueue.Enqueue(new PipelineRetryItem(_inFlightQueue.Dequeue()));
            TryPushSignal();
        }

        /// <summary>
        /// Checks whether the response contains a <c>Connection: close</c> header,
        /// indicating the server will close the connection after this response.
        /// </summary>
        public override void PostStop()
        {
            // Return pooled signal items to avoid pool leaks on abnormal termination.
            while (_signalQueue.TryDequeue(out var signal))
            {
                if (signal is ConnectionReuseItem reuseItem) reuseItem.Return();
                else if (signal is StreamAcquireItem acquireItem) acquireItem.Return();
            }

            var orphanCount = _inFlightQueue.Count;
            if (orphanCount > 0)
            {
                Log.Warning(
                    "Http1XCorrelationStage: PostStop with {0} orphaned request(s) — connection terminated abnormally",
                    orphanCount);
                _inFlightQueue.Clear();
            }

            _pendingResponse?.Dispose();
            _pendingResponse = null;
        }

        private static bool HasConnectionClose(HttpResponseMessage response)
        {
            return response.Headers.ConnectionClose == true;
        }
    }
}
