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
        => new RetryBidiLogic(this);

    private sealed class RetryBidiLogic : TimerGraphStageLogic, IFeatureStageOperations
    {
        private readonly RetryBidiStage _stage;
        private readonly RetryStateMachine? _sm;

        public RetryBidiLogic(RetryBidiStage stage) : base(stage.Shape)
        {
            _stage = stage;

            if (stage._policy is null)
            {
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

            _sm = new RetryStateMachine(this, stage._policy);

            SetHandler(stage._inRequest,
                onPush: () =>
                {
                    var request = Grab(stage._inRequest);
                    _sm.OnRequest(request);
                },
                onUpstreamFinish: () => _sm.OnRequestUpstreamFinish(),
                onUpstreamFailure: ex =>
                {
                    Log.Warning("RetryBidiStage: Request upstream failure absorbed: {0}", ex.Message);
                    Complete(stage._outRequest);
                });

            SetHandler(stage._outRequest,
                onPull: () =>
                {
                    if (_sm.HasReadyRetries)
                    {
                        _sm.FlushReadyRetry();
                    }
                    else
                    {
                        TryPullRequest();
                    }
                },
                onDownstreamFinish: _ => Cancel(stage._inRequest));

            SetHandler(stage._inResponse,
                onPush: () =>
                {
                    var response = Grab(stage._inResponse);
                    _sm.OnResponse(response);
                },
                onUpstreamFinish: () =>
                {
                    Complete(stage._outResponse);
                    MaybeComplete();
                },
                onUpstreamFailure: ex =>
                {
                    Log.Warning("RetryBidiStage: Response upstream failure absorbed: {0}", ex.Message);
                    Complete(stage._outResponse);
                });

            SetHandler(stage._outResponse,
                onPull: () => TryPullResponse(),
                onDownstreamFinish: _ => Cancel(stage._inResponse));
        }

        protected override void OnTimer(object timerKey) => _sm?.OnTimer(timerKey);

        public override void PostStop() => _sm?.PostStop();

        void IFeatureStageOperations.OnPushRequest(HttpRequestMessage request)
        {
            Push(_stage._outRequest, request);
            TryPullRequest();
        }

        void IFeatureStageOperations.OnPushResponse(HttpResponseMessage response)
        {
            Push(_stage._outResponse, response);
            TryPullResponse();
            MaybeComplete();
        }

        void IFeatureStageOperations.OnSignalPullRequest()
        {
            if (_sm!.HasReadyRetries && IsAvailable(_stage._outRequest))
            {
                _sm.FlushReadyRetry();
            }
            else
            {
                TryPullRequest();
            }
        }

        void IFeatureStageOperations.OnSignalPullResponse()
        {
            TryPullResponse();
        }

        void IFeatureStageOperations.OnCompleteStage()
        {
            Complete(_stage._outRequest);
        }

        void IFeatureStageOperations.OnScheduleTimer(string key, TimeSpan delay)
        {
            ScheduleOnce(key, delay);
        }

        void IFeatureStageOperations.OnCancelTimer(string key)
        {
            CancelTimer(key);
        }

        ILoggingAdapter IFeatureStageOperations.Log => Log;

        private void TryPullRequest()
        {
            if (IsAvailable(_stage._outRequest)
                && _sm!.CanAcceptRequest
                && !HasBeenPulled(_stage._inRequest)
                && !IsClosed(_stage._inRequest))
            {
                Pull(_stage._inRequest);
            }
        }

        private void TryPullResponse()
        {
            if (!HasBeenPulled(_stage._inResponse)
                && !IsClosed(_stage._inResponse))
            {
                Pull(_stage._inResponse);
            }
        }

        private void MaybeComplete()
        {
            if (_sm!.IsDrained
                && !IsClosed(_stage._outRequest)
                && (IsClosed(_stage._inRequest) || IsClosed(_stage._inResponse)))
            {
                Complete(_stage._outRequest);
            }
        }
    }
}

