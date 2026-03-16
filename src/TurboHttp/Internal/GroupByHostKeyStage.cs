using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.Stage;
using TurboHttp.IO.Stages;

namespace TurboHttp.Internal;

internal sealed class GroupByHostKeyStage<T> : GraphStage<FlowShape<T, Source<T, NotUsed>>>
{
    private readonly Inlet<T> _inlet = new("groupby.hostkey.in");
    private readonly Outlet<Source<T, NotUsed>> _outlet = new("groupby.hostkey.in");
    public override FlowShape<T, Source<T, NotUsed>> Shape { get; }

    private readonly Func<T, HostKey> _keyFor;
    private readonly int _maxSubstreams;

    public GroupByHostKeyStage(Func<T, HostKey> keyFor, int maxSubstreams = -1)
    {
        _keyFor = keyFor ?? throw new ArgumentNullException(nameof(keyFor));
        _maxSubstreams = maxSubstreams;
        Shape = new FlowShape<T, Source<T, NotUsed>>(_inlet, _outlet);
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

            SetHandler(stage._inlet,
                onPush: HandlePush,
                onUpstreamFinish: () =>
                {
                    _upstreamFinished = true;
                    TryFinish();
                });

            SetHandler(stage._outlet, onPull: HandleOutPull);
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
                else if (!HasBeenPulled(_stage._inlet) && !IsClosed(_stage._inlet))
                {
                    Pull(_stage._inlet);
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
                Push(_stage._outlet, bufferedSource);
            }
            else if (!HasBeenPulled(_stage._inlet))
            {
                Pull(_stage._inlet);
            }
        }

        private void HandlePush()
        {
            var item = Grab(_stage._inlet);
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

                if (IsAvailable(_stage._outlet))
                {
                    Push(_stage._outlet, source);
                }
                else
                {
                    _pendingSources.Enqueue(source);
                }

                state.Pending.Enqueue(item);
                DrainPending(key, state);
            }

            if (!HasBeenPulled(_stage._inlet) && _pendingSources.Count == 0)
            {
                Pull(_stage._inlet);
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

            _ = state.Queue
                .OfferAsync(item)
                .ContinueWith(_ => _onOfferComplete!(key), TaskContinuationOptions.ExecuteSynchronously);
        }
    }
}