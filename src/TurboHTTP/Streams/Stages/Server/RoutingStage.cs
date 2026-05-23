using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Stage;
using Microsoft.AspNetCore.Http.Features;
using TurboHTTP.Context.Features;
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
    private sealed record ResponseReady(TurboHttpContext Context, Task HandlerTask);
    private sealed record HandlerFinished(TurboHttpContext Context);
    private sealed record HandlerFaulted(TurboHttpContext Context, Exception Error);

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
                CompleteResponseBody(ctx);
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
                    CompleteResponseBody(ctx);
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
                    CompleteResponseBody(ctx);
                    Push(_stage._out, ctx);
                    if (_upstreamFinished)
                    {
                        CompleteStage();
                    }
                }
                else
                {
                    var bodyFeature = ctx.Features.Get<IHttpResponseBodyFeature>() as TurboHttpResponseBodyFeature;
                    var headersReady = bodyFeature?.WhenHeadersReady;

                    if (headersReady is not null)
                    {
                        Task.WhenAny(headersReady, task).ContinueWith(
                            _ => new ResponseReady(ctx, task),
                            TaskScheduler.Default)
                            .PipeTo(_stageActor!);
                    }
                    else
                    {
                        task.PipeTo(_stageActor!,
                            success: () => new DispatchCompleted(ctx),
                            failure: ex => new DispatchFailed(ctx, ex));
                    }
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
                case ResponseReady(var ctx, var handlerTask):
                    if (handlerTask.IsFaulted)
                    {
                        var feature = ctx.Features.Get<IHttpResponseBodyFeature>() as TurboHttpResponseBodyFeature;
                        if (feature is null || !feature.HasStarted)
                        {
                            ctx.Response.StatusCode = 500;
                        }
                    }

                    if (handlerTask.IsCompleted)
                    {
                        CompleteResponseBody(ctx);
                    }

                    Push(_stage._out, ctx);

                    if (!handlerTask.IsCompleted)
                    {
                        handlerTask.PipeTo(_stageActor!,
                            success: () => new HandlerFinished(ctx),
                            failure: ex => new HandlerFaulted(ctx, ex));
                    }
                    else
                    {
                        _dispatching = false;
                        if (_upstreamFinished)
                        {
                            CompleteStage();
                        }
                    }
                    break;

                case HandlerFinished(var finishedCtx):
                    CompleteResponseBody(finishedCtx);
                    _dispatching = false;
                    if (_upstreamFinished)
                    {
                        CompleteStage();
                    }
                    break;

                case HandlerFaulted(var faultedCtx, _):
                    CompleteResponseBody(faultedCtx);
                    _dispatching = false;
                    if (_upstreamFinished)
                    {
                        CompleteStage();
                    }
                    break;

                case DispatchCompleted completed:
                    _dispatching = false;
                    CompleteResponseBody(completed.Context);
                    Push(_stage._out, completed.Context);
                    if (_upstreamFinished)
                    {
                        CompleteStage();
                    }
                    break;

                case DispatchFailed failed:
                    _dispatching = false;
                    failed.Context.Response.StatusCode = 500;
                    CompleteResponseBody(failed.Context);
                    Push(_stage._out, failed.Context);
                    if (_upstreamFinished)
                    {
                        CompleteStage();
                    }
                    break;
            }
        }

        private static void CompleteResponseBody(TurboHttpContext ctx)
        {
            var bodyFeature = ctx.Features.Get<IHttpResponseBodyFeature>() as TurboHttpResponseBodyFeature;
            bodyFeature?.Complete();
        }
    }
}
