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

internal sealed class ApplicationBridgeStage<TContext> : GraphStage<FlowShape<IFeatureCollection, IFeatureCollection>>
    where TContext : notnull
{
    private readonly IHttpApplication<TContext> _application;
    private readonly int _parallelism;
    private readonly TimeSpan _handlerTimeout;
    private readonly TimeSpan _handlerGracePeriod;

    private readonly Inlet<IFeatureCollection> _in = new("AppBridge.In");
    private readonly Outlet<IFeatureCollection> _out = new("AppBridge.Out");

    public override FlowShape<IFeatureCollection, IFeatureCollection> Shape { get; }

    public ApplicationBridgeStage(
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

    private sealed class Logic : TimerGraphStageLogic
    {
        private readonly ApplicationBridgeStage<TContext> _stage;
        private IActorRef? _stageActor;
        private bool _upstreamFinished;
        private int _inFlight;
        private int _sequence;
        private int _nextToEmit;
        private bool _downstreamReady;
        private bool _unordered;
        private bool _protocolDetected;
        private readonly SortedDictionary<int, IFeatureCollection> _pending = [];
        private readonly Dictionary<int, CancellationTokenSource> _activeTimeouts = [];
        private readonly Dictionary<int, IFeatureCollection> _activeFeatures = [];
        private readonly HashSet<int> _gracePhase = [];
        private readonly Dictionary<int, TContext> _appContexts = [];
        private readonly bool _metricsEnabled;
        private readonly int _backpressureThreshold;
        private bool _backpressureSignaled;

        public Logic(ApplicationBridgeStage<TContext> stage) : base(stage.Shape)
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

        protected override void OnTimer(object timerKey)
        {
            if (timerKey is not string key)
            {
                return;
            }

            if (key.StartsWith("soft:") && int.TryParse(key.AsSpan(5), out var softSeq))
            {
                OnSoftTimeout(softSeq);
            }
            else if (key.StartsWith("hard:") && int.TryParse(key.AsSpan(5), out var hardSeq))
            {
                OnHardTimeout(hardSeq);
            }
        }

        private void OnSoftTimeout(int seq)
        {
            if (!_activeTimeouts.TryGetValue(seq, out var cts))
            {
                return;
            }

            cts.Cancel();
            _gracePhase.Add(seq);
            ScheduleOnce($"hard:{seq}", _stage._handlerGracePeriod);
        }

        private void OnHardTimeout(int seq)
        {
            if (!_activeTimeouts.ContainsKey(seq) || !_gracePhase.Contains(seq))
            {
                return;
            }

            if (!_activeFeatures.TryGetValue(seq, out var features))
            {
                return;
            }

            CleanupTimeout(seq);
            _inFlight--;
            if (_metricsEnabled)
            {
                Metrics.HandlerTimeouts().Add(1);
                Metrics.PipelineInFlight().Add(-1);
                ResetBackpressure();
            }

            DisposeAppContext(seq, null);

            if (features.Get<IHttpResponseBodyFeature>() is not TurboHttpResponseBodyFeature
                {
                    HasStarted: true
                })
            {
                var responseFeature = features.Get<IHttpResponseFeature>();
                responseFeature?.StatusCode = 503;
            }

            CompleteResponseBody(features);
            Emit(seq, features);

            if (_upstreamFinished && _inFlight == 0)
            {
                CompleteStage();
            }
        }

        private void OnPush()
        {
            var features = Grab(_stage._in);
            var seq = _sequence++;

            if (!_protocolDetected)
            {
                _protocolDetected = true;
                var requestFeature = features.Get<IHttpRequestFeature>();
                var protocol = requestFeature?.Protocol ?? "";
                _unordered = protocol.StartsWith("HTTP/2") || protocol.StartsWith("HTTP/3");
            }

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
                responseFeature?.StatusCode = 500;
                CompleteResponseBody(features);
                Emit(seq, features);
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
                responseFeature?.StatusCode = 500;
                CompleteResponseBody(features);
                Emit(seq, features);
                return;
            }

            var task = _stage._application.ProcessRequestAsync(appContext);

            if (task.IsCompletedSuccessfully)
            {
                _inFlight--;
                _stage._application.DisposeContext(appContext, null);
                _appContexts.Remove(seq);
                CompleteResponseBody(features);
                Emit(seq, features);
            }
            else if (task.IsFaulted)
            {
                _inFlight--;
                var responseFeature = features.Get<IHttpResponseFeature>();
                responseFeature?.StatusCode = 500;
                _stage._application.DisposeContext(appContext, task.Exception);
                _appContexts.Remove(seq);
                CompleteResponseBody(features);
                Emit(seq, features);
            }
            else
            {
                var lifetime = features.Get<IHttpRequestLifetimeFeature>();
                var cts = lifetime is not null
                    ? CancellationTokenSource.CreateLinkedTokenSource(lifetime.RequestAborted)
                    : new CancellationTokenSource();
                _activeTimeouts[seq] = cts;
                _activeFeatures[seq] = features;
                ScheduleOnce($"soft:{seq}", _stage._handlerTimeout);

                var bodyFeature = features.Get<IHttpResponseBodyFeature>() as TurboHttpResponseBodyFeature;
                var headersReady = bodyFeature?.WhenHeadersReady;

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
                    if (handlerTask.IsFaulted &&
                        features.Get<IHttpResponseBodyFeature>() is not TurboHttpResponseBodyFeature
                        {
                            HasStarted: true
                        })
                    {
                        var responseFeature = features.Get<IHttpResponseFeature>();
                        responseFeature?.StatusCode = 500;
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

                        CleanupTimeout(seq);
                        DisposeAppContext(seq, handlerTask.Exception);
                        Emit(seq, features);
                    }
                    else
                    {
                        Emit(seq, features);
                        handlerTask.PipeTo(_stageActor!,
                            success: () => new HandlerFinished(seq, features),
                            failure: ex => new HandlerFaulted(seq, features, ex));
                    }

                    break;

                case HandlerFinished(var seq, var finishedFeatures):
                    if (!_activeTimeouts.ContainsKey(seq))
                    {
                        break;
                    }

                    CompleteResponseBody(finishedFeatures);
                    _inFlight--;
                    if (_metricsEnabled)
                    {
                        Metrics.PipelineInFlight().Add(-1);
                        ResetBackpressure();
                    }

                    CleanupTimeout(seq);
                    DisposeAppContext(seq, null);
                    if (_upstreamFinished && _inFlight == 0)
                    {
                        CompleteStage();
                    }

                    break;

                case HandlerFaulted(var seq, var faultedFeatures, var error):
                    if (!_activeTimeouts.ContainsKey(seq))
                    {
                        break;
                    }

                    CompleteResponseBody(faultedFeatures);
                    _inFlight--;
                    if (_metricsEnabled)
                    {
                        Metrics.PipelineInFlight().Add(-1);
                        ResetBackpressure();
                    }

                    CleanupTimeout(seq);
                    DisposeAppContext(seq, error);
                    if (_upstreamFinished && _inFlight == 0)
                    {
                        CompleteStage();
                    }

                    break;

                case DispatchCompleted(var seq, var features):
                    if (!_activeTimeouts.ContainsKey(seq))
                    {
                        break;
                    }

                    _inFlight--;
                    if (_metricsEnabled)
                    {
                        Metrics.PipelineInFlight().Add(-1);
                        ResetBackpressure();
                    }

                    CleanupTimeout(seq);
                    DisposeAppContext(seq, null);
                    CompleteResponseBody(features);
                    Emit(seq, features);
                    break;

                case DispatchFailed(var seq, var features, var error):
                    if (!_activeTimeouts.ContainsKey(seq))
                    {
                        break;
                    }

                    _inFlight--;
                    if (_metricsEnabled)
                    {
                        Metrics.PipelineInFlight().Add(-1);
                        ResetBackpressure();
                    }

                    CleanupTimeout(seq);
                    DisposeAppContext(seq, error);
                    var respFeature = features.Get<IHttpResponseFeature>();
                    respFeature?.StatusCode = 500;
                    CompleteResponseBody(features);
                    Emit(seq, features);
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

        private void CleanupTimeout(int seq)
        {
            CancelTimer($"soft:{seq}");
            CancelTimer($"hard:{seq}");
            _gracePhase.Remove(seq);
            _activeFeatures.Remove(seq);
            if (_activeTimeouts.Remove(seq, out var cts))
            {
                cts.Dispose();
            }
        }

        private void TryPullNext()
        {
            if (_inFlight < _stage._parallelism && !HasBeenPulled(_stage._in))
            {
                Pull(_stage._in);
            }
        }

        private void Emit(int seq, IFeatureCollection features)
        {
            _pending[seq] = features;
            if (_metricsEnabled)
            {
                Metrics.PipelinePending().Add(1);
            }

            TryEmitPending();
        }

        private void TryEmitPending()
        {
            if (_unordered)
            {
                if (_downstreamReady && _pending.Count > 0)
                {
                    EmitOne(FirstPendingKey());
                }
            }
            else
            {
                // All pending keys are >= _nextToEmit (responses are never emitted out of
                // order before _nextToEmit), so ContainsKey is equivalent to checking the
                // smallest key — without the boxed enumerator that Keys.First() allocates.
                while (_downstreamReady && _pending.ContainsKey(_nextToEmit))
                {
                    EmitOne(_nextToEmit);
                    _nextToEmit++;
                }
            }
        }

        private int FirstPendingKey()
        {
            foreach (var key in _pending.Keys)
            {
                return key;
            }

            return -1;
        }

        private void EmitOne(int seq)
        {
            _downstreamReady = false;
            Push(_stage._out, _pending[seq]);
            _pending.Remove(seq);
            if (_metricsEnabled)
            {
                Metrics.PipelinePending().Add(-1);
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