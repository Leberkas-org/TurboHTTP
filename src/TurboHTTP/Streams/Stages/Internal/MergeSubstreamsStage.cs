using Akka;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.Stage;

namespace TurboHTTP.Streams.Stages.Internal;

internal sealed class MergeSubstreamsStage<T> : GraphStage<FlowShape<Source<T, NotUsed>, T>>
{
    private readonly int _maxConcurrent;

    private readonly Inlet<Source<T, NotUsed>> _in = new("MergeSubstreams.In");
    private readonly Outlet<T> _out = new("MergeSubstreams.Out");
    public override FlowShape<Source<T, NotUsed>, T> Shape { get; }


    public MergeSubstreamsStage(int maxConcurrent)
    {
        _maxConcurrent = maxConcurrent;
        Shape = new FlowShape<Source<T, NotUsed>, T>(_in, _out);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private readonly MergeSubstreamsStage<T> _stage;

        /// <summary>
        /// SubSinkInlets that have pushed an element but the outlet was busy.
        /// On the next <c>_out</c> pull we grab from the first ready sink.
        /// </summary>
        private readonly Queue<SubSinkInlet<T>> _readyQueue = new();

        /// <summary>All currently active SubSinkInlets — needed for cleanup.</summary>
        private readonly List<SubSinkInlet<T>> _activeSinks = [];

        private bool _upstreamDone;

        public Logic(MergeSubstreamsStage<T> stage) : base(stage.Shape)
        {
            _stage = stage;

            SetHandler(stage._in,
                onPush: () =>
                {
                    var source = Grab(stage._in);

                    MaterializeSubstream(source);

                    if (_activeSinks.Count < _stage._maxConcurrent && !HasBeenPulled(stage._in))
                    {
                        Pull(stage._in);
                    }
                },
                onUpstreamFinish: () =>
                {

                    _upstreamDone = true;

                    if (_activeSinks.Count == 0 && _readyQueue.Count == 0)
                    {

                        CompleteStage();
                    }
                },
                onUpstreamFailure: ex =>
                {
                    Log.Warning("MergeSubstreamsStage: Upstream failure absorbed: {0}", ex.Message);

                    // Mark upstream as done so substream completion callbacks
                    // can tear down the stage. Without this, zombie substream
                    // actors linger after materializer shutdown.
                    _upstreamDone = true;

                    if (_activeSinks.Count == 0 && _readyQueue.Count == 0)
                    {
                        CompleteStage();
                    }
                });

            SetHandler(stage._out,
                onPull: () =>
                {
                    // First try to drain from a substream that already has data ready.
                    // A sink may have completed (onUpstreamFinish) while its last pushed
                    // element was parked in _readyQueue. We must still Grab() that element
                    // — only skip Pull() since the sink is done.
                    while (_readyQueue.TryDequeue(out var readySink))
                    {
                        var isActive = _activeSinks.Contains(readySink);
                        var elem = readySink.Grab();
                        Push(stage._out, elem);
                        if (isActive)
                        {
                            readySink.Pull();
                        }

                        return;
                    }

                    // No ready sinks — pull all active sinks that haven't been pulled
                    foreach (var sink in _activeSinks)
                    {
                        if (sink is { HasBeenPulled: false, IsClosed: false })
                        {
                            sink.Pull();
                        }
                    }

                    // Also try to accept more substreams
                    if (!_upstreamDone && !HasBeenPulled(stage._in) && _activeSinks.Count < _stage._maxConcurrent)
                    {
                        Pull(stage._in);
                    }

                    // All substreams may have completed while their elements were
                    // in _readyQueue. Now that the queue is drained, check completion.
                    if (_upstreamDone && _activeSinks.Count == 0 && _readyQueue.Count == 0)
                    {
                        CompleteStage();
                    }
                });
        }

        public override void PreStart()
        {
            Pull(_stage._in);
        }

        private void MaterializeSubstream(Source<T, NotUsed> source)
        {
            var subSink = new SubSinkInlet<T>(this, $"MergeSubstreams.SubSink.{_activeSinks.Count}");
            _activeSinks.Add(subSink);

            subSink.SetHandler(new LambdaInHandler(
                onPush: () =>
                {
                    if (IsAvailable(_stage._out))
                    {
                        var elem = subSink.Grab();

                        Push(_stage._out, elem);
                        subSink.Pull();
                    }
                    else
                    {

                        // Outlet is busy — park this sink in the ready queue
                        _readyQueue.Enqueue(subSink);
                    }
                },
                onUpstreamFinish: () => OnSubstreamComplete(subSink),
                onUpstreamFailure: ex =>
                {
                    Log.Warning("MergeSubstreamsStage: Substream failed, absorbed: {0}", ex.Message);
                    OnSubstreamComplete(subSink);
                }));

            source.RunWith(Sink.FromGraph(subSink.Sink), SubFusingMaterializer);

            // Start pulling from this substream immediately
            subSink.Pull();
        }

        private void OnSubstreamComplete(SubSinkInlet<T> subSink)
        {
            _activeSinks.Remove(subSink);


            if (_upstreamDone && _activeSinks.Count == 0 && _readyQueue.Count == 0)
            {

                CompleteStage();
                return;
            }

            if (!_upstreamDone && !HasBeenPulled(_stage._in) && _activeSinks.Count < _stage._maxConcurrent)
            {
                Pull(_stage._in);
            }
        }
    }
}
