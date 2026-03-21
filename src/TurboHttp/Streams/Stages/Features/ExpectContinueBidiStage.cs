using System.Net;
using System.Net.Http;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.Protocol.RFC9110;

namespace TurboHttp.Streams.Stages.Features;

/// <summary>
/// Bidirectional stage that automatically adds <c>Expect: 100-continue</c> for requests
/// whose body size meets or exceeds a configurable threshold (RFC 9110 §10.1.1).
/// <para>
/// <b>Request direction (In1→Out1):</b> Requests with a body at or above the threshold
/// get the <c>Expect: 100-continue</c> header added. The full request (including body)
/// is forwarded downstream. Requests with no body or a body below the threshold pass
/// through unchanged.
/// </para>
/// <para>
/// <b>Response direction (In2→Out2):</b>
/// <list type="bullet">
///   <item><description>
///     <b>100 Continue</b> — consumed silently (body release signal); the stage pulls the
///     next response (the final one) from In2.
///   </description></item>
///   <item><description>
///     <b>417 Expectation Failed</b> — forwarded to the caller. The request is considered aborted.
///   </description></item>
///   <item><description>
///     <b>Other status codes</b> — forwarded to the caller unchanged.
///   </description></item>
/// </list>
/// </para>
/// <para>
/// When no <see cref="Expect100Policy"/> is provided the stage is a pass-through in both directions.
/// </para>
/// </summary>
internal sealed class ExpectContinueBidiStage
    : GraphStage<BidiShape<HttpRequestMessage, HttpRequestMessage, HttpResponseMessage, HttpResponseMessage>>
{
    private readonly Expect100Policy? _policy;

    private readonly Inlet<HttpRequestMessage> _inRequest = new("Expect100.In.Request");
    private readonly Outlet<HttpRequestMessage> _outRequest = new("Expect100.Out.Request");
    private readonly Inlet<HttpResponseMessage> _inResponse = new("Expect100.In.Response");
    private readonly Outlet<HttpResponseMessage> _outResponse = new("Expect100.Out.Response");

    public override BidiShape<HttpRequestMessage, HttpRequestMessage, HttpResponseMessage, HttpResponseMessage> Shape { get; }

    /// <summary>
    /// Creates a new <see cref="ExpectContinueBidiStage"/> with the given policy.
    /// </summary>
    /// <param name="policy">Expect policy. When null, the stage is a pass-through.</param>
    public ExpectContinueBidiStage(Expect100Policy? policy = null)
    {
        _policy = policy;
        Shape = new BidiShape<HttpRequestMessage, HttpRequestMessage, HttpResponseMessage, HttpResponseMessage>(
            _inRequest, _outRequest, _inResponse, _outResponse);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private readonly ExpectContinueBidiStage _stage;

        /// <summary>Whether the current in-flight request had the Expect header injected.</summary>
        private bool _expectPending;

        private bool _responseDemand;

        public Logic(ExpectContinueBidiStage stage) : base(stage.Shape)
        {
            _stage = stage;

            if (stage._policy is null)
            {
                // Null policy → pure pass-through in both directions
                SetHandler(stage._inRequest,
                    onPush: () => Push(stage._outRequest, Grab(stage._inRequest)),
                    onUpstreamFinish: () => Complete(stage._outRequest),
                    onUpstreamFailure: ex => Log.Warning("ExpectContinueBidiStage: Request upstream failure absorbed: {0}", ex.Message));

                SetHandler(stage._outRequest,
                    onPull: () => Pull(stage._inRequest),
                    onDownstreamFinish: _ => Cancel(stage._inRequest));

                SetHandler(stage._inResponse,
                    onPush: () => Push(stage._outResponse, Grab(stage._inResponse)),
                    onUpstreamFinish: () => Complete(stage._outResponse),
                    onUpstreamFailure: ex => Log.Warning("ExpectContinueBidiStage: Response upstream failure absorbed: {0}", ex.Message));

                SetHandler(stage._outResponse,
                    onPull: () => Pull(stage._inResponse),
                    onDownstreamFinish: _ => Cancel(stage._inResponse));

                return;
            }

            // --- Request direction (In1→Out1) ---
            SetHandler(stage._inRequest,
                onPush: () =>
                {
                    var request = Grab(stage._inRequest);
                    var bodySize = request.Content?.Headers.ContentLength ?? -1;

                    if (bodySize >= stage._policy.MinBodySizeBytes)
                    {
                        request.Headers.ExpectContinue = true;
                        _expectPending = true;
                    }
                    else
                    {
                        _expectPending = false;
                    }

                    Push(stage._outRequest, request);
                },
                onUpstreamFinish: () => Complete(stage._outRequest),
                onUpstreamFailure: ex => Log.Warning("ExpectContinueBidiStage: Request upstream failure absorbed: {0}", ex.Message));

            SetHandler(stage._outRequest,
                onPull: () => Pull(stage._inRequest),
                onDownstreamFinish: _ => Cancel(stage._inRequest));

            // --- Response direction (In2→Out2) ---
            SetHandler(stage._inResponse,
                onPush: () =>
                {
                    var response = Grab(stage._inResponse);

                    if (response.StatusCode == HttpStatusCode.Continue)
                    {
                        // 100 Continue — informational, never expose to caller.
                        // Consume and pull next response.
                        _expectPending = false;
                        response.Dispose();
                        TryPullResponse();
                        return;
                    }

                    if (_expectPending && response.StatusCode == (HttpStatusCode)417)
                    {
                        // 417 Expectation Failed — request aborted, forward response.
                        _expectPending = false;
                        _responseDemand = false;
                        Push(stage._outResponse, response);
                        return;
                    }

                    // Final response or non-Expect request — forward.
                    _expectPending = false;
                    _responseDemand = false;
                    Push(stage._outResponse, response);
                },
                onUpstreamFinish: () => Complete(stage._outResponse),
                onUpstreamFailure: ex => Log.Warning("ExpectContinueBidiStage: Response upstream failure absorbed: {0}", ex.Message));

            SetHandler(stage._outResponse,
                onPull: () =>
                {
                    _responseDemand = true;
                    TryPullResponse();
                },
                onDownstreamFinish: _ => Cancel(stage._inResponse));
        }

        private void TryPullResponse()
        {
            if (_responseDemand
                && !HasBeenPulled(_stage._inResponse)
                && !IsClosed(_stage._inResponse))
            {
                Pull(_stage._inResponse);
            }
        }
    }
}
