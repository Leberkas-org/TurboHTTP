using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Stage;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http.Features;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using TurboHTTP.Diagnostics;
using TurboHTTP.Server.Context.Features;
using static Servus.Core.Servus;

namespace TurboHTTP.Streams.Stages.Server;

internal sealed class SharedBridgeStage<TContext> : GraphStage<FlowShape<IFeatureCollection, IFeatureCollection>>
    where TContext : notnull
{
    private readonly IHttpApplication<TContext> _application;
    private readonly int _parallelism;
    private readonly TimeSpan _handlerTimeout;
    private readonly TimeSpan _handlerGracePeriod;

    private readonly Inlet<IFeatureCollection> _in = new("SharedBridge.In");
    private readonly Outlet<IFeatureCollection> _out = new("SharedBridge.Out");

    public override FlowShape<IFeatureCollection, IFeatureCollection> Shape { get; }

    public SharedBridgeStage(
        IHttpApplication<TContext> application,
        int parallelism,
        TimeSpan handlerTimeout,
        TimeSpan handlerGracePeriod)
    {
        _application = application;
        _parallelism = parallelism;
        _handlerTimeout = handlerTimeout;
        _handlerGracePeriod = handlerGracePeriod;
        Shape = new FlowShape<IFeatureCollection, IFeatureCollection>(_in, _out);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

    private sealed record DispatchCompleted(int Sequence, IFeatureCollection Features);

    private sealed record DispatchFailed(int Sequence, IFeatureCollection Features, Exception Error);

    private sealed record ResponseReady(int Sequence, IFeatureCollection Features, Task HandlerTask);

    private sealed record HandlerFinished(int Sequence, IFeatureCollection Features);

    private sealed record HandlerFaulted(int Sequence, IFeatureCollection Features, Exception Error);

    private sealed record HandlerTimedOut(int Sequence, IFeatureCollection Features);

    private sealed class Logic : GraphStageLogic
    {
        private readonly SharedBridgeStage<TContext> _stage;
        private IActorRef? _stageActor;
        private bool _upstreamFinished;
        private int _inFlight;
        private int _sequence;
        private bool _downstreamReady;
        private readonly Queue<IFeatureCollection> _pending = [];
        private readonly Dictionary<int, CancellationTokenSource> _activeTimeouts = [];
        private readonly Dictionary<int, TContext> _appContexts = [];
        private readonly bool _metricsEnabled;
        private readonly int _backpressureThreshold;
        private bool _backpressureSignaled;

        public Logic(SharedBridgeStage<TContext> stage) : base(stage.Shape)
        {
            _stage = stage;
            _metricsEnabled = Metrics.PipelineInFlight().Enabled
                || Metrics.PipelinePending().Enabled
                || Metrics.HandlerTimeouts().Enabled
                || Tracing.IsServerTracingActive();
            _backpressureThreshold = (int)(stage._parallelism * 0.8);

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
            var features = Grab(_stage._in);
            var seq = _sequence++;

            _inFlight++;
            if (_metricsEnabled)
            {
                Metrics.PipelineInFlight().Add(1);
                CheckBackpressure();
            }

            try
            {
                DispatchAsync(features, seq);
            }
            catch (Exception)
            {
                _inFlight--;
                if (_metricsEnabled)
                {
                    Metrics.PipelineInFlight().Add(-1);
                }
                var responseFeature = features.Get<IHttpResponseFeature>();
                if (responseFeature is not null)
                {
                    responseFeature.StatusCode = 500;
                }
                CompleteResponseBody(features);
                Emit(features);
            }

            TryPullNext();
        }

        private void DispatchAsync(IFeatureCollection features, int seq)
        {
            TContext appContext;
            try
            {
                appContext = _stage._application.CreateContext(features);
                _appContexts[seq] = appContext;
            }
            catch (Exception)
            {
                _inFlight--;
                var responseFeature = features.Get<IHttpResponseFeature>();
                if (responseFeature is not null)
                {
                    responseFeature.StatusCode = 500;
                }
                CompleteResponseBody(features);
                Emit(features);
                return;
            }

            var task = _stage._application.ProcessRequestAsync(appContext);

            if (task.IsCompletedSuccessfully)
            {
                _inFlight--;
                _stage._application.DisposeContext(appContext, null);
                _appContexts.Remove(seq);
                CompleteResponseBody(features);
                Emit(features);
            }
            else if (task.IsFaulted)
            {
                _inFlight--;
                var responseFeature = features.Get<IHttpResponseFeature>();
                if (responseFeature is not null)
                {
                    responseFeature.StatusCode = 500;
                }
                _stage._application.DisposeContext(appContext, task.Exception);
                _appContexts.Remove(seq);
                CompleteResponseBody(features);
                Emit(features);
            }
            else
            {
                var lifetime = features.Get<IHttpRequestLifetimeFeature>();
                var cts = lifetime is not null
                    ? CancellationTokenSource.CreateLinkedTokenSource(lifetime.RequestAborted)
                    : new CancellationTokenSource();
                cts.CancelAfter(_stage._handlerTimeout);
                _activeTimeouts[seq] = cts;

                var bodyFeature = features.Get<IHttpResponseBodyFeature>() as TurboHttpResponseBodyFeature;
                var headersReady = bodyFeature?.WhenHeadersReady;

                Task.Delay(_stage._handlerTimeout + _stage._handlerGracePeriod, cts.Token)
                    .PipeTo(_stageActor!,
                        success: () => new HandlerTimedOut(seq, features));

                if (headersReady is not null)
                {
                    Task.WhenAny(headersReady, task)
                        .PipeTo(_stageActor!,
                            success: () => new ResponseReady(seq, features, task));
                }
                else
                {
                    task.PipeTo(_stageActor!,
                        success: () => new DispatchCompleted(seq, features),
                        failure: ex => new DispatchFailed(seq, features, ex));
                }
            }
        }

        private void OnMessage((IActorRef sender, object msg) args)
        {
            switch (args.msg)
            {
                case ResponseReady(var seq, var features, var handlerTask):
                    if (handlerTask.IsFaulted)
                    {
                        if (features.Get<IHttpResponseBodyFeature>() is not TurboHttpResponseBodyFeature
                            {
                                HasStarted: true
                            })
                        {
                            var responseFeature = features.Get<IHttpResponseFeature>();
                            if (responseFeature is not null)
                            {
                                responseFeature.StatusCode = 500;
                            }
                        }
                    }

                    if (handlerTask.IsCompleted)
                    {
                        CompleteResponseBody(features);
                        _inFlight--;
                        if (_metricsEnabled)
                        {
                            Metrics.PipelineInFlight().Add(-1);
                            ResetBackpressure();
                        }
                        DisposeCts(seq);
                        DisposeAppContext(seq, handlerTask.Exception);
                        Emit(features);
                    }
                    else
                    {
                        Emit(features);
                        handlerTask.PipeTo(_stageActor!,
                            success: () => new HandlerFinished(seq, features),
                            failure: ex => new HandlerFaulted(seq, features, ex));
                    }

                    break;

                case HandlerFinished(var seq, var finishedFeatures):
                    CompleteResponseBody(finishedFeatures);
                    _inFlight--;
                    if (_metricsEnabled)
                    {
                        Metrics.PipelineInFlight().Add(-1);
                        ResetBackpressure();
                    }
                    DisposeCts(seq);
                    DisposeAppContext(seq, null);
                    if (_upstreamFinished && _inFlight == 0)
                    {
                        CompleteStage();
                    }

                    break;

                case HandlerFaulted(var seq, var faultedFeatures, var error):
                    CompleteResponseBody(faultedFeatures);
                    _inFlight--;
                    if (_metricsEnabled)
                    {
                        Metrics.PipelineInFlight().Add(-1);
                        ResetBackpressure();
                    }
                    DisposeCts(seq);
                    DisposeAppContext(seq, error);
                    if (_upstreamFinished && _inFlight == 0)
                    {
                        CompleteStage();
                    }

                    break;

                case DispatchCompleted(var seq, var features):
                    _inFlight--;
                    if (_metricsEnabled)
                    {
                        Metrics.PipelineInFlight().Add(-1);
                        ResetBackpressure();
                    }
                    DisposeCts(seq);
                    DisposeAppContext(seq, null);
                    CompleteResponseBody(features);
                    Emit(features);
                    break;

                case DispatchFailed(var seq, var features, var error):
                    _inFlight--;
                    if (_metricsEnabled)
                    {
                        Metrics.PipelineInFlight().Add(-1);
                        ResetBackpressure();
                    }
                    DisposeCts(seq);
                    DisposeAppContext(seq, error);
                    var respFeature = features.Get<IHttpResponseFeature>();
                    if (respFeature is not null)
                    {
                        respFeature.StatusCode = 500;
                    }
                    CompleteResponseBody(features);
                    Emit(features);
                    break;

                case HandlerTimedOut(var seq, var features):
                    if (_activeTimeouts.TryGetValue(seq, out var cts))
                    {
                        cts.Dispose();
                        _activeTimeouts.Remove(seq);
                        var respFeatureTimeout = features.Get<IHttpResponseFeature>();
                        if (respFeatureTimeout is not null && respFeatureTimeout.StatusCode == 200)
                        {
                            respFeatureTimeout.StatusCode = 503;
                            CompleteResponseBody(features);
                            _inFlight--;
                            if (_metricsEnabled)
                            {
                                Metrics.HandlerTimeouts().Add(1);
                                Metrics.PipelineInFlight().Add(-1);
                            }
                            DisposeAppContext(seq, null);
                            Emit(features);
                        }
                    }

                    break;
            }

            if (_upstreamFinished && _inFlight == 0 && _pending.Count == 0)
            {
                CompleteStage();
            }
        }

        private void DisposeAppContext(int seq, Exception? exception)
        {
            if (_appContexts.TryGetValue(seq, out var appCtx))
            {
                _stage._application.DisposeContext(appCtx, exception);
                _appContexts.Remove(seq);
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

        private void Emit(IFeatureCollection features)
        {
            _pending.Enqueue(features);
            if (_metricsEnabled)
            {
                Metrics.PipelinePending().Add(1);
            }
            TryEmitPending();
        }

        private void TryEmitPending()
        {
            while (_downstreamReady && _pending.Count > 0)
            {
                _downstreamReady = false;
                Push(_stage._out, _pending.Dequeue());
                if (_metricsEnabled)
                {
                    Metrics.PipelinePending().Add(-1);
                }
            }
        }

        private static void CompleteResponseBody(IFeatureCollection features)
        {
            var bodyFeature = features.Get<IHttpResponseBodyFeature>() as TurboHttpResponseBodyFeature;
            bodyFeature?.Complete();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void CheckBackpressure()
        {
            if (_inFlight >= _backpressureThreshold && !_backpressureSignaled)
            {
                _backpressureSignaled = true;
                if (Activity.Current is { } connectionActivity)
                {
                    Tracing.AddBackpressureEvent(connectionActivity, _inFlight, _stage._parallelism);
                }
            }
        }

        private void ResetBackpressure()
        {
            if (_backpressureSignaled && _inFlight < _backpressureThreshold)
            {
                _backpressureSignaled = false;
            }
        }
    }
}
