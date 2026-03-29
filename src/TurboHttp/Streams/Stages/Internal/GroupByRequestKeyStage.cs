using Akka;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.Stage;
using TurboHttp.Internal;

namespace TurboHttp.Streams.Stages.Internal;

internal sealed class GroupByRequestKeyStage<T> : GraphStage<FlowShape<T, Source<T, NotUsed>>>
{
    private readonly Inlet<T> _in = new("GroupByRequestKey.In");
    private readonly Outlet<Source<T, NotUsed>> _out = new("GroupByRequestKey.Out");
    public override FlowShape<T, Source<T, NotUsed>> Shape { get; }

    private readonly Func<T, RequestEndpoint> _keyFor;
    private readonly int _maxSubstreams;
    private readonly int _defaultQueueSize;

    public GroupByRequestKeyStage(Func<T, RequestEndpoint> keyFor, int maxSubstreams = -1, int queueSize = 64)
    {
        _keyFor = keyFor ?? throw new ArgumentNullException(nameof(keyFor));
        _maxSubstreams = maxSubstreams;
        _defaultQueueSize = queueSize;
        Shape = new FlowShape<T, Source<T, NotUsed>>(_in, _out);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this, inheritedAttributes);

    private sealed class SubflowState
    {
        public readonly ISourceQueueWithComplete<T> Queue;
        public readonly Task WatchTask;
        public readonly Queue<T> Pending = new();
        public bool Offering;
        public bool WatchRegistered;

        public SubflowState(ISourceQueueWithComplete<T> queue)
        {
            Queue = queue;
            WatchTask = queue.WatchCompletionAsync();
        }

        public bool IsDead => WatchTask.IsCompleted;
    }

    private sealed class Logic : GraphStageLogic
    {
        private readonly GroupByRequestKeyStage<T> _stage;
        private readonly int _queueSize;
        private readonly Dictionary<RequestEndpoint, SubflowState> _subflows = new();
        private readonly Queue<Source<T, NotUsed>> _pendingSources = new();
        private Action<(RequestEndpoint Key, T Item, bool Success, SubflowState State)>? _onOfferComplete;
        private Action<NotUsed>? _onSubstreamDied;
        private bool _upstreamFinished;

        public Logic(GroupByRequestKeyStage<T> stage, Attributes inheritedAttributes) : base(stage.Shape)
        {
            _stage = stage;
            var queueAttr = inheritedAttributes.GetAttribute(
                new TurboAttributes.SubstreamQueueSize(stage._defaultQueueSize));
            _queueSize = queueAttr.Size;

            SetHandler(stage._in,
                onPush: HandlePush,
                onUpstreamFinish: () =>
                {
                    _upstreamFinished = true;
                    TryFinish();
                },
                onUpstreamFailure: ex =>
                {
                    // Absorb — in HTTP/1.0 every connection close propagates as upstream failure.
                    // Dead substream detection (WatchTask.IsCompleted) handles recovery.
                    // Mark upstream as finished so TryFinish can complete all subflow queues,
                    // preventing zombie actors when a Processor actor terminates abruptly.
                    Log.Warning("GroupByHostKeyStage: Upstream failure absorbed: {0}", ex.Message);
                    _upstreamFinished = true;
                    TryFinish();
                });

            SetHandler(stage._out, onPull: HandleOutPull);
        }

        public override void PreStart()
        {
            _onSubstreamDied = GetAsyncCallback<NotUsed>(_ => TryCompleteStage());

            _onOfferComplete = GetAsyncCallback<(RequestEndpoint Key, T Item, bool Success, SubflowState State)>(tuple =>
            {
                var (key, item, success, originState) = tuple;

                if (!_subflows.TryGetValue(key, out var currentState))
                {
                    return;
                }

                // Guard against stale callbacks: if the substream was replaced since this
                // offer started, the callback belongs to the OLD state, not the current one.
                if (currentState != originState)
                {
                    // Stale callback. If the item failed, route it to the current substream.
                    if (!success)
                    {
                        currentState.Pending.Enqueue(item);
                        if (!currentState.Offering)
                        {
                            DrainPending(key, currentState);
                        }
                    }

                    return;
                }

                currentState.Offering = false;

                if (!success)
                {
                    // Queue is dead — re-enqueue the failed item and replace substream.
                    Log.Debug("GroupByHostKeyStage: offer failed for key={0}:{1}, replacing substream", key.Host, key.Port);
                    currentState.Pending.Enqueue(item);
                    ReplaceSubstream(key, currentState);
                    return;
                }

                DrainPending(key, currentState);

                if (_upstreamFinished)
                {
                    TryFinish();
                    TryCompleteStage();
                }
                else if (!HasBeenPulled(_stage._in) && !IsClosed(_stage._in))
                {
                    Pull(_stage._in);
                }
            });
        }

        // Defers completion until all live subflows are drained.
        private void TryFinish()
        {
            if (_subflows.Values.Any(state => !state.IsDead && (state.Pending.Count > 0 || state.Offering)))
            {
                Log.Debug("GroupByHostKeyStage: TryFinish deferred — subflows still draining");
                return; // still draining
            }

            // Complete all live substream queues.
            foreach (var state in _subflows.Values)
            {
                if (!state.IsDead)
                {
                    state.Queue.Complete();
                }
            }

            Log.Debug("GroupByHostKeyStage: completing stage, {0} substreams", _subflows.Count);
            TryCompleteStage();
        }

