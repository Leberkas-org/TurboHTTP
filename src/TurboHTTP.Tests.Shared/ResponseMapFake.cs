using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.Stage;

namespace TurboHTTP.Tests.Shared;

/// <summary>
/// Protocol-level BidiFlow fake that maps <see cref="HttpRequestMessage"/> to
/// <see cref="HttpResponseMessage"/> using a <see cref="ResponseMap"/>.
/// Operates at the HTTP message level — no byte encoding/decoding involved.
/// </summary>
/// <remarks>
/// Designed for feature-logic tests (cookies, redirects, cache, retry) where
/// byte-level fidelity is unnecessary. The request passes through unchanged on
/// the outbound side; the inbound side resolves the response from the map.
/// </remarks>
public sealed class ResponseMapFake
    : GraphStage<BidiShape<HttpRequestMessage, HttpRequestMessage, HttpResponseMessage, HttpResponseMessage>>
{
    private readonly ResponseMap _map;

    private readonly Inlet<HttpRequestMessage> _inRequest = new("ResponseMap.In.Request");
    private readonly Outlet<HttpRequestMessage> _outRequest = new("ResponseMap.Out.Request");
    private readonly Inlet<HttpResponseMessage> _inResponse = new("ResponseMap.In.Response");
    private readonly Outlet<HttpResponseMessage> _outResponse = new("ResponseMap.Out.Response");

    public override BidiShape<HttpRequestMessage, HttpRequestMessage, HttpResponseMessage, HttpResponseMessage> Shape { get; }

    public ResponseMapFake(ResponseMap map)
    {
        _map = map;
        Shape = new BidiShape<HttpRequestMessage, HttpRequestMessage, HttpResponseMessage, HttpResponseMessage>(
            _inRequest, _outRequest, _inResponse, _outResponse);
    }

    /// <summary>
    /// Creates a <see cref="BidiFlow{TIn1,TOut1,TIn2,TOut2,TMat}"/> from the given <see cref="ResponseMap"/>.
    /// </summary>
    public static BidiFlow<HttpRequestMessage, HttpRequestMessage, HttpResponseMessage, HttpResponseMessage, NotUsed>
        Create(ResponseMap map) =>
        BidiFlow.FromGraph(new ResponseMapFake(map));

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private readonly ResponseMapFake _stage;
        private readonly Queue<HttpResponseMessage> _pendingResponses = new();
        private bool _responseRequested;

        public Logic(ResponseMapFake stage) : base(stage.Shape)
        {
            _stage = stage;

            // Request direction: resolve response from map, pass request through
            SetHandler(stage._inRequest,
                onPush: () =>
                {
                    var request = Grab(stage._inRequest);
                    var response = stage._map.Resolve(request);

                    // Pass request through to outbound (enables downstream stages to see it)
                    Push(stage._outRequest, request);

                    // Queue the resolved response for the inbound direction
                    if (_responseRequested)
                    {
                        _responseRequested = false;
                        Push(stage._outResponse, response);
                    }
                    else
                    {
                        _pendingResponses.Enqueue(response);
                    }
                },
                onUpstreamFinish: () => Complete(stage._outRequest),
                onUpstreamFailure: FailStage);

            SetHandler(stage._outRequest,
                onPull: () => Pull(stage._inRequest),
                onDownstreamFinish: _ => Cancel(stage._inRequest));

            // Response direction: ignored — responses come from the map, not upstream
            SetHandler(stage._inResponse,
                onPush: () =>
                {
                    // Discard any response from the real downstream; we generate our own
                    Grab(stage._inResponse);
                    Pull(stage._inResponse);
                },
                onUpstreamFinish: () => { },
                onUpstreamFailure: _ => { });

            SetHandler(stage._outResponse,
                onPull: () =>
                {
                    if (_pendingResponses.TryDequeue(out var response))
                    {
                        Push(stage._outResponse, response);
                    }
                    else
                    {
                        _responseRequested = true;
                    }
                },
                onDownstreamFinish: _ =>
                {
                    if (!IsClosed(stage._inRequest))
                    {
                        Cancel(stage._inRequest);
                    }
                });
        }

        public override void PreStart()
        {
            // Pull inResponse so the downstream flow's output port is not blocked.
            // Responses from the downstream are discarded — the map generates them.
            Pull(_stage._inResponse);
        }
    }
}
