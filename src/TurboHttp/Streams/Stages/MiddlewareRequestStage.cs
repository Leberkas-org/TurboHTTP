using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.Middleware;

namespace TurboHttp.Streams.Stages;

internal sealed class MiddlewareRequestStage : GraphStage<FlowShape<HttpRequestMessage, HttpRequestMessage>>
{
    private readonly TurboMiddleware _middleware;

    private readonly Inlet<HttpRequestMessage> _in = new("MiddlewareRequest.In");
    private readonly Outlet<HttpRequestMessage> _out = new("MiddlewareRequest.Out");

    public override FlowShape<HttpRequestMessage, HttpRequestMessage> Shape { get; }

    public MiddlewareRequestStage(TurboMiddleware middleware)
    {
        _middleware = middleware;
        Shape = new FlowShape<HttpRequestMessage, HttpRequestMessage>(_in, _out);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private readonly MiddlewareRequestStage _stage;
        private Action<HttpRequestMessage>? _onProcessed;
        private bool _asyncInFlight;
        private bool _upstreamFinished;

        public Logic(MiddlewareRequestStage stage) : base(stage.Shape)
        {
            _stage = stage;

            SetHandler(stage._in,
                onPush: () =>
                {
                    var request = Grab(stage._in);
                    var task = stage._middleware.ProcessRequestAsync(request, CancellationToken.None);

                    if (task.IsCompletedSuccessfully)
                    {
                        Push(stage._out, task.Result);
                        return;
                    }

                    _asyncInFlight = true;
                    var callback = _onProcessed!;
                    task.AsTask().ContinueWith(
                        t => callback(t.Result),
                        TaskContinuationOptions.ExecuteSynchronously);
                },
                onUpstreamFinish: () =>
                {
                    if (_asyncInFlight)
                    {
                        _upstreamFinished = true;
                    }
                    else
                    {
                        CompleteStage();
                    }
                },
                onUpstreamFailure: ex => Log.Warning("MiddlewareRequestStage: Upstream failure absorbed: {0}", ex.Message));

            SetHandler(stage._out,
                onPull: () => Pull(stage._in),
                onDownstreamFinish: _ => CompleteStage());
        }

        public override void PreStart()
        {
            _onProcessed = GetAsyncCallback<HttpRequestMessage>(result =>
            {
                _asyncInFlight = false;
                Push(_stage._out, result);
                if (_upstreamFinished)
                {
                    CompleteStage();
                }
            });
        }
    }
}
