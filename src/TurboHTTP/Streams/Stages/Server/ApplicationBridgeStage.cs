using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Stage;
using Microsoft.AspNetCore.Http.Features;
using TurboHTTP.Context.Features;

namespace TurboHTTP.Streams.Stages.Server;

internal sealed class ApplicationBridgeStage : GraphStage<FlowShape<RequestContext, RequestContext>>
{
    private readonly Func<IFeatureCollection, object> _createContext;
    private readonly Func<object, Task> _processRequest;
    private readonly Action<object, Exception?> _disposeContext;
    private readonly int _parallelism;
    private readonly TimeSpan _handlerTimeout;
    private readonly TimeSpan _handlerGracePeriod;

    private readonly Inlet<RequestContext> _in = new("AppBridge.In");
    private readonly Outlet<RequestContext> _out = new("AppBridge.Out");

    public override FlowShape<RequestContext, RequestContext> Shape { get; }

    private ApplicationBridgeStage(
        Func<IFeatureCollection, object> createContext,
        Func<object, Task> processRequest,
        Action<object, Exception?> disposeContext,
        int parallelism,
        TimeSpan handlerTimeout,
        TimeSpan handlerGracePeriod)
    {
        _createContext = createContext;
        _processRequest = processRequest;
        _disposeContext = disposeContext;
        _parallelism = parallelism;
        _handlerTimeout = handlerTimeout;
        _handlerGracePeriod = handlerGracePeriod;
        Shape = new FlowShape<RequestContext, RequestContext>(_in, _out);
    }

