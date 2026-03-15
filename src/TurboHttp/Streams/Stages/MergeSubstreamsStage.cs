using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.Stage;

namespace TurboHttp.Streams.Stages;

public sealed class MergeSubstreamsStage<T> : GraphStage<FlowShape<Source<T, NotUsed>, T>>
{
    private readonly int _maxConcurrent;

    public Inlet<Source<T, NotUsed>> In { get; } = new("MergeSubstreams.In");
    public Outlet<T> Out { get; } = new("MergeSubstreams.Out");
    public override FlowShape<Source<T, NotUsed>, T> Shape { get; }

    public MergeSubstreamsStage(int maxConcurrent)
    {
        _maxConcurrent = maxConcurrent;
        Shape = new FlowShape<Source<T, NotUsed>, T>(In, Out);
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

            SetHandler(stage.In,
                onPush: () =>
                {
                    var source = Grab(stage.In);
                    _active++;

                    source.RunWith(
                            Sink.ForEach<T>(elem => _onElement!(elem)),
                            SubFusingMaterializer)
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
                            },
                            TaskContinuationOptions.ExecuteSynchronously);

                    if (_active < _stage._maxConcurrent && !HasBeenPulled(stage.In))
                    {
                        Pull(stage.In);
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
                onUpstreamFailure: FailStage);

            SetHandler(stage.Out,
                onPull: () =>
                {
                    if (_buffer.TryDequeue(out var elem))
                    {
                        Push(stage.Out, elem);
                    }
                    else if (!_upstreamDone && !HasBeenPulled(stage.In) &&
                             _active < _stage._maxConcurrent)
                    {
                        Pull(stage.In);
                    }
                    // else: wait for next _onElement callback
                });
        }

        public override void PreStart()
        {
            _onElement = GetAsyncCallback<T>(elem =>
            {
                if (IsAvailable(_stage.Out))
                {
                    Push(_stage.Out, elem);
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
                    case false when !HasBeenPulled(_stage.In) && _active < _stage._maxConcurrent:
                        Pull(_stage.In);
                        break;
                }
            });

            _onSubstreamFailed = GetAsyncCallback<Exception>(FailStage);

            Pull(_stage.In);
        }
    }
}
