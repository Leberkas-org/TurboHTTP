using Akka;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.Stage;
using TurboHTTP.Internal;

namespace TurboHTTP.Streams.Stages.Internal;

internal sealed class GroupByRequestEndpointStage<T> : GraphStage<FlowShape<T, Source<T, NotUsed>>>
{
    internal static readonly HttpRequestOptionsKey<int> ConnectionAffinitySlot =
        new("TurboHTTP.ConnectionAffinitySlot");

    private readonly Inlet<T> _in = new("GroupByRequestKey.In");
    private readonly Outlet<Source<T, NotUsed>> _out = new("GroupByRequestKey.Out");
    public override FlowShape<T, Source<T, NotUsed>> Shape { get; }

    private readonly Func<T, RequestEndpoint> _keyFor;
    private readonly int _maxSubstreams;
    private readonly Func<RequestEndpoint, int> _maxSubstreamsPerKey;
    private readonly Func<RequestEndpoint, int> _maxConcurrencyPerSlot;

    public GroupByRequestEndpointStage(
        Func<T, RequestEndpoint> keyFor,
        int maxSubstreams = -1,
        Func<RequestEndpoint, int>? maxSubstreamsPerKey = null,
        Func<RequestEndpoint, int>? maxConcurrencyPerSlot = null)
    {
        _keyFor = keyFor ?? throw new ArgumentNullException(nameof(keyFor));
        _maxSubstreams = maxSubstreams;
        _maxSubstreamsPerKey = maxSubstreamsPerKey ?? (_ => 1);
        _maxConcurrencyPerSlot = maxConcurrencyPerSlot ?? (_ => 1);
        Shape = new FlowShape<T, Source<T, NotUsed>>(_in, _out);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this, inheritedAttributes);

    private sealed class SubflowState
    {
        private static int _nextSlotId;

        public readonly int SlotId = Interlocked.Increment(ref _nextSlotId);
        public readonly ChannelSourceStage<T> ChannelStage;

        /// <summary>
        /// Aliases <see cref="ChannelSourceStage{T}.Completion"/> for dead-slot detection.
        /// Replaces the former <c>ISourceQueueWithComplete.WatchCompletionAsync()</c> task.
        /// </summary>
        public readonly Task WatchTask;

        public readonly Queue<T> Pending = new();

        /// <summary>
        /// Number of items currently buffered in the channel and not yet consumed by the downstream stage.
        /// Incremented by <c>DrainPending</c> on each successful write; decremented by <c>_onChannelConsumed</c>.
        /// </summary>
        public int OfferingCount;

        /// <summary>Maximum number of items that may be simultaneously in-flight in the channel.</summary>
        public readonly int MaxOffering;

        public bool WatchRegistered;

        public SubflowState(ChannelSourceStage<T> channelStage, int maxOffering = 1)
        {
            ChannelStage = channelStage;
            WatchTask = channelStage.Completion;
            MaxOffering = maxOffering;
        }

        public bool IsDead => WatchTask.IsCompleted;

        /// <summary>True when this slot can accept at least one more item.</summary>
        public bool HasCapacity => !IsDead && OfferingCount < MaxOffering;

        /// <summary>Total items queued (channel + local pending) for load-balancing purposes.</summary>
        public int TotalPending => Pending.Count + ChannelStage.Count;
    }

    private sealed class SubflowGroup
    {
        private readonly Dictionary<int, SubflowState> _slotsById = new();

        public int Count => _slotsById.Count;
        public IEnumerable<SubflowState> AllSlots => _slotsById.Values;
        public SubflowState? LastAdded { get; private set; }

        public void AddSlot(SubflowState state)
        {
            _slotsById[state.SlotId] = state;
            LastAdded = state;
        }

        public void RemoveSlot(SubflowState state)
        {
            _slotsById.Remove(state.SlotId);
            if (ReferenceEquals(LastAdded, state))
            {
                LastAdded = null;
            }
        }

        /// <summary>Returns true if the slot is still registered in this group (O(1)).</summary>
        public bool ContainsSlot(SubflowState state)
            => _slotsById.TryGetValue(state.SlotId, out var found) && ReferenceEquals(found, state);

        /// <summary>Returns the first slot that has capacity, or null.</summary>
        public SubflowState? FindCapacitySlot()
        {
            foreach (var slot in _slotsById.Values)
            {
                if (slot.HasCapacity) return slot;
            }

            return null;
        }

        /// <summary>Returns the alive slot with the matching slot ID, or null if not found or dead (O(1)).</summary>
        public SubflowState? FindBySlotId(int slotId)
            => _slotsById.TryGetValue(slotId, out var slot) && !slot.IsDead ? slot : null;

        public SubflowState? FindFirst(Func<SubflowState, bool> predicate)
        {
            foreach (var slot in _slotsById.Values)
            {
                if (predicate(slot))
                {
                    return slot;
                }
            }

            return null;
        }

        /// <summary>Returns the alive slot with the fewest total queued items, or null.</summary>
        public SubflowState? FindLeastLoaded()
        {
            SubflowState? best = null;

            foreach (var slot in _slotsById.Values)
            {
                if (slot.IsDead)
                {
                    continue;
                }

                if (best is null || slot.TotalPending < best.TotalPending)
                {
                    best = slot;
                }
            }

            return best;
        }