    public static ApplicationBridgeStage Create<TContext>(
        Microsoft.AspNetCore.Hosting.Server.IHttpApplication<TContext> application,
        int parallelism,
        TimeSpan handlerTimeout,
        TimeSpan handlerGracePeriod) where TContext : notnull
    {
        return new ApplicationBridgeStage(
            features => application.CreateContext(features),
            ctx => application.ProcessRequestAsync((TContext)ctx),
            (ctx, ex) => application.DisposeContext((TContext)ctx, ex),
            parallelism,
            handlerTimeout,
            handlerGracePeriod);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

    private sealed record DispatchCompleted(int Sequence, RequestContext Context);

    private sealed record DispatchFailed(int Sequence, RequestContext Context, Exception Error);

    private sealed record ResponseReady(int Sequence, RequestContext Context, Task HandlerTask);

    private sealed record HandlerFinished(int Sequence, RequestContext Context);

    private sealed record HandlerFaulted(int Sequence, RequestContext Context, Exception Error);

    private sealed record HandlerTimedOut(int Sequence, RequestContext Context);

    private sealed class Logic : GraphStageLogic
    {
        private readonly ApplicationBridgeStage _stage;
        private IActorRef? _stageActor;
        private bool _upstreamFinished;
        private int _inFlight;
        private int _sequence;
        private int _nextToEmit;
        private bool _downstreamReady;
        private readonly SortedDictionary<int, RequestContext> _pending = [];
        private readonly Dictionary<int, CancellationTokenSource> _activeTimeouts = [];
        private readonly Dictionary<int, object> _appContexts = [];

        public Logic(ApplicationBridgeStage stage) : base(stage.Shape)
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

            _inFlight++;

            try
            {
                DispatchAsync(ctx, seq);
            }
            catch (Exception)
            {
                _inFlight--;
                var responseFeature = ctx.Features.Get<IHttpResponseFeature>();
                if (responseFeature is not null)
                {
                    responseFeature.StatusCode = 500;
                }
                CompleteResponseBody(ctx);
                Emit(seq, ctx);
            }

            TryPullNext();
        }

        private void DispatchAsync(RequestContext ctx, int seq)
        {
            object? appContext = null;
            try
            {
                appContext = _stage._createContext(ctx.Features);
                _appContexts[seq] = appContext;
            }
            catch (Exception)
            {
                _inFlight--;
                var responseFeature = ctx.Features.Get<IHttpResponseFeature>();
                if (responseFeature is not null)
                {
                    responseFeature.StatusCode = 500;
                }
                CompleteResponseBody(ctx);
                Emit(seq, ctx);
                return;
            }

            var task = DispatchAsyncInternal(ctx, seq, appContext);

            if (task.IsCompletedSuccessfully)
            {
                _inFlight--;
                _stage._disposeContext(appContext, null);
                _appContexts.Remove(seq);
                CompleteResponseBody(ctx);
                Emit(seq, ctx);
            }
            else if (task.IsFaulted)
            {
                _inFlight--;
                var responseFeature = ctx.Features.Get<IHttpResponseFeature>();
                if (responseFeature is not null)
                {
                    responseFeature.StatusCode = 500;
                }
                _stage._disposeContext(appContext, task.Exception);
                _appContexts.Remove(seq);
                CompleteResponseBody(ctx);
                Emit(seq, ctx);
            }
            else
            {
                var cts = CancellationTokenSource.CreateLinkedTokenSource(ctx.Lifetime?.Token ?? CancellationToken.None);
                cts.CancelAfter(_stage._handlerTimeout);
                _activeTimeouts[seq] = cts;

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

        private async Task DispatchAsyncInternal(RequestContext ctx, int seq, object appContext)
        {
            await _stage._processRequest(appContext);
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
                            var responseFeature = ctx.Features.Get<IHttpResponseFeature>();
                            if (responseFeature is not null)
                            {
                                responseFeature.StatusCode = 500;
                            }
                        }
                    }

                    if (handlerTask.IsCompleted)
                    {
                        CompleteResponseBody(ctx);
                        _inFlight--;
                        DisposeCts(seq);
                        if (_appContexts.TryGetValue(seq, out var appCtxReady))
                        {
                            _stage._disposeContext(appCtxReady, handlerTask.Exception);
                            _appContexts.Remove(seq);
                        }
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
                    if (_appContexts.TryGetValue(seq, out var appCtx))
                    {
                        _stage._disposeContext(appCtx, null);
                        _appContexts.Remove(seq);
                    }
                    if (_upstreamFinished && _inFlight == 0)
                    {
                        CompleteStage();
                    }

                    break;

                case HandlerFaulted(var seq, var faultedCtx, var error):
                    CompleteResponseBody(faultedCtx);
                    _inFlight--;
                    DisposeCts(seq);
                    if (_appContexts.TryGetValue(seq, out var appCtxFaulted))
                    {
                        _stage._disposeContext(appCtxFaulted, error);
                        _appContexts.Remove(seq);
                    }
                    if (_upstreamFinished && _inFlight == 0)
                    {
                        CompleteStage();
                    }

                    break;

                case DispatchCompleted(var seq, var ctx):
                    _inFlight--;
                    DisposeCts(seq);
                    if (_appContexts.TryGetValue(seq, out var appCtxCompleted))
                    {
                        _stage._disposeContext(appCtxCompleted, null);
                        _appContexts.Remove(seq);
                    }
                    CompleteResponseBody(ctx);
                    Emit(seq, ctx);
                    break;

                case DispatchFailed(var seq, var ctx, var error):
                    _inFlight--;
                    DisposeCts(seq);
                    if (_appContexts.TryGetValue(seq, out var appCtxFailed))
                    {
                        _stage._disposeContext(appCtxFailed, error);
                        _appContexts.Remove(seq);
                    }
                    var respFeature = ctx.Features.Get<IHttpResponseFeature>();
                    if (respFeature is not null)
                    {
                        respFeature.StatusCode = 500;
                    }
                    CompleteResponseBody(ctx);
                    Emit(seq, ctx);
                    break;

                case HandlerTimedOut(var seq, var ctx):
                    if (_activeTimeouts.TryGetValue(seq, out var cts))
                    {
                        cts.Dispose();
                        _activeTimeouts.Remove(seq);
                        var respFeatureTimeout = ctx.Features.Get<IHttpResponseFeature>();
                        if (respFeatureTimeout is not null && respFeatureTimeout.StatusCode == 200)
                        {
                            respFeatureTimeout.StatusCode = 503;
                            CompleteResponseBody(ctx);
                            _inFlight--;
                            if (_appContexts.TryGetValue(seq, out var appCtxTimeout))
                            {
                                _stage._disposeContext(appCtxTimeout, null);
                                _appContexts.Remove(seq);
                            }
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

        private void Emit(int seq, RequestContext ctx)
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

        private static void CompleteResponseBody(RequestContext ctx)
        {
            var bodyFeature = ctx.Features.Get<IHttpResponseBodyFeature>() as TurboHttpResponseBodyFeature;
            bodyFeature?.Complete();
        }
    }
}
