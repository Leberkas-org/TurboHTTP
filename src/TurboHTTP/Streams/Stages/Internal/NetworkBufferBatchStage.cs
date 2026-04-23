using Akka.Streams;
using Akka.Streams.Stage;
using Servus.Akka.IO;

namespace TurboHTTP.Streams.Stages.Internal;

/// <summary>
/// Batches consecutive <see cref="NetworkBuffer"/> items up to <paramref name="maxWeight"/> bytes
/// into a single larger buffer, reducing downstream write calls.
/// <para>
/// Unlike <c>BatchWeighted</c>, this stage is safe for mixed streams that interleave
/// <see cref="NetworkBuffer"/> with control items (<c>StreamAcquireItem</c>,
/// <c>ConnectionReuseItem</c>, etc.).  When a non-<see cref="NetworkBuffer"/> item arrives
/// while accumulating, the stage flushes the accumulated buffer first and then emits the
/// control item — preserving ordering and never dropping data.
/// </para>
/// <para>
/// When downstream already has pending demand (<c>IsAvailable(out)</c>) at the time a
/// <see cref="NetworkBuffer"/> first arrives, the buffer is pushed immediately rather than
/// stashed — matching <c>BatchWeighted</c>'s "emit on demand" behaviour and preventing
/// the deadlock that would otherwise occur when downstream is waiting for data to unblock
/// a response.
/// </para>
/// </summary>
internal sealed class NetworkBufferBatchStage : GraphStage<FlowShape<IOutputItem, IOutputItem>>
{
    private readonly long _maxWeight;

    private readonly Inlet<IOutputItem> _in = new("NetworkBufferBatch.In");
    private readonly Outlet<IOutputItem> _out = new("NetworkBufferBatch.Out");

    public override FlowShape<IOutputItem, IOutputItem> Shape { get; }

