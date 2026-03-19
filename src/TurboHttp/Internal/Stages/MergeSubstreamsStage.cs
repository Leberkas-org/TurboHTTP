using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.Stage;

namespace TurboHttp.Internal.Stages;

internal sealed class MergeSubstreamsStage<T> : GraphStage<FlowShape<Source<T, NotUsed>, T>>
{
    private readonly int _maxConcurrent;

    private readonly Inlet<Source<T, NotUsed>> _inlet = new("merge.substreams.in");
    private readonly Outlet<T> _outlet = new("merge.substreams.out");
    public override FlowShape<Source<T, NotUsed>, T> Shape { get; }


    public MergeSubstreamsStage(int maxConcurrent)
    {
        _maxConcurrent = maxConcurrent;
        Shape = new FlowShape<Source<T, NotUsed>, T>(_inlet, _outlet);
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

            SetHandler(stage._inlet,
                onPush: () =>
                {
                    var source = Grab(stage._inlet);
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

                    if (_active < _stage._maxConcurrent && !HasBeenPulled(stage._inlet))
                    {
                        Pull(stage._inlet);
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

            SetHandler(stage._outlet,
                onPull: () =>
                {
                    if (_buffer.TryDequeue(out var elem))
                    {
                        Push(stage._outlet, elem);
                    }
                    else if (!_upstreamDone && !HasBeenPulled(stage._inlet) && _active < _stage._maxConcurrent)
                    {
                        Pull(stage._inlet);
                    }
                    // else: wait for next _onElement callback
                });
        }

        public override void PreStart()
        {
            _onElement = GetAsyncCallback<T>(elem =>
            {
                if (IsAvailable(_stage._outlet))
                {
                    Push(_stage._outlet, elem);
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
                    case false when !HasBeenPulled(_stage._inlet) && _active < _stage._maxConcurrent:
                        Pull(_stage._inlet);
                        break;
                }
            });

            _onSubstreamFailed = GetAsyncCallback<Exception>(FailStage);

            Pull(_stage._inlet);
        }
    }
}