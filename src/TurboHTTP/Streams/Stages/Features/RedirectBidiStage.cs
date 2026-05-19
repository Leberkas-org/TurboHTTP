using System.Diagnostics;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHTTP.Diagnostics;
using TurboHTTP.Protocol.Semantics;
using static Servus.Core.Servus;

namespace TurboHTTP.Streams.Stages.Features;

/// <summary>
/// Bidirectional stage that evaluates redirects on the response path and re-injects
/// redirect requests on the request output without any external feedback loop.
/// <para>
/// Request direction (In1→Out1): forwards requests unchanged. Redirect requests generated
/// internally take priority over new requests from In1.
/// </para>
/// <para>
/// Response direction (In2→Out2): evaluates responses via <see cref="RedirectHandler"/>.
/// Non-redirect responses pass through to Out2. Redirect responses are consumed and a new
/// redirect request is emitted on Out1 (the response is disposed).
/// </para>
/// <para>
/// Each request chain gets its own <see cref="RedirectHandler"/> instance, tracked via
/// <see cref="HttpRequestMessage.Options"/>. This ensures that <c>_visitedUris</c> and
/// <c>_redirectCount</c> are isolated per request chain.
/// </para>
/// <para>
/// Internal state machine per request chain: IDLE → AWAITING_RESPONSE → (REDIRECTING | IDLE).
/// The stage-level state tracks pending redirects; per-chain state is carried in
/// <see cref="HttpRequestMessage.Options"/> via <see cref="RedirectHandlerKey"/>.
/// </para>
/// <para>
/// When no <see cref="RedirectPolicy"/> is provided the stage is a pass-through in both directions.
/// </para>
/// </summary>
internal sealed class RedirectBidiStage
    : GraphStage<BidiShape<HttpRequestMessage, HttpRequestMessage, HttpResponseMessage, HttpResponseMessage>>
{
    internal static readonly HttpRequestOptionsKey<RedirectHandler> RedirectHandlerKey
        = new("TurboHTTP.RedirectHandler");

    private readonly RedirectPolicy? _policy;

    private readonly Inlet<HttpRequestMessage> _inRequest = new("Redirect.In.Request");
    private readonly Outlet<HttpRequestMessage> _outRequest = new("Redirect.Out.Request");
    private readonly Inlet<HttpResponseMessage> _inResponse = new("Redirect.In.Response");
    private readonly Outlet<HttpResponseMessage> _outResponse = new("Redirect.Out.Response");

    public override BidiShape<HttpRequestMessage, HttpRequestMessage, HttpResponseMessage, HttpResponseMessage> Shape
    {
        get;
    }

    /// <summary>
    /// Creates a new <see cref="RedirectBidiStage"/> with the given redirect policy.
    /// </summary>
    /// <param name="policy">Redirect policy. When null, the stage is a pass-through (no redirects).</param>
    public RedirectBidiStage(RedirectPolicy? policy = null)
    {
        _policy = policy;
        Shape = new BidiShape<HttpRequestMessage, HttpRequestMessage, HttpResponseMessage, HttpResponseMessage>(
            _inRequest, _outRequest, _inResponse, _outResponse);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new RedirectBidiLogic(this);


    private sealed class RedirectBidiLogic : GraphStageLogic, IFeatureStageOperations
    {
        private readonly RedirectBidiStage _stage;
        private readonly RedirectStateMachine? _sm;

        public RedirectBidiLogic(RedirectBidiStage stage) : base(stage.Shape)
        {
            _stage = stage;

            if (stage._policy is null)
            {
                SetHandler(stage._inRequest,
                    onPush: () => Push(stage._outRequest, Grab(stage._inRequest)),
                    onUpstreamFinish: () => Complete(stage._outRequest),
                    onUpstreamFailure: ex =>
                    {
                        Log.Warning("RedirectBidiStage: Request upstream failure absorbed: {0}", ex.Message);
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
                        Log.Warning("RedirectBidiStage: Response upstream failure absorbed: {0}", ex.Message);
                        Complete(stage._outResponse);
                    });

                SetHandler(stage._outResponse,
                    onPull: () => Pull(stage._inResponse),
                    onDownstreamFinish: _ => Cancel(stage._inResponse));

                return;
            }

            _sm = new RedirectStateMachine(this, stage._policy);

            SetHandler(stage._inRequest,
                onPush: () =>
                {
                    var request = Grab(stage._inRequest);
                    try
                    {
                        _sm.OnRequest(request);
                    }
                    catch (Exception ex)
                    {
                        Tracing.For("Redirect").Warning(this, "→ redirect request processing failed: {0}", ex.Message);
                        Push(stage._outRequest, request);
                    }
                },
                onUpstreamFinish: () => _sm.OnRequestUpstreamFinish(),
                onUpstreamFailure: ex =>
                {
                    Log.Warning("RedirectBidiStage: Request upstream failure absorbed: {0}", ex.Message);
                    Complete(stage._outRequest);
                });

            SetHandler(stage._outRequest,
                onPull: () =>
                {
                    if (_sm.HasReadyRedirects)
                    {
                        _sm.FlushReadyRedirect();
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
                    try
                    {
                        _sm.OnResponse(response);
                    }
                    catch (Exception ex)
                    {
                        Tracing.For("Redirect").Warning(this, "← redirect response evaluation failed: {0}", ex.Message);
                        Push(stage._outResponse, response);
                    }
                },
                onUpstreamFinish: () =>
                {
                    Complete(stage._outResponse);
                    MaybeComplete();
                },
                onUpstreamFailure: ex =>
                {
                    Log.Warning("RedirectBidiStage: Response upstream failure absorbed: {0}", ex.Message);
                    Complete(stage._outResponse);
                });

            SetHandler(stage._outResponse,
                onPull: () => TryPullResponse(),
                onDownstreamFinish: _ => Cancel(stage._inResponse));
        }

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
            if (_sm!.HasReadyRedirects && IsAvailable(_stage._outRequest))
            {
                _sm.FlushReadyRedirect();
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
        }

        void IFeatureStageOperations.OnCancelTimer(string key)
        {
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

internal sealed class RedirectStateMachine
{
    private readonly IFeatureStageOperations _ops;
    private readonly RedirectPolicy _policy;

    private readonly Queue<HttpRequestMessage> _readyRedirects = new();
    private int _inFlightCount;

    public RedirectStateMachine(IFeatureStageOperations ops, RedirectPolicy policy)
    {
        _ops = ops;
        _policy = policy;
    }

    public bool CanAcceptRequest => _readyRedirects.Count == 0;

    public bool HasReadyRedirects => _readyRedirects.Count > 0;

    public bool IsDrained =>
        _inFlightCount == 0
        && _readyRedirects.Count == 0;

    public void OnRequest(HttpRequestMessage request)
    {
        _inFlightCount++;
        _ops.OnPushRequest(request);
    }

    public void OnResponse(HttpResponseMessage response)
    {
        var original = response.RequestMessage;

        if (original is null || !RedirectHandler.IsRedirect(response))
        {
            _inFlightCount--;
            _ops.OnPushResponse(response);
            return;
        }

        try
        {
            if (!original.Options.TryGetValue(RedirectBidiStage.RedirectHandlerKey, out var handler))
            {
                handler = new RedirectHandler(_policy);
            }

            var newRequest = handler.BuildRedirectRequest(original, response);

            Activity? rootActivity = null;
            if (original.Options.TryGetValue(TurboHttpInstrumentationExtensions.RequestActivityKey,
                    out rootActivity))
            {
                Tracing.AddRedirectEvent(
                    rootActivity, newRequest.RequestUri!, (int)response.StatusCode);
            }

            Metrics.RedirectCount().Add(1,
                new KeyValuePair<string, object?>("http.response.status_code", (int)response.StatusCode));
            Tracing.For("Redirect").Info(_ops, "Redirect followed: {0} → {2} (HTTP {1})",
                original.RequestUri?.OriginalString ?? "",
                (int)response.StatusCode,
                newRequest.RequestUri?.OriginalString ?? "");

            newRequest.Options.Set(RedirectBidiStage.RedirectHandlerKey, handler);

            if (rootActivity is not null)
            {
                newRequest.Options.Set(TurboHttpInstrumentationExtensions.RequestActivityKey, rootActivity);
            }

            response.Dispose();

            _readyRedirects.Enqueue(newRequest);
            _inFlightCount--;
            _ops.OnSignalPullResponse();
            _ops.OnSignalPullRequest();
        }
        catch (RedirectException ex)
        {
            Tracing.For("Redirect").Warning(_ops, "Redirect error: {0} (for {1})", ex.Message, original.RequestUri);
            _inFlightCount--;
            _ops.OnPushResponse(response);
        }
    }

    public void FlushReadyRedirect()
    {
        if (_readyRedirects.Count > 0)
        {
            var request = _readyRedirects.Dequeue();
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

    public void PostStop()
    {
        _readyRedirects.Clear();
    }
}