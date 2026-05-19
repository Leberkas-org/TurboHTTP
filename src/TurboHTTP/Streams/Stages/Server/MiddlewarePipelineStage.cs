using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHTTP.Server;
using TurboHTTP.Server.Middleware;

namespace TurboHTTP.Streams.Stages.Server;

internal sealed class MiddlewarePipelineStage : GraphStage<FlowShape<TurboHttpContext, TurboHttpContext>>
{
    private readonly TurboRequestDelegate _pipeline;

    private readonly Inlet<TurboHttpContext> _in = new("Middleware.In");
    private readonly Outlet<TurboHttpContext> _out = new("Middleware.Out");

    public override FlowShape<TurboHttpContext, TurboHttpContext> Shape { get; }

    public MiddlewarePipelineStage(TurboRequestDelegate pipeline)
    {
        _pipeline = pipeline;
        Shape = new FlowShape<TurboHttpContext, TurboHttpContext>(_in, _out);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

    private sealed record MiddlewareCompleted(TurboHttpContext Context);
    private sealed record MiddlewareFailed(TurboHttpContext Context, Exception Error);

    private sealed class Logic : GraphStageLogic
    {
        private readonly MiddlewarePipelineStage _stage;
        private IActorRef? _stageActor;
        private bool _upstreamFinished;
        private bool _dispatching;

        public Logic(MiddlewarePipelineStage stage) : base(stage.Shape)
        {
            _stage = stage;

            SetHandler(stage._in,
                onPush: OnPush,
                onUpstreamFinish: () =>
                {
                    _upstreamFinished = true;
                    if (!_dispatching)
                    {
                        CompleteStage();
                    }
                });

            SetHandler(stage._out,
                onPull: () => Pull(stage._in));
        }

        public override void PreStart()
        {
            _stageActor = GetStageActor(OnMessage).Ref;
        }

        private void OnPush()
        {
            var ctx = Grab(_stage._in);
            _dispatching = true;

            try
            {
                var task = _stage._pipeline(ctx);
                if (task.IsCompletedSuccessfully)
                {
                    _dispatching = false;
                    Push(_stage._out, ctx);
                    if (_upstreamFinished)
                    {
                        CompleteStage();
                    }
                }
                else if (task.IsFaulted)
                {
                    _dispatching = false;
                    ctx.Response.StatusCode = 500;
                    Push(_stage._out, ctx);
                    if (_upstreamFinished)
                    {
                        CompleteStage();
                    }
                }
                else
                {
                    task.PipeTo(_stageActor!,
                        success: () => new MiddlewareCompleted(ctx),
                        failure: ex => new MiddlewareFailed(ctx, ex));
                }
            }
            catch (Exception)
            {
                _dispatching = false;
                ctx.Response.StatusCode = 500;
                Push(_stage._out, ctx);
                if (_upstreamFinished)
                {
                    CompleteStage();
                }
            }
        }

        private void OnMessage((IActorRef sender, object msg) args)
        {
            switch (args.msg)
            {
                case MiddlewareCompleted completed:
                    _dispatching = false;
                    Push(_stage._out, completed.Context);
                    if (_upstreamFinished)
                    {
                        CompleteStage();
                    }
                    break;

                case MiddlewareFailed failed:
                    _dispatching = false;
                    failed.Context.Response.StatusCode = 500;
                    Push(_stage._out, failed.Context);
                    if (_upstreamFinished)
                    {
                        CompleteStage();
                    }
                    break;
            }
        }
    }
}
