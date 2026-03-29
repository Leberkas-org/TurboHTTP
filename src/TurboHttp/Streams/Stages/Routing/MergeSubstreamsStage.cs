using Akka;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.Stage;

namespace TurboHttp.Streams.Stages.Routing;

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

        private readonly Queue<T> _buffer = new();
        private int _active;
        private bool _upstreamDone;

        private Action<T>? _onElement;
        private Action? _onSubstreamDone;
        private Action<Exception>? _onSubstreamFailed;

        public Logic(MergeSubstreamsStage<T> stage) : base(stage.Shape)
        {
            _stage = stage;

            SetHandler(stage._in,
                onPush: () =>
                {
                    var source = Grab(stage._in);
                    _active++;

                    source
                        .RunWith(Sink.ForEach<T>(elem => _onElement!(elem)), SubFusingMaterializer)
                        .ContinueWith(
                            t =>
                            {
                                if (t.IsFaulted)
                                {
                                    _onSubstreamFailed!(t.Exception!.GetBaseException());
                                }
                                else
                                {
                                    _onSubstreamDone!();
                                }
                            }, TaskContinuationOptions.ExecuteSynchronously);

                    if (_active < _stage._maxConcurrent && !HasBeenPulled(stage._in))
                    {
                        Pull(stage._in);
                    }
                },
                onUpstreamFinish: () =>
                {
                    _upstreamDone = true;

                    if (_active == 0 && _buffer.Count == 0)
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

                    if (_active == 0 && _buffer.Count == 0)
                    {
                        CompleteStage();
                    }
                });

            SetHandler(stage._out,
                onPull: () =>
                {
                    if (_buffer.TryDequeue(out var elem))
                    {
                        Push(stage._out, elem);
                    }
                    else if (!_upstreamDone && !HasBeenPulled(stage._in) && _active < _stage._maxConcurrent)
                    {
                        Pull(stage._in);
                    }
                    // else: wait for next _onElement callback
                });
        }

        public override void PreStart()
        {
            _onElement = GetAsyncCallback<T>(elem =>
            {
                if (IsAvailable(_stage._out))
                {
                    Push(_stage._out, elem);
                }
                else
                {
                    _buffer.Enqueue(elem);
                }
            });

            _onSubstreamDone = GetAsyncCallback(() =>
            {
                _active--;

                switch (_upstreamDone)
                {
                    case true when _active == 0 && _buffer.Count == 0:
                        CompleteStage();
                        return;
                    case false when !HasBeenPulled(_stage._in) && _active < _stage._maxConcurrent:
                        Pull(_stage._in);
                        break;
                }
            });

            _onSubstreamFailed = GetAsyncCallback<Exception>(ex =>
            {
                _active--;
                Log.Warning("MergeSubstreamsStage: Substream failed, absorbed: {0}", ex.Message);

                if (_upstreamDone && _active == 0 && _buffer.Count == 0)
                {
                    CompleteStage();
                    return;
                }

                if (!_upstreamDone && !HasBeenPulled(_stage._in) && _active < _stage._maxConcurrent)
                {
                    Pull(_stage._in);
                }
            });

            Pull(_stage._in);
        }
    }
}