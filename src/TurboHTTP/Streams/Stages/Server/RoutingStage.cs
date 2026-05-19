using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHTTP.Routing;
using TurboHTTP.Server;

namespace TurboHTTP.Streams.Stages.Server;

internal sealed class RoutingStage : GraphStage<FlowShape<TurboHttpContext, TurboHttpContext>>
{
    private readonly RouteTable _routeTable;

    private readonly Inlet<TurboHttpContext> _in = new("Routing.In");
    private readonly Outlet<TurboHttpContext> _out = new("Routing.Out");

    public override FlowShape<TurboHttpContext, TurboHttpContext> Shape { get; }

    public RoutingStage(RouteTable routeTable)
    {
        _routeTable = routeTable;
        Shape = new FlowShape<TurboHttpContext, TurboHttpContext>(_in, _out);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

    private sealed record DispatchCompleted(TurboHttpContext Context);
    private sealed record DispatchFailed(TurboHttpContext Context, Exception Error);

    private sealed class Logic : GraphStageLogic
    {
        private readonly RoutingStage _stage;
        private IActorRef? _stageActor;
        private bool _upstreamFinished;
        private bool _dispatching;

        public Logic(RoutingStage stage) : base(stage.Shape)
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
            var method = new HttpMethod(ctx.Request.Method);
            var path = ctx.Request.Path.Value ?? "/";

            var match = _stage._routeTable.Match(method, path);
            if (match is not { IsMatch: true, Dispatcher: not null })
            {
                ctx.Response.StatusCode = 404;
                Push(_stage._out, ctx);
                return;
            }

            foreach (var kv in match.RouteValues)
            {
                ctx.Request.RouteValues[kv.Key] = kv.Value;
            }

            _dispatching = true;

            try
            {
                var task = match.Dispatcher.DispatchAsync(ctx, ctx.RequestAborted);
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
                        success: () => new DispatchCompleted(ctx),
                        failure: ex => new DispatchFailed(ctx, ex));
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
                case DispatchCompleted completed:
                    _dispatching = false;
                    Push(_stage._out, completed.Context);
                    if (_upstreamFinished)
                    {
                        CompleteStage();
                    }
                    break;

                case DispatchFailed failed:
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
