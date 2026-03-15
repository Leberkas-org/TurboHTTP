using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.Stage;
using TurboHttp.IO.Stages;

namespace TurboHttp.Streams.Stages;

public sealed class GroupByHostKeyStage<T> : GraphStage<FlowShape<T, Source<T, NotUsed>>>
{
    public Inlet<T> In { get; } = new("GroupByHostKey.In");
    public Outlet<Source<T, NotUsed>> Out { get; } = new("GroupByHostKey.Out");
    public override FlowShape<T, Source<T, NotUsed>> Shape { get; }

    private readonly Func<T, HostKey> _keyFor;
    private readonly int _maxSubstreams;

    public GroupByHostKeyStage(Func<T, HostKey> keyFor, int maxSubstreams = -1)
    {
        _keyFor = keyFor ?? throw new ArgumentNullException(nameof(keyFor));
        _maxSubstreams = maxSubstreams;
        Shape = new FlowShape<T, Source<T, NotUsed>>(In, Out);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class SubflowState(ISourceQueueWithComplete<T> queue)
    {
        public readonly ISourceQueueWithComplete<T> Queue = queue;
        public readonly Queue<T> Pending = new();
        public bool Offering;
    }

    private sealed class Logic : GraphStageLogic
    {
        private readonly GroupByHostKeyStage<T> _stage;
        private readonly Dictionary<HostKey, SubflowState> _subflows = new();
        private readonly Queue<Source<T, NotUsed>> _pendingSources = new();
        private Action<HostKey>? _onOfferComplete;
        private bool _upstreamFinished;

        public Logic(GroupByHostKeyStage<T> stage) : base(stage.Shape)
        {
            _stage = stage;

            SetHandler(stage.In,
                onPush: HandlePush,
                onUpstreamFinish: () =>
                {
                    _upstreamFinished = true;
                    TryFinish();
                },
                onUpstreamFailure: ex =>
                {
                    foreach (var state in _subflows.Values)
                    {
                        state.Queue.Fail(ex);
                    }

                    FailStage(ex);
                });

            SetHandler(stage.Out, onPull: HandleOutPull);
        }

        public override void PreStart()
        {
            _onOfferComplete = GetAsyncCallback<HostKey>(key =>
            {
                if (!_subflows.TryGetValue(key, out var state))
                {
                    return;
                }

                state.Offering = false;
                DrainPending(key, state);

                if (_upstreamFinished)
                {
                    TryFinish();
                }
                else if (!HasBeenPulled(_stage.In) && !IsClosed(_stage.In))
                {
                    Pull(_stage.In);
                }
            });
        }

        // Defers completion until all per-subflow pending queues are fully drained.
        private void TryFinish()
        {
            if (_subflows.Values.Any(state => state.Pending.Count > 0 || state.Offering))
            {
                return; // still draining
            }

            foreach (var state in _subflows.Values)
            {
                state.Queue.Complete();
            }

            CompleteStage();
        }

        private void HandleOutPull()
        {
            if (_pendingSources.TryDequeue(out var bufferedSource))
            {
                Push(_stage.Out, bufferedSource);
            }
            else if (!HasBeenPulled(_stage.In))
            {
                Pull(_stage.In);
            }
        }

        private void HandlePush()
        {
            var item = Grab(_stage.In);
            var key = _stage._keyFor(item);

            if (_subflows.TryGetValue(key, out var existing))
            {
                existing.Pending.Enqueue(item);

                if (!existing.Offering)
                {
                    DrainPending(key, existing);
                }
            }
            else
            {
                if (_stage._maxSubstreams > 0 && _subflows.Count >= _stage._maxSubstreams)
                {
                    throw new TooManySubstreamsOpenException();
                }

                var (matQueue, source) = Source
                    .Queue<T>(16, OverflowStrategy.Backpressure)
                    .PreMaterialize(SubFusingMaterializer);

                var state = new SubflowState(matQueue);
                _subflows[key] = state;

                if (IsAvailable(_stage.Out))
                {
                    Push(_stage.Out, source);
                }
                else
                {
                    _pendingSources.Enqueue(source);
                }

                state.Pending.Enqueue(item);
                DrainPending(key, state);
            }

            if (!HasBeenPulled(_stage.In) && _pendingSources.Count == 0)
            {
                Pull(_stage.In);
            }
        }

        private void DrainPending(HostKey key, SubflowState state)
        {
            if (state.Offering || state.Pending.Count == 0)
            {
                return;
            }

            var item = state.Pending.Dequeue();
            state.Offering = true;

            _ = state.Queue.OfferAsync(item).ContinueWith(
                _ => _onOfferComplete!(key),
                TaskContinuationOptions.ExecuteSynchronously);
        }
    }
}
