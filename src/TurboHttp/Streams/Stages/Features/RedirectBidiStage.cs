using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.Diagnostics;
using TurboHttp.Protocol.RFC9110;

namespace TurboHttp.Streams.Stages.Features;

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
        = new("TurboHttp.RedirectHandler");

    private readonly RedirectPolicy? _policy;

    private readonly Inlet<HttpRequestMessage> _inRequest = new("Redirect.In.Request");
    private readonly Outlet<HttpRequestMessage> _outRequest = new("Redirect.Out.Request");
    private readonly Inlet<HttpResponseMessage> _inResponse = new("Redirect.In.Response");
    private readonly Outlet<HttpResponseMessage> _outResponse = new("Redirect.Out.Response");

    public override BidiShape<HttpRequestMessage, HttpRequestMessage, HttpResponseMessage, HttpResponseMessage> Shape { get; }

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
        => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private readonly RedirectBidiStage _stage;

        /// <summary>Queue of redirect requests ready for immediate emission on Out1.</summary>
        private readonly Queue<HttpRequestMessage> _readyRedirects = new();

        /// <summary>Whether Out1 (request output) has downstream demand.</summary>
        private bool _requestDemand;

        /// <summary>Whether Out2 (response output) has downstream demand.</summary>
        private bool _responseDemand;

        /// <summary>
        /// Number of requests emitted on Out1 for which no response has been received on In2 yet.
        /// Prevents premature completion of Out1 when upstream finishes before in-flight responses arrive.
        /// </summary>
        private int _inFlightCount;

        public Logic(RedirectBidiStage stage) : base(stage.Shape)
        {
            _stage = stage;

            if (stage._policy is null)
            {
                // Null policy -> pure pass-through in both directions
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

            // --- Request direction (In1→Out1) ---
            // Redirect requests have priority over new requests from In1.

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
                    // Don't complete Out1 yet if there are pending redirects or in-flight requests
                    TryCompleteIfDone();
                },
                onUpstreamFailure: ex =>
                    {
                        Log.Warning("RedirectBidiStage: Request upstream failure absorbed: {0}", ex.Message);
                        Complete(stage._outRequest);
                    });

            SetHandler(stage._outRequest,
                onPull: () =>
                {
                    _requestDemand = true;
                    // Redirects take priority over new requests
                    if (!TryEmitRedirect())
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
                    _inFlightCount--;

                    // Without the original request context, cannot evaluate redirect — pass through.
                    if (original is null || !RedirectHandler.IsRedirect(response))
                    {
                        // If this was a redirected request, clean up the handler state.
                        if (original is not null
                            && original.Options.TryGetValue(RedirectHandlerKey, out _))
                        {
                            // Handler state cleanup handled by garbage collection
                        }

                        _responseDemand = false;
                        Push(stage._outResponse, response);
                        TryCompleteIfDone();
                        TryPullResponse();
                        return;
                    }

                    try
                    {
                        // Get or create a per-request-chain RedirectHandler via Options
                        if (!original.Options.TryGetValue(RedirectHandlerKey, out var handler))
                        {
                            handler = new RedirectHandler(_stage._policy!);
                        }

                        var newRequest = handler.BuildRedirectRequest(original, response);

                        // Emit a child "TurboHttp.Redirect" span for this hop
                        var previous = Activity.Current;
                        if (original.Options.TryGetValue(TurboHttpInstrumentation.RequestActivityKey, out var rootActivity))
                        {
                            Activity.Current = rootActivity;
                        }

                        var redirectActivity = TurboHttpInstrumentation.StartRedirect(
                            newRequest.RequestUri!, (int)response.StatusCode);
                        redirectActivity?.Stop();
                        Activity.Current = previous;

                        // Record redirect metric + EventSource event
                        TurboHttpMetrics.RedirectCount.Add(1,
                            new KeyValuePair<string, object?>("http.response.status_code", (int)response.StatusCode));
                        TurboHttpEventSource.Log.RedirectFollowed(
                            original.RequestUri?.OriginalString ?? "",
                            (int)response.StatusCode,
                            newRequest.RequestUri?.OriginalString ?? "");

                        // Carry the handler forward with the redirect request
                        newRequest.Options.Set(RedirectHandlerKey, handler);

                        // Carry root activity forward so subsequent stages can parent under it
                        if (rootActivity is not null)
                        {
                            newRequest.Options.Set(TurboHttpInstrumentation.RequestActivityKey, rootActivity);
                        }

                        // Dispose the redirect response — it won't reach the caller
                        response.Dispose();

                        _readyRedirects.Enqueue(newRequest);
                        TryEmitRedirect();

                        // Pull next response (demand still outstanding since we didn't push to Out2)
                        TryPullResponse();
                    }
                    catch (RedirectException ex) when (ex.Error == RedirectError.ProtocolDowngrade)
                    {
                        // HTTPS→HTTP downgrade blocked — forward as final response.
                        // Handler state will be cleaned up by garbage collection.
                        if (original.Options.TryGetValue(RedirectHandlerKey, out _))
                        {
                            // No pending work tracking needed
                        }

                        _responseDemand = false;
                        Push(stage._outResponse, response);
                        TryCompleteIfDone();
                        TryPullResponse();
                    }
                    catch (RedirectException)
                    {
                        // Max redirects exceeded or loop detected — forward as final response.
                        if (original.Options.TryGetValue(RedirectHandlerKey, out _))
                        {
                            // No pending work tracking needed
                        }

                        _responseDemand = false;
                        Push(stage._outResponse, response);
                        TryCompleteIfDone();
                        TryPullResponse();
                    }
                },
                onUpstreamFinish: () =>
                {
                    Complete(stage._outResponse);
                    TryCompleteIfDone();
                },
                onUpstreamFailure: ex =>
                    {
                        Log.Warning("RedirectBidiStage: Response upstream failure absorbed: {0}", ex.Message);
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

        public override void PostStop()
        {
            _readyRedirects.Clear();
        }

        /// <summary>
        /// Attempts to emit a ready redirect request on Out1. Returns true if a redirect was emitted.
        /// </summary>
        private bool TryEmitRedirect()
        {
            if (_requestDemand && _readyRedirects.Count > 0)
            {
                var request = _readyRedirects.Dequeue();
                _requestDemand = false;
                _inFlightCount++;
                Push(_stage._outRequest, request);
                TryCompleteIfDone();
                return true;
            }

            return false;
        }

        /// <summary>
        /// Pulls In1 (request inlet) when Out1 has demand, no ready redirects exist,
        /// and In1 hasn't been pulled yet.
        /// </summary>
        private void TryPullRequest()
        {
            if (_requestDemand
                && _readyRedirects.Count == 0
                && !HasBeenPulled(_stage._inRequest)
                && !IsClosed(_stage._inRequest))
            {
                Pull(_stage._inRequest);
            }
        }

        /// <summary>
        /// Pulls In2 (response inlet) when Out2 has demand and In2 hasn't been pulled yet.
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
        /// Completes Out1 when upstream (In1) is finished, all pending redirects have been drained,
        /// and either all in-flight requests have been resolved or the response upstream (In2) has
        /// closed (no more responses will arrive, so in-flight requests are orphaned).
        /// </summary>
        private void TryCompleteIfDone()
        {
            if (IsClosed(_stage._inRequest) && _readyRedirects.Count == 0
                && (_inFlightCount == 0 || IsClosed(_stage._inResponse))
                && !IsClosed(_stage._outRequest))
            {
                Complete(_stage._outRequest);
            }
        }
    }
}
