using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHTTP.Internal;
using TurboHTTP.Protocol.Http11;

namespace TurboHTTP.Streams.Stages.Routing;

/// <summary>
/// Request-response correlation for HTTP/1.1 (RFC 9112) with pipelining support.
/// <para>
/// Key difference from the former <see cref="Http1XCorrelationStage"/>:
/// request pulling is <b>decoupled</b> from signal emission. The old stage
/// blocked <c>TryPullRequest()</c> whenever the signal queue was non-empty,
/// serializing request acceptance behind signal delivery and halving throughput
/// under pipelining. This stage pulls requests eagerly up to
/// <c>_effectivePipelineDepth</c> regardless of pending signals.
/// </para>
/// <para>
/// Ordering guarantee: <see cref="Akka.Streams.Dsl.MergePreferred{T}"/> in the
/// engine graph ensures signals on the Preferred inlet are emitted ahead of
/// encoder data on the regular inlet, so transport sees StreamAcquireItem
/// before the corresponding NetworkBuffer even without a signal gate.
/// </para>
/// </summary>
internal sealed class Http11CorrelationStage : GraphStage<Http1XCorrelationShape>
{
    private readonly Inlet<HttpRequestMessage> _inRequest = new("Http11Correlation.In.Request");
    private readonly Inlet<HttpResponseMessage> _inResponse = new("Http11Correlation.In.Response");
    private readonly Outlet<HttpResponseMessage> _out = new("Http11Correlation.Out");
    private readonly Outlet<IOutputItem> _outSignal = new("Http11Correlation.Out.Signal");

    internal readonly int MaxPipelineDepth;

    public override Http1XCorrelationShape Shape { get; }

    public Http11CorrelationStage(int maxPipelineDepth = 8)
    {
        MaxPipelineDepth = maxPipelineDepth;
        Shape = new Http1XCorrelationShape(_inRequest, _inResponse, _out, _outSignal);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private readonly Http11CorrelationStage _stage;
        private readonly Queue<HttpRequestMessage> _inFlightQueue = new();
        private readonly Queue<IOutputItem> _signalQueue = new();
        private int _effectivePipelineDepth;
        private bool _requestUpstreamFinished;
        private bool _responseUpstreamFinished;
        private HttpResponseMessage? _pendingResponse;

        public Logic(Http11CorrelationStage stage) : base(stage.Shape)
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
                    Log.Warning("Http11CorrelationStage: Upstream failure absorbed: {0}", ex.Message);
                    CompleteStage();
                });

            SetHandler(stage._inResponse,
                onPush: () =>
                {
                    var response = Grab(stage._inResponse);
                    var queueCountBeforeDequeue = _inFlightQueue.Count;
                    var request = _inFlightQueue.Dequeue();
                    response.RequestMessage = request;

                    if (HasConnectionClose(response))
                    {
                        if (queueCountBeforeDequeue > 1)
                        {
                            Log.Warning(
                                "Http11CorrelationStage: Server sent Connection: close with {0} pipelined requests in-flight — disabling pipelining",
                                queueCountBeforeDequeue);
                            _effectivePipelineDepth = 1;
                        }
                    }

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
                    // Key change: pull next request immediately — do NOT wait for
                    // signal queue to drain. MergePreferred guarantees ordering.
                    TryPullRequest();
                },
                onUpstreamFinish: () =>
                {
                    _responseUpstreamFinished = true;
                    EmitOrphanedRequests();
                    TryComplete();
                },
                onUpstreamFailure: ex =>
                {
                    Log.Warning("Http11CorrelationStage: Upstream failure absorbed: {0}", ex.Message);
                    EmitOrphanedRequests();
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

        private void TryPullResponse()
        {
            if (_pendingResponse is not null)
            {
                return;
            }

            if (_inFlightQueue.Count > 0
                && !IsClosed(_stage._inResponse)
                && !HasBeenPulled(_stage._inResponse))
            {
                Pull(_stage._inResponse);
            }
        }

        /// <summary>
        /// Pulls the next request if pipeline depth allows it.
        /// Unlike <see cref="Http1XCorrelationStage"/>, this does NOT check
        /// <c>_signalQueue.Count</c> — signals flow independently through
        /// <c>OutControl</c> via <c>MergePreferred</c>.
        /// </summary>
        private void TryPullRequest()
        {
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

        private void EmitOrphanedRequests()
        {
            if (_inFlightQueue.Count == 0)
            {
                return;
            }

            Log.Warning(
                "Http11CorrelationStage: Connection closed with {0} orphaned pipelined request(s) — emitting for retry",
                _inFlightQueue.Count);

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

        public override void PostStop()
        {
            while (_signalQueue.TryDequeue(out var signal))
            {
                if (signal is ConnectionReuseItem reuseItem) reuseItem.Return();
                else if (signal is StreamAcquireItem acquireItem) acquireItem.Return();
            }

            var orphanCount = _inFlightQueue.Count;
            if (orphanCount > 0)
            {
                Log.Warning(
                    "Http11CorrelationStage: PostStop with {0} orphaned request(s) — connection terminated abnormally",
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
