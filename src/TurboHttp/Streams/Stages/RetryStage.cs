using System;
using System.Net.Http;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.Protocol;

namespace TurboHttp.Streams.Stages;

/// <summary>
/// RFC 9110 §9.2 — Intercepts responses that should be retried (408/503 or network failure)
/// and emits the original request on <see cref="FanOutShape{TIn,TOut0,TOut1}.Out1"/> for
/// re-injection into the HTTP engine, while forwarding final (non-retryable) responses
/// on <see cref="FanOutShape{TIn,TOut0,TOut1}.Out0"/>.
/// <para>
/// Only idempotent methods (GET, HEAD, PUT, DELETE, OPTIONS, TRACE) are retried.
/// Retry-After delays from 408/503 responses are honoured via a timer before re-emission.
/// </para>
/// <para>
/// Both downstream outlets must have demand before the stage pulls the inlet,
/// matching the same demand contract used by <see cref="RedirectStage"/>.
/// </para>
/// </summary>
internal sealed class RetryStage
    : GraphStage<FanOutShape<HttpResponseMessage, HttpResponseMessage, HttpRequestMessage>>
{
    private readonly RetryPolicy _policy;

    private readonly Inlet<HttpResponseMessage> _in
        = new("retry.in");

    private readonly Outlet<HttpResponseMessage> _outFinal
        = new("retry.out0.final");

    private readonly Outlet<HttpRequestMessage> _outRetry
        = new("retry.out1.retry");

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
        private readonly RetryStage _stage;
        private bool _finalHasDemand;
        private bool _retryHasDemand;
        private int _attemptCount = 1;
        private HttpRequestMessage? _pendingRetryRequest;

        private const string RetryTimerKey = "retry-timer";

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
                        _finalHasDemand = false;
                        Push(stage._outFinal, response);
                        return;
                    }

                    var decision = RetryEvaluator.Evaluate(
                        original,
                        response,
                        networkFailure: false,
                        bodyPartiallyConsumed: false,
                        attemptCount: _attemptCount,
                        policy: _stage._policy);

                    if (!decision.ShouldRetry)
                    {
                        _finalHasDemand = false;
                        Push(stage._outFinal, response);
                        return;
                    }

                    _attemptCount++;
                    _pendingRetryRequest = original;

                    if (decision.RetryAfterDelay.HasValue && decision.RetryAfterDelay.Value > TimeSpan.Zero)
                    {
                        // Honour the Retry-After delay before re-emitting the request.
                        ScheduleOnce(RetryTimerKey, decision.RetryAfterDelay.Value);
                    }
                    else
                    {
                        EmitRetry();
                    }
                },
                onUpstreamFinish: CompleteStage,
                onUpstreamFailure: FailStage);

            SetHandler(stage._outFinal,
                onPull: () =>
                {
                    _finalHasDemand = true;
                    TryPullInlet();
                },
                onDownstreamFinish: _ => CompleteStage());

            SetHandler(stage._outRetry,
                onPull: () =>
                {
                    _retryHasDemand = true;
                    // A pending retry request may be waiting for demand (e.g. after a timer fired
                    // before downstream re-requested).
                    if (_pendingRetryRequest is not null)
                    {
                        EmitRetry();
                        return;
                    }

                    TryPullInlet();
                },
                onDownstreamFinish: _ => CompleteStage());
        }

        protected override void OnTimer(object timerKey)
        {
            // Timer fired after a Retry-After delay; emit the buffered retry request if
            // downstream has demand, otherwise let onPull handle it when demand arrives.
            if (_pendingRetryRequest is not null && _retryHasDemand)
            {
                EmitRetry();
            }
        }

        private void EmitRetry()
        {
            var request = _pendingRetryRequest!;
            _pendingRetryRequest = null;
            _retryHasDemand = false;
            Push(_stage._outRetry, request);
        }

        private void TryPullInlet()
        {
            if (_finalHasDemand && _retryHasDemand && !HasBeenPulled(_stage._in))
            {
                Pull(_stage._in);
            }
        }
    }
}
