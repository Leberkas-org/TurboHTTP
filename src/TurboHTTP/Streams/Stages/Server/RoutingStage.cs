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
    private readonly TurboRequestDelegate _pipeline;
    private readonly int _parallelism;
    private readonly TimeSpan _handlerTimeout;
    private readonly TimeSpan _handlerGracePeriod;

    private readonly Inlet<TurboHttpContext> _in = new("Routing.In");
    private readonly Outlet<TurboHttpContext> _out = new("Routing.Out");

    public override FlowShape<TurboHttpContext, TurboHttpContext> Shape { get; }

    public RoutingStage(RouteTable routeTable, TurboRequestDelegate pipeline, int parallelism, TimeSpan handlerTimeout,
        TimeSpan handlerGracePeriod)
    {
        _routeTable = routeTable;
        _pipeline = pipeline;
        _parallelism = parallelism;
        _handlerTimeout = handlerTimeout;
        _handlerGracePeriod = handlerGracePeriod;
        Shape = new FlowShape<TurboHttpContext, TurboHttpContext>(_in, _out);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

    private sealed record DispatchCompleted(int Sequence, TurboHttpContext Context);

    private sealed record DispatchFailed(int Sequence, TurboHttpContext Context, Exception Error);

    private sealed record ResponseReady(int Sequence, TurboHttpContext Context, Task HandlerTask);

    private sealed record HandlerFinished(int Sequence, TurboHttpContext Context);

    private sealed record HandlerFaulted(int Sequence, TurboHttpContext Context, Exception Error);

    private sealed record HandlerTimedOut(int Sequence, TurboHttpContext Context);

    private sealed class Logic : GraphStageLogic
    {
        private readonly RoutingStage _stage;
        private IActorRef? _stageActor;
        private bool _upstreamFinished;
        private int _inFlight;
        private int _sequence;
        private int _nextToEmit;
        private bool _downstreamReady;
        private readonly SortedDictionary<int, TurboHttpContext> _pending = [];
        private readonly Dictionary<int, CancellationTokenSource> _activeTimeouts = [];

        public Logic(RoutingStage stage) : base(stage.Shape)
        {
            _stage = stage;

            SetHandler(stage._in,
                onPush: OnPush,
                onUpstreamFinish: () =>
                {
                    _upstreamFinished = true;
                    if (_inFlight == 0)
                    {
                        CompleteStage();
                    }
                });

            SetHandler(stage._out,
                onPull: () =>
                {
                    _downstreamReady = true;
                    TryEmitPending();
                    TryPullNext();
                });
        }

        public override void PreStart()
        {
            _stageActor = GetStageActor(OnMessage).Ref;
            Pull(_stage._in);
        }

        private void OnPush()
        {
            var ctx = Grab(_stage._in);
            var seq = _sequence++;
            var path = ctx.Request.Path.Value ?? "/";

            var match = _stage._routeTable.Match(ctx.Request.Method, path);
            if (match is not { IsMatch: true, Dispatcher: not null })
            {
                ctx.Response.StatusCode = 404;
                CompleteResponseBody(ctx);
                Emit(seq, ctx);
                return;
            }

            foreach (var kv in match.RouteValues)
            {
                ctx.Request.RouteValues[kv.Key] = kv.Value;
            }

            _inFlight++;

            try
            {
                DispatchAsync(ctx, seq, match);
            }
            catch (Exception)
            {
                _inFlight--;
                ctx.Response.StatusCode = 500;
                CompleteResponseBody(ctx);
                Emit(seq, ctx);
            }

            TryPullNext();
        }

        private void DispatchAsync(TurboHttpContext ctx, int seq, RouteMatchResult match)
        {
            var task = DispatchAsyncInternal(ctx, seq, match);

            if (task.IsCompletedSuccessfully)
            {
                _inFlight--;
                CompleteResponseBody(ctx);
                Emit(seq, ctx);
            }
            else if (task.IsFaulted)
            {
                _inFlight--;
                ctx.Response.StatusCode = 500;
                CompleteResponseBody(ctx);
                Emit(seq, ctx);
            }
            else
            {
                var cts = CancellationTokenSource.CreateLinkedTokenSource(ctx.RequestAborted);
                cts.CancelAfter(_stage._handlerTimeout);
                _activeTimeouts[seq] = cts;
                ctx.RequestAborted = cts.Token;

                var bodyFeature = ctx.Features.Get<IHttpResponseBodyFeature>() as TurboHttpResponseBodyFeature;
                var headersReady = bodyFeature?.WhenHeadersReady;

                Task.Delay(_stage._handlerTimeout + _stage._handlerGracePeriod, cts.Token)
                    .PipeTo(_stageActor!,
                        success: () => new HandlerTimedOut(seq, ctx));

                if (headersReady is not null)
                {
                    Task.WhenAny(headersReady, task)
                        .PipeTo(_stageActor!,
                            success: () => new ResponseReady(seq, ctx, task));
                }
                else
                {
                    task.PipeTo(_stageActor!,
                        success: () => new DispatchCompleted(seq, ctx),
                        failure: ex => new DispatchFailed(seq, ctx, ex));
                }
            }
        }

        private async Task DispatchAsyncInternal(TurboHttpContext ctx, int seq, RouteMatchResult match)
        {
            await _stage._pipeline(ctx);
            await match.Dispatcher!.DispatchAsync(ctx, ctx.RequestAborted);
        }

        private void OnMessage((IActorRef sender, object msg) args)
        {
            switch (args.msg)
            {
                case ResponseReady(var seq, var ctx, var handlerTask):
                    if (handlerTask.IsFaulted)
                    {
                        if (ctx.Features.Get<IHttpResponseBodyFeature>() is not TurboHttpResponseBodyFeature
                            {
                                HasStarted: true
                            })
                        {
                            ctx.Response.StatusCode = 500;
                        }
                    }

                    if (handlerTask.IsCompleted)
                    {
                        CompleteResponseBody(ctx);
                        _inFlight--;
                        DisposeCts(seq);
                        Emit(seq, ctx);
                    }
                    else
                    {
                        Emit(seq, ctx);
                        handlerTask.PipeTo(_stageActor!,
                            success: () => new HandlerFinished(seq, ctx),
                            failure: ex => new HandlerFaulted(seq, ctx, ex));
                    }

                    break;

                case HandlerFinished(var seq, var finishedCtx):
                    CompleteResponseBody(finishedCtx);
                    _inFlight--;
                    DisposeCts(seq);
                    if (_upstreamFinished && _inFlight == 0)
                    {
                        CompleteStage();
                    }

                    break;

                case HandlerFaulted(var seq, var faultedCtx, _):
                    CompleteResponseBody(faultedCtx);
                    _inFlight--;
                    DisposeCts(seq);
                    if (_upstreamFinished && _inFlight == 0)
                    {
                        CompleteStage();
                    }

                    break;

                case DispatchCompleted(var seq, var ctx):
                    _inFlight--;
                    DisposeCts(seq);
                    CompleteResponseBody(ctx);
                    Emit(seq, ctx);
                    break;

                case DispatchFailed(var seq, var ctx, _):
                    _inFlight--;
                    DisposeCts(seq);
                    ctx.Response.StatusCode = 500;
                    CompleteResponseBody(ctx);
                    Emit(seq, ctx);
                    break;

                case HandlerTimedOut(var seq, var ctx):
                    if (_activeTimeouts.TryGetValue(seq, out var cts))
                    {
                        cts.Dispose();
                        _activeTimeouts.Remove(seq);
                        if (!ctx.Response.HasStarted)
                        {
                            ctx.Response.StatusCode = 503;
                            CompleteResponseBody(ctx);
                            _inFlight--;
                            Emit(seq, ctx);
                        }
                    }

                    break;
            }

            if (_upstreamFinished && _inFlight == 0 && _pending.Count == 0)
            {
                CompleteStage();
            }
        }

        private void DisposeCts(int seq)
        {
            if (_activeTimeouts.TryGetValue(seq, out var cts))
            {
                cts.Dispose();
                _activeTimeouts.Remove(seq);
            }
        }

        private void TryPullNext()
        {
            if (_inFlight < _stage._parallelism && !HasBeenPulled(_stage._in))
            {
                Pull(_stage._in);
            }
        }

        private void Emit(int seq, TurboHttpContext ctx)
        {
            _pending[seq] = ctx;
            TryEmitPending();
        }

        private void TryEmitPending()
        {
            while (_downstreamReady && _pending.Count > 0 && _pending.Keys.First() == _nextToEmit)
            {
                _downstreamReady = false;
                Push(_stage._out, _pending[_nextToEmit]);
                _pending.Remove(_nextToEmit);
                _nextToEmit++;
            }
        }

        private static void CompleteResponseBody(TurboHttpContext ctx)
        {
            var bodyFeature = ctx.Features.Get<IHttpResponseBodyFeature>() as TurboHttpResponseBodyFeature;
            bodyFeature?.Complete();
        }
    }
}