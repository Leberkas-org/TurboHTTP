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
    private readonly int _maxSubstreamsPerKey;

    public GroupByRequestKeyStage(
        Func<T, RequestEndpoint> keyFor,
        int maxSubstreams = -1,
        int maxSubstreamsPerKey = 1)
    {
        _keyFor = keyFor ?? throw new ArgumentNullException(nameof(keyFor));
        _maxSubstreams = maxSubstreams;
        _maxSubstreamsPerKey = maxSubstreamsPerKey < 1 ? 1 : maxSubstreamsPerKey;
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

        /// <summary>True when this slot can accept a new item immediately.</summary>
        public bool HasCapacity => !IsDead && !Offering;
    }

    private sealed class SubflowGroup
    {
        public readonly List<SubflowState> Slots = new();

        /// <summary>Returns the first slot that has capacity, or null.</summary>
        public SubflowState? FindCapacitySlot()
            => Slots.Find(s => s.HasCapacity);

        /// <summary>Returns the alive slot with the fewest pending items, or null.</summary>
        public SubflowState? FindLeastLoaded()
        {
            SubflowState? best = null;
            foreach (var slot in Slots)
            {
                if (slot.IsDead)
                {
                    continue;
                }

                if (best == null || slot.Pending.Count < best.Pending.Count)
                {
                    best = slot;
                }
            }

            return best;
        }

        /// <summary>Removes all dead slots from the group in-place.</summary>
        public void RemoveDead() => Slots.RemoveAll(s => s.IsDead);
    }

    private sealed class Logic : GraphStageLogic
    {
        private readonly GroupByRequestKeyStage<T> _stage;
        private readonly int _queueSize;
        private readonly Dictionary<RequestEndpoint, SubflowGroup> _subflows = new();
        private readonly Queue<Source<T, NotUsed>> _pendingSources = new();
        private Action<(RequestEndpoint Key, T Item, bool Success, SubflowState State)>? _onOfferComplete;
        private Action<NotUsed>? _onSubstreamDied;
        private bool _upstreamFinished;

        public Logic(GroupByRequestKeyStage<T> stage, Attributes inheritedAttributes) : base(stage.Shape)
        {
            _stage = stage;
            var queueAttr = inheritedAttributes.GetAttribute(new TurboAttributes.SubstreamQueueSize(1));
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

            _onOfferComplete =
                GetAsyncCallback<(RequestEndpoint Key, T Item, bool Success, SubflowState State)>(tuple =>
                {
                    var (key, item, success, originState) = tuple;

                    if (!_subflows.TryGetValue(key, out var group))
                    {
                        return;
                    }

                    // Guard against stale callbacks: if the slot was removed from the group
                    // since this offer started, the callback belongs to a dead/removed slot.
                    if (!group.Slots.Contains(originState))
                    {
                        // Stale callback. If the item failed, route it to the current group.
                        if (!success)
                        {
                            var target = group.FindLeastLoaded();
                            if (target != null)
                            {
                                target.Pending.Enqueue(item);
                                if (!target.Offering)
                                {
                                    DrainPending(key, target);
                                }
                            }
                        }

                        return;
                    }

                    originState.Offering = false;

                    if (!success)
                    {
                        // Queue is dead — re-enqueue the failed item and handle the dead slot.
                        Log.Debug("GroupByHostKeyStage: offer failed for key={0}:{1}, handling dead slot", key.Host,
                            key.Port);
                        originState.Pending.Enqueue(item);
                        HandleDeadSlot(key, group, originState);
                        return;
                    }

                    DrainPending(key, originState);

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
            foreach (var group in _subflows.Values)
            {
                foreach (var state in group.Slots)
                {
                    if (!state.IsDead && (state.Pending.Count > 0 || state.Offering))
                    {
                        Log.Debug("GroupByHostKeyStage: TryFinish deferred — subflows still draining");
                        return; // still draining
                    }
                }
            }

            // Complete all live substream queues.
            foreach (var group in _subflows.Values)
            {
                foreach (var state in group.Slots)
                {
                    if (!state.IsDead)
                    {
                        state.Queue.Complete();
                    }
                }
            }

            Log.Debug("GroupByHostKeyStage: completing stage, {0} groups", _subflows.Count);
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

            var aliveStates = _subflows.Values
                .SelectMany(g => g.Slots)
                .Where(s => !s.IsDead)
                .ToList();

            if (aliveStates.Count > 0)
            {
                var idleAlive = aliveStates.Count(s => !s.Offering && s.Pending.Count == 0);
                Log.Debug(
                    "GroupByHostKeyStage: deferring completion, {0} substreams still alive ({1} idle but not yet dead)",
                    aliveStates.Count, idleAlive);

                // Register a callback on each alive substream's WatchTask so we
                // re-check once it dies.  Without this, nobody would re-invoke
                // TryCompleteStage after TryFinish has already completed the queues.
                var callback = _onSubstreamDied!;
                foreach (var state in aliveStates)
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

            if (!_subflows.TryGetValue(key, out var group))
            {
                // No group exists — check total limit then create fresh group.
                var totalSlots = _subflows.Values.Sum(g => g.Slots.Count);
                if (_stage._maxSubstreams > 0 && totalSlots >= _stage._maxSubstreams)
                {
                    throw new TooManySubstreamsOpenException();
                }

                group = new SubflowGroup();
                _subflows[key] = group;
                CreateSubstreamInGroup(key, group, item);
            }
            else
            {
                // Try to find a slot that is ready to accept work.
                var capSlot = group.FindCapacitySlot();
                if (capSlot != null)
                {
                    Log.Debug("GroupByHostKeyStage: routed to existing slot key={0}:{1}", key.Host, key.Port);
                    capSlot.Pending.Enqueue(item);
                    if (!capSlot.Offering)
                    {
                        DrainPending(key, capSlot);
                    }
                }
                else
                {
                    // No slot with capacity — clean dead slots first.
                    group.RemoveDead();

                    var totalSlots = _subflows.Values.Sum(g => g.Slots.Count);
                    var canCreate = group.Slots.Count < _stage._maxSubstreamsPerKey &&
                                    (_stage._maxSubstreams <= 0 || totalSlots < _stage._maxSubstreams);

                    if (canCreate)
                    {
                        Log.Debug("GroupByHostKeyStage: creating additional slot for key={0}:{1}, slot={2}", key.Host,
                            key.Port, group.Slots.Count + 1);
                        CreateSubstreamInGroup(key, group, item);
                    }
                    else
                    {
                        // All limits reached — route to least-loaded slot.
                        var leastLoaded = group.FindLeastLoaded();
                        if (leastLoaded != null)
                        {
                            Log.Debug("GroupByHostKeyStage: all slots busy, routing to least-loaded key={0}:{1}",
                                key.Host, key.Port);
                            leastLoaded.Pending.Enqueue(item);
                            if (!leastLoaded.Offering)
                            {
                                DrainPending(key, leastLoaded);
                            }
                        }
                        else
                        {
                            // All slots dead (edge case: limits hit but no alive slot).
                            // Create a replacement to avoid losing the item.
                            CreateSubstreamInGroup(key, group, item);
                        }
                    }
                }
            }

            if (!HasBeenPulled(_stage._in) && !IsClosed(_stage._in) && _pendingSources.Count == 0)
            {
                Pull(_stage._in);
            }
        }

        private void CreateSubstreamInGroup(RequestEndpoint key, SubflowGroup group, T item)
        {
            Log.Debug("GroupByHostKeyStage: creating new substream key={0}:{1}, total={2}", key.Host, key.Port,
                _subflows.Count);

            var (matQueue, source) = Source
                .Queue<T>(_queueSize, OverflowStrategy.Backpressure)
                .PreMaterialize(SubFusingMaterializer);

            var state = new SubflowState(matQueue);
            group.Slots.Add(state);

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

        private void HandleDeadSlot(RequestEndpoint key, SubflowGroup group, SubflowState deadSlot)
        {
            group.Slots.Remove(deadSlot);

            if (deadSlot.Pending.Count == 0)
            {
                if (group.Slots.Count == 0)
                {
                    _subflows.Remove(key);
                }

                if (_upstreamFinished)
                {
                    TryFinish();
                    TryCompleteStage();
                }

                return;
            }

            // Transfer pending items to an alive slot, or create a new one.
            var aliveSlot = group.Slots.Find(s => !s.IsDead);
            if (aliveSlot != null)
            {
                while (deadSlot.Pending.TryDequeue(out var pending))
                {
                    aliveSlot.Pending.Enqueue(pending);
                }

                if (!aliveSlot.Offering)
                {
                    DrainPending(key, aliveSlot);
                }
            }
            else
            {
                // No alive slot — create a replacement using first pending item as seed.
                var seedItem = deadSlot.Pending.Dequeue();
                CreateSubstreamInGroup(key, group, seedItem);

                // Transfer remaining items to the newly created slot.
                var newSlot = group.Slots.LastOrDefault();
                if (newSlot != null)
                {
                    while (deadSlot.Pending.TryDequeue(out var pending))
                    {
                        newSlot.Pending.Enqueue(pending);
                    }
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
                if (_subflows.TryGetValue(key, out var grp))
                {
                    HandleDeadSlot(key, grp, state);
                }

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