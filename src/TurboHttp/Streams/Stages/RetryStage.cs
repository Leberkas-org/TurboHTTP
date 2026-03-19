using System;
using System.Collections.Generic;
using System.Net.Http;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.Protocol.RFC9110;

namespace TurboHttp.Streams.Stages;

/// <summary>
/// RFC 9110 §9.2 — Intercepts responses that should be retried (408/503 or network failure)
/// and emits the original request on <see cref="FanOutShape{TIn,TOut0,TOut1}.Out1"/> for
/// re-injection into the HTTP engine, while forwarding final (non-retryable) responses
/// on <see cref="FanOutShape{TIn,TOut0,TOut1}.Out0"/>.
/// <para>
/// Only idempotent methods (GET, HEAD, PUT, DELETE, OPTIONS, TRACE) are retried.
/// Retry-After delays from 408/503 responses are honoured via independent timers.
/// </para>
/// <para>
/// This stage supports concurrent processing: final responses flow independently of
/// pending retries, and multiple Retry-After timers can run in parallel. The inlet is
/// pulled when the final outlet has demand and the pending retry count is below
/// <see cref="MaxPendingRetries"/>, without requiring retry-outlet demand.
/// </para>
/// </summary>
internal sealed class RetryStage : GraphStage<FanOutShape<HttpResponseMessage, HttpResponseMessage, HttpRequestMessage>>
{
    private readonly RetryPolicy _policy;

    private readonly Inlet<HttpResponseMessage> _in
        = new("retry.in");

    private readonly Outlet<HttpResponseMessage> _outFinal
        = new("retry.out0.final");

    private readonly Outlet<HttpRequestMessage> _outRetry
        = new("retry.out1.retry");

    /// <summary>
    /// Maximum number of pending retries (ready + waiting) before the stage stops pulling
    /// the inlet. Provides backpressure when the retry feedback loop is slow.
    /// </summary>
    internal const int MaxPendingRetries = 16;

    public override FanOutShape<HttpResponseMessage, HttpResponseMessage, HttpRequestMessage> Shape { get; }

    /// <summary>
    /// Creates a new <see cref="RetryStage"/> with the given retry policy.
    /// </summary>
    /// <param name="policy">Retry policy. Defaults to <see cref="RetryPolicy.Default"/> when null.</param>
    public RetryStage(RetryPolicy? policy = null)
    {
        _policy = policy ?? RetryPolicy.Default;
        Shape = new FanOutShape<HttpResponseMessage, HttpResponseMessage, HttpRequestMessage>(
            _in, _outFinal, _outRetry);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : TimerGraphStageLogic
    {
        private static readonly HttpRequestOptionsKey<int> AttemptCountKey = new("TurboHttp.RetryAttemptCount");

        private readonly RetryStage _stage;
        private readonly Queue<HttpRequestMessage> _readyRetries = new();
        private readonly Dictionary<string, HttpRequestMessage> _waitingRetries = new();
        private long _retryIdCounter;
        private bool _finalDemand;
        private bool _retryDemand;
        private bool _upstreamFinished;

        public Logic(RetryStage stage) : base(stage.Shape)
        {
            _stage = stage;

            SetHandler(stage._in,
                onPush: () =>
                {
                    var response = Grab(stage._in);
                    var original = response.RequestMessage;

                    // Without the original request context the evaluator cannot determine
                    // idempotency or build a retry request — pass through as final.
                    if (original is null)
                    {
                        _finalDemand = false;
                        Push(stage._outFinal, response);
                        TryPullInlet();
                        return;
                    }

                    var attemptCount = original.Options.TryGetValue(AttemptCountKey, out var count) ? count : 1;

                    var decision = RetryEvaluator.Evaluate(
                        original,
                        response,
                        networkFailure: false,
                        bodyPartiallyConsumed: false,
                        attemptCount: attemptCount,
                        policy: _stage._policy);

                    if (!decision.ShouldRetry)
                    {
                        _finalDemand = false;
                        Push(stage._outFinal, response);
                        TryPullInlet();
                        return;
                    }

                    // Retryable response — dispose it (not forwarded to any outlet).
                    response.Dispose();

                    original.Options.Set(AttemptCountKey, attemptCount + 1);

                    if (decision.RetryAfterDelay.HasValue && decision.RetryAfterDelay.Value > TimeSpan.Zero)
                    {
                        // Schedule a timer for the Retry-After delay.
                        var timerId = $"retry-{_retryIdCounter++}";
                        _waitingRetries[timerId] = original;
                        ScheduleOnce(timerId, decision.RetryAfterDelay.Value);
                    }
                    else
                    {
                        // Immediate retry.
                        _readyRetries.Enqueue(original);
                        TryEmitRetry();
                    }

                    TryPullInlet();
                },
                onUpstreamFinish: () =>
                {
                    if (_readyRetries.Count == 0 && _waitingRetries.Count == 0)
                    {
                        CompleteStage();
                    }
                    else
                    {
                        _upstreamFinished = true;
                    }
                },
                onUpstreamFailure: FailStage);

            SetHandler(stage._outFinal,
                onPull: () =>
                {
                    _finalDemand = true;
                    TryPullInlet();
                },
                onDownstreamFinish: _ => CompleteStage());

            SetHandler(stage._outRetry,
                onPull: () =>
                {
                    _retryDemand = true;
                    TryEmitRetry();
                    TryPullInlet();
                },
                onDownstreamFinish: _ => CompleteStage());
        }

        protected override void OnTimer(object timerKey)
        {
            var key = (string)timerKey;
            if (_waitingRetries.Remove(key, out var request))
            {
                _readyRetries.Enqueue(request);
                TryEmitRetry();
                TryPullInlet();
            }
        }

        public override void PostStop()
        {
            _readyRetries.Clear();
            _waitingRetries.Clear();
        }

        private void TryEmitRetry()
        {
            if (_retryDemand && _readyRetries.Count > 0)
            {
                var request = _readyRetries.Dequeue();
                _retryDemand = false;
                Push(_stage._outRetry, request);
                CheckDrainComplete();
            }
        }

        private void TryPullInlet()
        {
            if (_finalDemand
                && !HasBeenPulled(_stage._in)
                && !_upstreamFinished
                && _readyRetries.Count + _waitingRetries.Count < MaxPendingRetries)
            {
                Pull(_stage._in);
            }
        }

        private void CheckDrainComplete()
        {
            if (_upstreamFinished && _readyRetries.Count == 0 && _waitingRetries.Count == 0)
            {
                CompleteStage();
            }
        }
    }
}
