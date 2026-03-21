using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.Middleware;

namespace TurboHttp.Streams.Stages;

internal sealed class MiddlewareResponseStage : GraphStage<FlowShape<HttpResponseMessage, HttpResponseMessage>>
{
    private readonly TurboMiddleware _middleware;

    private readonly Inlet<HttpResponseMessage> _in = new("MiddlewareResponse.In");
    private readonly Outlet<HttpResponseMessage> _out = new("MiddlewareResponse.Out");

    public override FlowShape<HttpResponseMessage, HttpResponseMessage> Shape { get; }

    public MiddlewareResponseStage(TurboMiddleware middleware)
    {
        _middleware = middleware;
        Shape = new FlowShape<HttpResponseMessage, HttpResponseMessage>(_in, _out);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private readonly MiddlewareResponseStage _stage;
        private Action<HttpResponseMessage>? _onProcessed;

        public Logic(MiddlewareResponseStage stage) : base(stage.Shape)
        {
            _stage = stage;

            SetHandler(stage._in,
                onPush: () =>
                {
                    var response = Grab(stage._in);
                    var original = response.RequestMessage!;
                    var task = stage._middleware.ProcessResponseAsync(original, response, CancellationToken.None);

                    if (task.IsCompletedSuccessfully)
                    {
                        Push(stage._out, task.Result);
                        return;
                    }

                    var callback = _onProcessed!;
                    task.AsTask().ContinueWith(
                        t => callback(t.Result),
                        TaskContinuationOptions.ExecuteSynchronously);
                },
                onUpstreamFinish: CompleteStage,
                onUpstreamFailure: ex => Log.Warning("MiddlewareResponseStage: Upstream failure absorbed: {0}", ex.Message));

            SetHandler(stage._out,
                onPull: () => Pull(stage._in),
                onDownstreamFinish: _ => CompleteStage());
        }

        public override void PreStart()
        {
            _onProcessed = GetAsyncCallback<HttpResponseMessage>(result =>
            {
                Push(_stage._out, result);
            });
        }
    }
}
