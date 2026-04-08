using System.Diagnostics;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHTTP.Diagnostics;
using TurboHTTP.Protocol.Semantics;

namespace TurboHTTP.Streams.Stages.Features;

/// <summary>
/// Bidirectional stage that evaluates retry decisions on the response path and
/// re-injects retry requests on the request output without any external feedback loop.
/// <para>
/// Request direction (In1→Out1): forwards requests and buffers the current in-flight
/// request for potential retry. Retry requests take priority over new requests from In1.
/// </para>
/// <para>
/// Response direction (In2→Out2): evaluates responses via <see cref="RetryEvaluator"/>.
/// Non-retryable responses pass through to Out2. Retryable responses are disposed and
/// the original request is re-injected on Out1 (immediately or after a Retry-After delay).
/// </para>
/// <para>
/// When no <see cref="RetryPolicy"/> is provided the stage is a pass-through in both directions.
/// </para>
/// </summary>
internal sealed class RetryBidiStage
    : GraphStage<BidiShape<HttpRequestMessage, HttpRequestMessage, HttpResponseMessage, HttpResponseMessage>>
{
    private readonly RetryPolicy? _policy;

    private readonly Inlet<HttpRequestMessage> _inRequest = new("Retry.In.Request");
    private readonly Outlet<HttpRequestMessage> _outRequest = new("Retry.Out.Request");
    private readonly Inlet<HttpResponseMessage> _inResponse = new("Retry.In.Response");
    private readonly Outlet<HttpResponseMessage> _outResponse = new("Retry.Out.Response");

    /// <summary>
    /// Maximum number of pending retries (ready + waiting) before the stage stops accepting
    /// new requests. Provides backpressure when retries accumulate.
    /// </summary>
    internal const int MaxPendingRetries = 16;

    public override BidiShape<HttpRequestMessage, HttpRequestMessage, HttpResponseMessage, HttpResponseMessage> Shape
    {
        get;
    }

    /// <summary>
    /// Creates a new <see cref="RetryBidiStage"/> with the given retry policy.
    /// </summary>
    /// <param name="policy">Retry policy. When null, the stage is a pass-through (no retries).</param>
    public RetryBidiStage(RetryPolicy? policy = null)
    {
        _policy = policy;
        Shape = new BidiShape<HttpRequestMessage, HttpRequestMessage, HttpResponseMessage, HttpResponseMessage>(
            _inRequest, _outRequest, _inResponse, _outResponse);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : TimerGraphStageLogic
    {
        private static readonly HttpRequestOptionsKey<int> AttemptCountKey = new("TurboHTTP.RetryAttemptCount");

        private readonly RetryBidiStage _stage;

        /// <summary>Queue of retry requests ready for immediate emission on Out1.</summary>
        private readonly Queue<HttpRequestMessage> _readyRetries = new();

        /// <summary>Retry requests waiting for a Retry-After timer to fire.</summary>
        private readonly Dictionary<string, HttpRequestMessage> _waitingRetries = new();

        private long _retryIdCounter;

        /// <summary>Whether Out1 (request output) has downstream demand.</summary>
        private bool _requestDemand;

        /// <summary>Whether Out2 (response output) has downstream demand.</summary>
        private bool _responseDemand;

        /// <summary>
        /// Number of requests emitted on Out1 for which no response has been received on In2 yet.
        /// Prevents premature completion of Out1 when upstream finishes before in-flight responses arrive.
        /// </summary>
        private int _inFlightCount;

        /// <summary>
        /// Guards the retry transaction (evaluate → enqueue → emit → decrement) so that
        /// <see cref="TryCompleteIfDone"/> cannot fire mid-decision and close the outlet
        /// prematurely. (DL-009 atomic transaction guard)
        /// </summary>
        private bool _retryTransactionActive;

        public Logic(RetryBidiStage stage) : base(stage.Shape)
        {
            _stage = stage;

            if (stage._policy is null)
            {
                // Null policy → pure pass-through in both directions
                SetHandler(stage._inRequest,
                    onPush: () => Push(stage._outRequest, Grab(stage._inRequest)),
                    onUpstreamFinish: () => Complete(stage._outRequest),
                    onUpstreamFailure: ex =>
                    {
                        Log.Warning("RetryBidiStage: Request upstream failure absorbed: {0}", ex.Message);
                        Complete(stage._outRequest);
                    });

                SetHandler(stage._outRequest,
                    onPull: () => Pull(stage._inRequest),
                    onDownstreamFinish: _ => Cancel(stage._inRequest));

                SetHandler(stage._inResponse,
                    onPush: () => Push(stage._outResponse, Grab(stage._inResponse)),
                    onUpstreamFinish: () => Complete(stage._outResponse),
                    onUpstreamFailure: ex =>
                    {
                        Log.Warning("RetryBidiStage: Response upstream failure absorbed: {0}", ex.Message);
                        Complete(stage._outResponse);
                    });

                SetHandler(stage._outResponse,
                    onPull: () => Pull(stage._inResponse),
                    onDownstreamFinish: _ => Cancel(stage._inResponse));

                return;
            }

            // --- Request direction (In1→Out1) ---
            // Retry requests have priority over new requests from In1.

            SetHandler(stage._inRequest,
                onPush: () =>
                {
                    var request = Grab(stage._inRequest);
                    _requestDemand = false;
                    _inFlightCount++;
                    Push(stage._outRequest, request);
                },
                onUpstreamFinish: () =>
                {
                    // Don't complete Out1 yet if there are pending retries or in-flight requests
                    TryCompleteIfDone();
                },
                onUpstreamFailure: ex =>
                {
                    Log.Warning("RetryBidiStage: Request upstream failure absorbed: {0}", ex.Message);
                    Complete(stage._outRequest);
                });

            SetHandler(stage._outRequest,
                onPull: () =>
                {
                    _requestDemand = true;
                    // Retries take priority over new requests
                    if (!TryEmitRetry())
                    {
                        TryPullRequest();
                    }
                },
                onDownstreamFinish: _ => Cancel(stage._inRequest));

            // --- Response direction (In2→Out2) ---

            SetHandler(stage._inResponse,
                onPush: () =>
                {
                    var response = Grab(stage._inResponse);
                    var original = response.RequestMessage;

                    // Without the original request, cannot determine idempotency — pass through.
                    if (original is null)
                    {
                        _inFlightCount--;
                        _responseDemand = false;
                        Push(stage._outResponse, response);
                        TryCompleteIfDone();
                        TryPullResponse();
                        return;
                    }

                    var attemptCount = original.Options.TryGetValue(AttemptCountKey, out var count) ? count : 1;

                    var decision = RetryEvaluator.Evaluate(
                        original,
                        response,
                        networkFailure: false,
                        bodyPartiallyConsumed: false,
                        attemptCount: attemptCount,
                        policy: _stage._policy!);

                    if (!decision.ShouldRetry)
                    {
                        _inFlightCount--;
                        _responseDemand = false;
                        Push(stage._outResponse, response);
                        TryCompleteIfDone();
                        TryPullResponse();
                        return;
                    }

                    // Emit a child "TurboHTTP.Retry" span for this attempt
                    var previous = Activity.Current;
                    if (original.Options.TryGetValue(TurboHttpInstrumentation.RequestActivityKey, out var rootActivity))
                    {
                        Activity.Current = rootActivity;
                    }

                    var retryActivity = TurboHttpInstrumentation.StartRetry(attemptCount);
                    retryActivity?.Stop();
                    Activity.Current = previous;

                    // Record retry metric + trace event
                    TurboHttpMetrics.RetryCount.Add(1,
                        new KeyValuePair<string, object?>("http.request.method", original.Method.Method),
                        new KeyValuePair<string, object?>("server.address", original.RequestUri?.Host ?? "unknown"));
                    TurboTrace.Retry.Warning(this, "Retry attempt: {0} {1} (attempt {2})",
                        original.Method.Method,
                        original.RequestUri?.OriginalString ?? "",
                        attemptCount + 1);

                    // Retryable — dispose the response and enqueue the original request for retry.
                    // The entire retry decision (evaluate → enqueue → emit → decrement) is treated
                    // as an atomic transaction so that TryCompleteIfDone() cannot fire mid-decision
                    // and close the outlet prematurely. (DL-009 atomic transaction guard)
                    _retryTransactionActive = true;

                    response.Dispose();
                    original.Options.Set(AttemptCountKey, attemptCount + 1);

                    if (decision.RetryAfterDelay.HasValue && decision.RetryAfterDelay.Value > TimeSpan.Zero)
                    {
                        var timerId = $"retry-{_retryIdCounter++}";
                        _waitingRetries[timerId] = original;
                        ScheduleOnce(timerId, decision.RetryAfterDelay.Value);
                    }
                    else
                    {
                        _readyRetries.Enqueue(original);
                        TryEmitRetry();
                    }

                    _inFlightCount--;
                    TryPullResponse();

                    _retryTransactionActive = false;
                    TryCompleteIfDone();
                },
                onUpstreamFinish: () =>
                {
                    Complete(stage._outResponse);
                    TryCompleteIfDone();
                },
                onUpstreamFailure: ex =>
                {
                    Log.Warning("RetryBidiStage: Response upstream failure absorbed: {0}", ex.Message);
                    Complete(stage._outResponse);
                });

            SetHandler(stage._outResponse,
                onPull: () =>
                {
                    _responseDemand = true;
                    TryPullResponse();
                },
                onDownstreamFinish: _ => Cancel(stage._inResponse));
        }

        protected override void OnTimer(object timerKey)
        {
            var key = (string)timerKey;
            if (_waitingRetries.Remove(key, out var request))
            {
                _retryTransactionActive = true;

                _readyRetries.Enqueue(request);
                TryEmitRetry();

                _retryTransactionActive = false;
                TryCompleteIfDone();
            }
        }

        public override void PostStop()
        {
            _readyRetries.Clear();
            _waitingRetries.Clear();
        }

        /// <summary>
        /// Attempts to emit a ready retry request on Out1. Returns true if a retry was emitted.
        /// </summary>
        private bool TryEmitRetry()
        {
            if (_requestDemand && _readyRetries.Count > 0)
            {
                var request = _readyRetries.Dequeue();
                _requestDemand = false;
                _inFlightCount++;
                Push(_stage._outRequest, request);
                TryCompleteIfDone();
                return true;
            }

            return false;
        }

        /// <summary>
        /// Pulls In1 (request inlet) when Out1 has demand, no ready retries exist,
        /// and the pending retry count is below the limit.
        /// </summary>
        private void TryPullRequest()
        {
            if (_requestDemand
                && _readyRetries.Count == 0
                && !HasBeenPulled(_stage._inRequest)
                && !IsClosed(_stage._inRequest)
                && _readyRetries.Count + _waitingRetries.Count < MaxPendingRetries)
            {
                Pull(_stage._inRequest);
            }
        }

        /// <summary>
        /// Pulls In2 (response inlet) when Out2 has demand.
        /// </summary>
        private void TryPullResponse()
        {
            if (_responseDemand
                && !HasBeenPulled(_stage._inResponse)
                && !IsClosed(_stage._inResponse))
            {
                Pull(_stage._inResponse);
            }
        }

        /// <summary>
        /// Completes Out1 when upstream (In1) is finished, all pending retries have been drained,
        /// and either all in-flight requests have been resolved or the response upstream (In2) has
        /// closed (no more responses will arrive, so in-flight requests are orphaned).
        /// </summary>
        private void TryCompleteIfDone()
        {
            if (_retryTransactionActive)
            {
                return;
            }

            if (IsClosed(_stage._outRequest))
            {
                return;
            }

            // Case 1: Response upstream closed — no more responses will arrive,
            // so in-flight requests are orphaned and pending retries cannot complete.
            if (IsClosed(_stage._inResponse)
                && _readyRetries.Count == 0
                && _waitingRetries.Count == 0)
            {
                Complete(_stage._outRequest);
                return;
            }

            // Case 2: Request upstream closed, no pending retries, and all in-flight resolved.
            if (IsClosed(_stage._inRequest)
                && _readyRetries.Count == 0
                && _waitingRetries.Count == 0
                && _inFlightCount == 0)
            {
                Complete(_stage._outRequest);
            }
        }
    }
}