        // Two-phase completion: only kill stage actor scope when every substream
        // has actually died. This gives downstream feature BidiStages (Retry,
        // Cache) time to emit re-injections after upstream finishes.
        //
        // Liveness guard (DL-009 + DL-010): a substream that is idle (!Offering,
        // Pending.Count == 0) but not yet dead (!IsDead) may still have async
        // work running in downstream stages (RetryBidi retries, CacheBidi body
        // reads).  We must not complete until WatchTask fires for every substream.
        private void TryCompleteStage()
        {
            if (!_upstreamFinished)
            {
                return;
            }

            var aliveSubstreams = _subflows.Values.Where(state => !state.IsDead).ToList();

            if (aliveSubstreams.Count > 0)
            {
                var idleAlive = aliveSubstreams.Count(s => !s.Offering && s.Pending.Count == 0);
                Log.Debug(
                    "GroupByHostKeyStage: deferring completion, {0} substreams still alive ({1} idle but not yet dead)",
                    aliveSubstreams.Count, idleAlive);

                // Register a callback on each alive substream's WatchTask so we
                // re-check once it dies.  Without this, nobody would re-invoke
                // TryCompleteStage after TryFinish has already completed the queues.
                var callback = _onSubstreamDied!;
                foreach (var state in aliveSubstreams)
                {
                    if (!state.WatchRegistered)
                    {
                        state.WatchRegistered = true;
                        state.WatchTask.ContinueWith(
                            _ => callback(NotUsed.Instance),
                            TaskContinuationOptions.ExecuteSynchronously);
                    }
                }

                return;
            }

            Log.Debug("GroupByHostKeyStage: all substreams dead, completing stage");
            CompleteStage();
        }

        private void HandleOutPull()
        {
            if (_pendingSources.TryDequeue(out var bufferedSource))
            {
                Push(_stage._out, bufferedSource);
            }
            else if (!HasBeenPulled(_stage._in) && !IsClosed(_stage._in))
            {
                Pull(_stage._in);
            }
        }

        private void HandlePush()
        {
            var item = Grab(_stage._in);
            var key = _stage._keyFor(item);

            if (_subflows.TryGetValue(key, out var existing) && !existing.IsDead)
            {
                Log.Debug("GroupByHostKeyStage: routed to existing substream key={0}:{1}", key.Host, key.Port);
                existing.Pending.Enqueue(item);

                if (!existing.Offering)
                {
                    DrainPending(key, existing);
                }
            }
            else
            {
                // Either no subflow exists, or existing one is dead — create fresh.
                if (existing != null)
                {
                    _subflows.Remove(key);
                }

                if (_stage._maxSubstreams > 0 && _subflows.Count >= _stage._maxSubstreams)
                {
                    throw new TooManySubstreamsOpenException();
                }

                CreateSubstream(key, item);
            }

            if (!HasBeenPulled(_stage._in) && !IsClosed(_stage._in) && _pendingSources.Count == 0)
            {
                Pull(_stage._in);
            }
        }

        private void CreateSubstream(RequestEndpoint key, T item)
        {
            Log.Debug("GroupByHostKeyStage: creating new substream key={0}:{1}, total={2}", key.Host, key.Port, _subflows.Count + 1);

            var (matQueue, source) = Source
                .Queue<T>(_queueSize, OverflowStrategy.Backpressure)
                .PreMaterialize(SubFusingMaterializer);

            var state = new SubflowState(matQueue);
            _subflows[key] = state;

            if (IsAvailable(_stage._out))
            {
                Push(_stage._out, source);
            }
            else
            {
                _pendingSources.Enqueue(source);
            }

            state.Pending.Enqueue(item);
            DrainPending(key, state);
        }

        private void ReplaceSubstream(RequestEndpoint key, SubflowState deadState)
        {
            _subflows.Remove(key);

            if (deadState.Pending.Count == 0)
            {
                if (_upstreamFinished)
                {
                    TryFinish();
                    TryCompleteStage();
                }

                return;
            }

            // Take first pending item as seed for new substream.
            var seedItem = deadState.Pending.Dequeue();
            CreateSubstream(key, seedItem);

            // Transfer remaining pending items.
            if (deadState.Pending.Count > 0 && _subflows.TryGetValue(key, out var newState))
            {
                while (deadState.Pending.TryDequeue(out var pending))
                {
                    newState.Pending.Enqueue(pending);
                }
            }

            if (_upstreamFinished)
            {
                TryCompleteStage();
            }
        }

        private void DrainPending(RequestEndpoint key, SubflowState state)
        {
            if (state.Offering || state.Pending.Count == 0)
            {
                return;
            }

            // Fast path: if queue is already dead, replace immediately instead of
            // waiting 5s for OfferAsync to timeout via Ask pattern.
            if (state.IsDead)
            {
                ReplaceSubstream(key, state);
                return;
            }

            var item = state.Pending.Dequeue();
            state.Offering = true;

            var offerCallback = _onOfferComplete!;
            var capturedState = state;

            var offerTask = state.Queue.OfferAsync(item);

            // Race the offer against queue death.  If the Source.Queue actor dies
            // between the IsDead check above and OfferAsync, the Ask pattern inside
            // OfferAsync would wait for a 5s timeout — long enough to trip the test
            // timeout and appear as a deadlock.  By racing against WatchTask, we
            // detect the dead queue in milliseconds instead of seconds.
            Task.WhenAny(offerTask, state.WatchTask).ContinueWith(_ =>
            {
                var success = offerTask.IsCompletedSuccessfully && offerTask.Result is QueueOfferResult.Enqueued;
                offerCallback((key, item, success, capturedState));
            }, TaskContinuationOptions.ExecuteSynchronously);
        }
    }
}