    public NetworkBufferBatchStage(long maxWeight)
    {
        _maxWeight = maxWeight;
        Shape = new FlowShape<IOutputItem, IOutputItem>(_in, _out);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private readonly NetworkBufferBatchStage _stage;

        // Current NetworkBuffer accumulation in progress.
        private NetworkBuffer? _batching;

        // Up to two items ready to emit.  _slot1 is always emitted before _slot2.
        // Invariant: _slot2 is null whenever _slot1 is null.
        // At most two slots are ever filled simultaneously (old batch + control item).
        private IOutputItem? _slot1;
        private IOutputItem? _slot2;

        private bool _upstreamDone;

        public Logic(NetworkBufferBatchStage stage) : base(stage.Shape)
        {
            _stage = stage;

            SetHandler(stage._in,
                onPush: OnPush,
                onUpstreamFinish: OnUpstreamFinish);

            SetHandler(stage._out,
                onPull: OnPull);
        }

        private void OnPush()
        {
            var item = Grab(_stage._in);

            if (item is NetworkBuffer nb)
            {
                if (_batching is null)
                {
                    if (IsAvailable(_stage._out))
                    {
                        // Downstream is already waiting — push immediately (mirrors BatchWeighted's
                        // "emit on demand" path when in Open state with pending downstream demand).
                        Push(_stage._out, nb);
                        if (!HasBeenPulled(_stage._in))
                        {
                            Pull(_stage._in);
                        }
                    }
                    else
                    {
                        // No downstream demand yet — stash and eagerly pull to try to batch more.
                        _batching = nb;
                        if (!HasBeenPulled(_stage._in))
                        {
                            Pull(_stage._in);
                        }
                    }
                }
                else
                {
                    var totalLength = _batching.Length + nb.Length;
                    if (totalLength <= _stage._maxWeight)
                    {
                        // Fits — merge and keep pulling.
                        _batching = MergeBuffers(_batching, nb);
                        if (!HasBeenPulled(_stage._in))
                        {
                            Pull(_stage._in);
                        }
                    }
                    else
                    {
                        // Overflow: flush the current batch and start a fresh one with nb.
                        Enqueue(_batching);
                        _batching = nb;
                        TryFlush();
                    }
                }
            }
            else
            {
                // Control item (StreamAcquireItem, ConnectionReuseItem, ConnectItem, …).
                // Flush any accumulated NetworkBuffer BEFORE emitting the control item
                // so that ordering is preserved and no bytes are lost.
                if (_batching is not null)
                {
                    Enqueue(_batching);
                    _batching = null;
                }

                Enqueue(item);
                TryFlush();
            }
        }

        private void OnPull()
        {
            if (_slot1 is not null)
            {
                // Dequeue and push the first ready item.
                var toEmit = _slot1;
                _slot1 = _slot2;
                _slot2 = null;
                Push(_stage._out, toEmit);

                if (_slot1 is not null)
                {
                    // More items queued — wait for the next OnPull.
                    return;
                }

                if (_upstreamDone && _batching is null)
                {
                    CompleteStage();
                }
                else if (!_upstreamDone && _batching is null && !HasBeenPulled(_stage._in))
                {
                    Pull(_stage._in);
                }
                // else: still accumulating in _batching — OnPush drives the next pull.
            }
            else if (_batching is not null)
            {
                // Downstream demands data; flush whatever has been accumulated so far
                // (mirrors BatchWeighted's "emit on pull" behaviour when in Closed state).
                var toEmit = _batching;
                _batching = null;
                Push(_stage._out, toEmit);

                if (_upstreamDone)
                {
                    CompleteStage();
                }
                else if (!HasBeenPulled(_stage._in))
                {
                    Pull(_stage._in);
                }
            }
            else if (_upstreamDone)
            {
                CompleteStage();
            }
            else if (!HasBeenPulled(_stage._in))
            {
                Pull(_stage._in);
            }
        }

        private void OnUpstreamFinish()
        {
            _upstreamDone = true;

            // Move any accumulated bytes into the emit queue so they are drained.
            if (_batching is not null)
            {
                Enqueue(_batching);
                _batching = null;
            }

            // If there is nothing left to emit, complete immediately.
            if (_slot1 is null)
            {
                CompleteStage();
            }
            // else: OnPull will drain _slot1 (and optionally _slot2) and then complete.
        }

        /// <summary>
        /// Attempts to push the head of the emit queue if downstream has demand.
        /// Pulls inlet when the queue is empty and we are no longer accumulating.
        /// </summary>
        private void TryFlush()
        {
            if (_slot1 is null || !IsAvailable(_stage._out))
            {
                return;
            }

            var toEmit = _slot1;
            _slot1 = _slot2;
            _slot2 = null;
            Push(_stage._out, toEmit);

            // If more items are queued, wait for the next OnPull.
            if (_slot1 is not null)
            {
                return;
            }

            if (_upstreamDone && _batching is null)
            {
                CompleteStage();
            }
            else if (!_upstreamDone && _batching is null && !HasBeenPulled(_stage._in))
            {
                Pull(_stage._in);
            }
            // else: still accumulating in _batching — OnPush drives the next pull.
        }

        /// <summary>Inserts an item into the next available emit slot (max two).</summary>
        private void Enqueue(IOutputItem item)
        {
            if (_slot1 is null)
            {
                _slot1 = item;
            }
            else if (_slot2 is null)
            {
                _slot2 = item;
            }
            else
            {
                // Should never be reached: the stage's pull discipline ensures at most
                // two items can be ready simultaneously (old batch + one control item).
                throw new InvalidOperationException(
                    "NetworkBufferBatchStage: emit queue overflow — this is a bug.");
            }
        }

        private static NetworkBuffer MergeBuffers(NetworkBuffer acc, NetworkBuffer next)
        {
            var totalLength = acc.Length + next.Length;

            if (acc.Capacity >= totalLength)
            {
                next.Memory.CopyTo(acc.FullMemory[acc.Length..]);
                next.Dispose();
                acc.Length = totalLength;
                return acc;
            }

            var merged = NetworkBuffer.Rent(totalLength);
            acc.Memory.CopyTo(merged.FullMemory);
            next.Memory.CopyTo(merged.FullMemory[acc.Length..]);
            acc.Dispose();
            next.Dispose();
            merged.Length = totalLength;
            merged.Key = acc.Key;
            return merged;
        }
    }
}