internal sealed class RetryStateMachine
{
    private static readonly HttpRequestOptionsKey<int> AttemptCountKey = new("TurboHTTP.RetryAttemptCount");

    private readonly IFeatureStageOperations _ops;
    private readonly RetryPolicy _policy;

    private readonly Queue<HttpRequestMessage> _readyRetries = new();
    private readonly Dictionary<string, HttpRequestMessage> _waitingRetries = new();
    private long _retryIdCounter;
    private int _inFlightCount;

    public RetryStateMachine(IFeatureStageOperations ops, RetryPolicy policy)
    {
        _ops = ops;
        _policy = policy;
    }

    public bool CanAcceptRequest =>
        _readyRetries.Count == 0
        && _readyRetries.Count + _waitingRetries.Count < RetryBidiStage.MaxPendingRetries;

    public bool HasReadyRetries => _readyRetries.Count > 0;

    public bool IsDrained =>
        _inFlightCount == 0
        && _readyRetries.Count == 0
        && _waitingRetries.Count == 0;

    public void OnRequest(HttpRequestMessage request)
    {
        _inFlightCount++;
        _ops.OnPushRequest(request);
    }

    public void OnResponse(HttpResponseMessage response)
    {
        var original = response.RequestMessage;

        if (original is null)
        {
            _inFlightCount--;
            _ops.OnPushResponse(response);
            return;
        }

        var attemptCount = original.Options.TryGetValue(AttemptCountKey, out var count) ? count : 1;

        var decision = RetryEvaluator.Evaluate(
            original,
            response,
            networkFailure: false,
            bodyPartiallyConsumed: false,
            attemptCount: attemptCount,
            policy: _policy);

        if (!decision.ShouldRetry)
        {
            _inFlightCount--;
            _ops.OnPushResponse(response);
            return;
        }

        EmitRetryTelemetry(original, attemptCount);

        response.Dispose();
        original.Options.Set(AttemptCountKey, attemptCount + 1);

        _inFlightCount--;

        if (decision.RetryAfterDelay.HasValue && decision.RetryAfterDelay.Value > TimeSpan.Zero)
        {
            var timerId = $"retry-{_retryIdCounter++}";
            _waitingRetries[timerId] = original;
            _ops.OnScheduleTimer(timerId, decision.RetryAfterDelay.Value);
        }
        else
        {
            _readyRetries.Enqueue(original);
        }

        _ops.OnSignalPullResponse();
        _ops.OnSignalPullRequest();
    }

    public void FlushReadyRetry()
    {
        if (_readyRetries.Count > 0)
        {
            var request = _readyRetries.Dequeue();
            _inFlightCount++;
            _ops.OnPushRequest(request);
        }
    }

    public void OnRequestUpstreamFinish()
    {
        if (IsDrained)
        {
            _ops.OnCompleteStage();
        }
    }

    public void OnTimer(object timerKey)
    {
        var key = (string)timerKey;
        if (_waitingRetries.Remove(key, out var request))
        {
            _readyRetries.Enqueue(request);
            _ops.OnSignalPullRequest();
        }
    }

    public void PostStop()
    {
        _readyRetries.Clear();
        _waitingRetries.Clear();
    }

    private void EmitRetryTelemetry(HttpRequestMessage original, int attemptCount)
    {
        if (original.Options.TryGetValue(TurboHttpInstrumentation.RequestActivityKey, out var rootActivity))
        {
            TurboHttpInstrumentation.AddRetryEvent(rootActivity, attemptCount);
        }

        TurboHttpMetrics.RetryCount.Add(1,
            new KeyValuePair<string, object?>("http.request.method", original.Method.Method),
            new KeyValuePair<string, object?>("server.address", original.RequestUri?.Host ?? "unknown"));
        TurboTrace.Retry.Warning(_ops, "Retry attempt: {0} {1} (attempt {2})",
            original.Method.Method,
            original.RequestUri?.OriginalString ?? "",
            attemptCount + 1);
    }
}