using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;

namespace TurboHttp.Streams.Stages.Features;

/// <summary>
/// Bidirectional stage that wraps a <see cref="TurboMiddleware"/> instance,
/// calling <see cref="TurboMiddleware.ProcessRequestAsync"/> on outbound requests
/// and <see cref="TurboMiddleware.ProcessResponseAsync"/> on inbound responses.
/// Composes via <c>BidiFlow.Atop</c> alongside built-in feature stages.
/// </summary>
internal sealed class MiddlewareBidiStage
    : GraphStage<BidiShape<HttpRequestMessage, HttpRequestMessage, HttpResponseMessage, HttpResponseMessage>>
{
    private readonly TurboMiddleware _middleware;

    private readonly Inlet<HttpRequestMessage> _inRequest;
    private readonly Outlet<HttpRequestMessage> _outRequest;
    private readonly Inlet<HttpResponseMessage> _inResponse;
    private readonly Outlet<HttpResponseMessage> _outResponse;

    public override BidiShape<HttpRequestMessage, HttpRequestMessage, HttpResponseMessage, HttpResponseMessage> Shape { get; }

    public MiddlewareBidiStage(TurboMiddleware middleware, int index)
    {
        _middleware = middleware;

        var name = middleware.GetType().Name;
        // Use the middleware class name for readable port names.
        // Always append index to guarantee global uniqueness when multiple
        // middlewares share the same class name (e.g. delegates).
        var prefix = $"{name}{index}";

        _inRequest = new Inlet<HttpRequestMessage>($"{prefix}.In.Request");
        _outRequest = new Outlet<HttpRequestMessage>($"{prefix}.Out.Request");
        _inResponse = new Inlet<HttpResponseMessage>($"{prefix}.In.Response");
        _outResponse = new Outlet<HttpResponseMessage>($"{prefix}.Out.Response");

        Shape = new BidiShape<HttpRequestMessage, HttpRequestMessage, HttpResponseMessage, HttpResponseMessage>(
            _inRequest, _outRequest, _inResponse, _outResponse);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private readonly MiddlewareBidiStage _stage;

        private Action<HttpRequestMessage>? _onRequestProcessed;
        private Action<HttpResponseMessage>? _onResponseProcessed;

        private bool _requestAsyncInFlight;
        private bool _responseAsyncInFlight;
        private bool _requestUpstreamFinished;
        private bool _responseUpstreamFinished;

        public Logic(MiddlewareBidiStage stage) : base(stage.Shape)
        {
            _stage = stage;

            // ── Request direction ──────────────────────────────────────
            SetHandler(stage._inRequest,
                onPush: () =>
                {
                    var request = Grab(stage._inRequest);
                    var task = stage._middleware.ProcessRequestAsync(request, CancellationToken.None);

                    if (task.IsCompletedSuccessfully)
                    {
                        Push(stage._outRequest, task.Result);
                        return;
                    }

                    _requestAsyncInFlight = true;
                    var callback = _onRequestProcessed!;
                    task.AsTask().ContinueWith(
                        t => callback(t.Result),
                        TaskContinuationOptions.ExecuteSynchronously);
                },
                onUpstreamFinish: () =>
                {
                    if (_requestAsyncInFlight)
                    {
                        _requestUpstreamFinished = true;
                    }
                    else
                    {
                        Complete(stage._outRequest);
                    }
                },
                onUpstreamFailure: ex => Log.Warning("MiddlewareBidiStage: Request upstream failure absorbed: {0}", ex.Message));

            SetHandler(stage._outRequest,
                onPull: () => Pull(stage._inRequest),
                onDownstreamFinish: _ => Cancel(stage._inRequest));

            // ── Response direction ─────────────────────────────────────
            SetHandler(stage._inResponse,
                onPush: () =>
                {
                    var response = Grab(stage._inResponse);
                    var original = response.RequestMessage!;
                    var task = stage._middleware.ProcessResponseAsync(original, response, CancellationToken.None);

                    if (task.IsCompletedSuccessfully)
                    {
                        Push(stage._outResponse, task.Result);
                        return;
                    }

                    _responseAsyncInFlight = true;
                    var callback = _onResponseProcessed!;
                    task.AsTask().ContinueWith(
                        t => callback(t.Result),
                        TaskContinuationOptions.ExecuteSynchronously);
                },
                onUpstreamFinish: () =>
                {
                    if (_responseAsyncInFlight)
                    {
                        _responseUpstreamFinished = true;
                    }
                    else
                    {
                        Complete(stage._outResponse);
                    }
                },
                onUpstreamFailure: ex => Log.Warning("MiddlewareBidiStage: Response upstream failure absorbed: {0}", ex.Message));

            SetHandler(stage._outResponse,
                onPull: () => Pull(stage._inResponse),
                onDownstreamFinish: _ => Cancel(stage._inResponse));
        }

        public override void PreStart()
        {
            _onRequestProcessed = GetAsyncCallback<HttpRequestMessage>(result =>
            {
                _requestAsyncInFlight = false;
                Push(_stage._outRequest, result);
                if (_requestUpstreamFinished)
                {
                    Complete(_stage._outRequest);
                }
            });

            _onResponseProcessed = GetAsyncCallback<HttpResponseMessage>(result =>
            {
                _responseAsyncInFlight = false;
                Push(_stage._outResponse, result);
                if (_responseUpstreamFinished)
                {
                    Complete(_stage._outResponse);
                }
            });
        }
    }
}