        /// <summary>Removes all dead slots from the lookup dictionary. Returns number removed.</summary>
        public int RemoveDead()
        {
            // Check if there are any dead slots first — avoid allocation in the common case.
            var hasDead = false;
            foreach (var slot in _slotsById.Values)
            {
                if (slot.IsDead)
                {
                    hasDead = true;
                    break;
                }
            }

            if (!hasDead)
            {
                return 0;
            }

            // Collect dead slot IDs for removal.
            var dead = new List<int>();
            foreach (var (id, slot) in _slotsById)
            {
                if (slot.IsDead)
                {
                    dead.Add(id);
                }
            }

            foreach (var id in dead)
            {
                _slotsById.Remove(id);
            }

            return dead.Count;
        }
    }

    private sealed class Logic : GraphStageLogic
    {
        private readonly GroupByRequestEndpointStage<T> _stage;
        private readonly Dictionary<RequestEndpoint, SubflowGroup> _subflows = new();
        private readonly Queue<Source<T, NotUsed>> _pendingSources = new();
        private Action<(RequestEndpoint Key, SubflowState State)>? _onChannelConsumed;
        private Action<NotUsed>? _onSubstreamDied;
        private bool _upstreamFinished;
        private int _totalSlotCount;

        public Logic(GroupByRequestEndpointStage<T> stage, Attributes inheritedAttributes) : base(stage.Shape)
        {
            _stage = stage;

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

            // Fired by ChannelSourceStage when it reads an item — meaning the channel slot has
            // capacity again and the next item from the local Pending queue can be forwarded.
            // This replaces the old _onOfferComplete + Task.WhenAny approach, eliminating
            // one Akka actor round-trip per item on the write path.
            _onChannelConsumed =
                GetAsyncCallback<(RequestEndpoint Key, SubflowState State)>(tuple =>
                {
                    var (key, originState) = tuple;

                    if (!_subflows.TryGetValue(key, out var group))
                    {
                        return;
                    }

                    // Guard against stale callbacks from a slot that was already evicted.
                    if (!group.ContainsSlot(originState))
                    {
                        return;
                    }

                    originState.OfferingCount--;

                    if (originState.IsDead)
                    {
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
                foreach (var state in group.AllSlots)
                {
                    if (!state.IsDead && (state.Pending.Count > 0 || state.OfferingCount > 0))
                    {
                        Log.Debug("GroupByHostKeyStage: TryFinish deferred — subflows still draining");
                        return; // still draining
                    }
                }
            }

            // Signal each live substream's channel that no more items will arrive.
            foreach (var group in _subflows.Values)
            {
                foreach (var state in group.AllSlots)
                {
                    if (!state.IsDead)
                    {
                        state.ChannelStage.Writer.TryComplete();
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

            var aliveCount = 0;
            var idleAlive = 0;

            foreach (var group in _subflows.Values)
            {
                foreach (var slot in group.AllSlots)
                {
                    if (slot.IsDead) { continue; }

                    aliveCount++;

                    if (slot is { OfferingCount: 0, Pending.Count: 0 })
                    {
                        idleAlive++;
                    }
                }
            }

            if (aliveCount > 0)
            {
                Log.Debug(
                    "GroupByHostKeyStage: deferring completion, {0} substreams still alive ({1} idle but not yet dead)",
                    aliveCount, idleAlive);

                // Register a callback on each alive substream's WatchTask so we
                // re-check once it dies.  Without this, nobody would re-invoke
                // TryCompleteStage after TryFinish has already completed the queues.
                var callback = _onSubstreamDied!;
                foreach (var group in _subflows.Values)
                {
                    foreach (var state in group.AllSlots)
                    {
                        if (state is { IsDead: false, WatchRegistered: false })
                        {
                            state.WatchRegistered = true;
                            state.WatchTask.ContinueWith(
                                _ => callback(NotUsed.Instance),
                                TaskContinuationOptions.ExecuteSynchronously);
                        }
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
                if (_stage._maxSubstreams > 0 && _totalSlotCount >= _stage._maxSubstreams)
                {
                    throw new TooManySubstreamsOpenException();
                }

                group = new SubflowGroup();
                _subflows[key] = group;
                CreateSubstreamInGroup(key, group, item);
            }
            else
            {
                // Connection affinity: if the request was previously tagged with a slot index
                // (e.g. after redirect/retry re-injection), route it back to the same slot.
                var affinitySlot = TryGetAffinitySlot(item, group);
                if (affinitySlot != null)
                {
                    Log.Debug("GroupByHostKeyStage: affinity hit, routed to slot key={0}:{1}", key.Host, key.Port);
                    affinitySlot.Pending.Enqueue(item);
                    DrainPending(key, affinitySlot);
                }
                // Try to find a slot that is ready to accept work.
                else if (group.FindCapacitySlot() is { } capSlot)
                {
                    Log.Debug("GroupByHostKeyStage: routed to existing slot key={0}:{1}", key.Host, key.Port);
                    TagAffinitySlot(item, capSlot);
                    capSlot.Pending.Enqueue(item);
                    DrainPending(key, capSlot);
                }
                else
                {
                    // No slot with capacity — clean dead slots first.
                    var removed = group.RemoveDead();
                    _totalSlotCount -= removed;

                    var canCreate = group.Count < _stage._maxSubstreamsPerKey(key) &&
                                    (_stage._maxSubstreams <= 0 || _totalSlotCount < _stage._maxSubstreams);

                    if (canCreate)
                    {
                        Log.Debug("GroupByHostKeyStage: creating additional slot for key={0}:{1}, slot={2}", key.Host,
                            key.Port, group.Count + 1);
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
                            TagAffinitySlot(item, leastLoaded);
                            leastLoaded.Pending.Enqueue(item);
                            DrainPending(key, leastLoaded);
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

        /// <summary>
        /// Checks whether the item carries a connection affinity tag and the tagged slot is still alive.
        /// Returns the target slot if affinity applies, null otherwise.
        /// </summary>
        private static SubflowState? TryGetAffinitySlot(T item, SubflowGroup group)
        {
            if (item is HttpRequestMessage request &&
                request.Options.TryGetValue(ConnectionAffinitySlot, out var slotId))
            {
                return group.FindBySlotId(slotId);
            }

            return null;
        }

        /// <summary>
        /// Tags the item with a connection affinity slot ID so that re-injected
        /// requests (redirect/retry) route back to the same slot.
        /// </summary>
        private static void TagAffinitySlot(T item, SubflowState slot)
        {
            if (item is HttpRequestMessage request)
            {
                request.Options.Set(ConnectionAffinitySlot, slot.SlotId);
            }
        }

        private void CreateSubstreamInGroup(RequestEndpoint key, SubflowGroup group, T item)
        {
            Log.Debug("GroupByHostKeyStage: creating new substream key={0}:{1}, total={2}", key.Host, key.Port,
                _subflows.Count);

            // Late-bound capture: the state is created after the channel stage so the
            // consumed callback can reference it without a circular dependency.
            var consumedCallback = _onChannelConsumed!;
            var capturedKey = key;
            SubflowState? capturedState = null;

            var maxOffering = _stage._maxConcurrencyPerSlot(key);
            var channelStage = new ChannelSourceStage<T>(
                capacity: maxOffering,
                onConsumed: () => consumedCallback((capturedKey, capturedState!)));

            var (_, source) = Source.FromGraph(channelStage).PreMaterialize(Materializer);

            var state = new SubflowState(channelStage, maxOffering);
            capturedState = state;

            group.AddSlot(state);
            _totalSlotCount++;

            // Tag the item with the new slot's ID for connection affinity.
            TagAffinitySlot(item, state);

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
            group.RemoveSlot(deadSlot);
            _totalSlotCount--;

            // Recover items that were buffered in the channel but not yet consumed.
            // ChannelSourceStage has terminated at this point, so the writer is completed
            // and DrainRemaining() is safe to call without racing a concurrent reader.
            foreach (var buffered in deadSlot.ChannelStage.DrainRemaining())
            {
                deadSlot.Pending.Enqueue(buffered);
            }

            if (deadSlot.Pending.Count == 0)
            {
                if (group.Count == 0)
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
            var aliveSlot = group.FindFirst(s => !s.IsDead);
            if (aliveSlot != null)
            {
                while (deadSlot.Pending.TryDequeue(out var pending))
                {
                    TagAffinitySlot(pending, aliveSlot);
                    aliveSlot.Pending.Enqueue(pending);
                }

                DrainPending(key, aliveSlot);
            }
            else
            {
                // No alive slot — create a replacement using first pending item as seed.
                var seedItem = deadSlot.Pending.Dequeue();
                CreateSubstreamInGroup(key, group, seedItem);

                // Transfer remaining items to the newly created slot.
                var newSlot = group.LastAdded;
                if (newSlot != null)
                {
                    while (deadSlot.Pending.TryDequeue(out var pending))
                    {
                        TagAffinitySlot(pending, newSlot);
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
            // Fill the channel up to MaxOffering in one pass — no Akka round-trip between items.
            // _onChannelConsumed fires per consumed item and decrements OfferingCount, allowing
            // further draining without waiting for the whole channel to empty first.
            while (state is { HasCapacity: true, Pending.Count: > 0 })
            {
                if (state.IsDead)
                {
                    if (_subflows.TryGetValue(key, out var grp))
                    {
                        HandleDeadSlot(key, grp, state);
                    }
                    return;
                }

                var item = state.Pending.Dequeue();

                if (state.ChannelStage.Writer.TryWrite(item))
                {
                    state.OfferingCount++;
                }
                else
                {
                    // Channel full or completed unexpectedly — re-enqueue and bail.
                    state.Pending.Enqueue(item);
                    if (state.IsDead && _subflows.TryGetValue(key, out var grp))
                    {
                        HandleDeadSlot(key, grp, state);
                    }
                    return;
                }
            }
        }
    }
}