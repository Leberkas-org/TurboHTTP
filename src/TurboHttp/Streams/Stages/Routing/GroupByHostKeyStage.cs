using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.Stage;
using TurboHttp.Internal;
using TurboHttp.Streams.Stages;

namespace TurboHttp.Streams.Stages.Routing;

internal sealed class GroupByHostKeyStage<T> : GraphStage<FlowShape<T, Source<T, NotUsed>>>
{
    private readonly Inlet<T> _in = new("GroupByHostKey.In");
    private readonly Outlet<Source<T, NotUsed>> _out = new("GroupByHostKey.Out");
    public override FlowShape<T, Source<T, NotUsed>> Shape { get; }


    private readonly Func<T, RequestEndpoint> _keyFor;
    private readonly int _maxSubstreams;
    private readonly int _defaultQueueSize;

    public GroupByHostKeyStage(Func<T, RequestEndpoint> keyFor, int maxSubstreams = -1, int queueSize = 64)
    {
        _keyFor = keyFor ?? throw new ArgumentNullException(nameof(keyFor));
        _maxSubstreams = maxSubstreams;
        _defaultQueueSize = queueSize;
        Shape = new FlowShape<T, Source<T, NotUsed>>(_in, _out);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this, inheritedAttributes);

    private sealed class SubflowState(ISourceQueueWithComplete<T> queue)
    {
        public readonly ISourceQueueWithComplete<T> Queue = queue;
        public readonly Queue<T> Pending = new();
        public bool Offering;
    }

    private sealed class Logic : GraphStageLogic
    {
        private readonly GroupByHostKeyStage<T> _stage;
        private readonly int _queueSize;
        private readonly Dictionary<RequestEndpoint, SubflowState> _subflows = new();
        private readonly Queue<Source<T, NotUsed>> _pendingSources = new();
        private Action<RequestEndpoint>? _onOfferComplete;
        private bool _upstreamFinished;

        public Logic(GroupByHostKeyStage<T> stage, Attributes inheritedAttributes) : base(stage.Shape)
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
                onUpstreamFailure: ex => Log.Warning("GroupByHostKeyStage: Upstream failure absorbed: {0}", ex.Message));

            SetHandler(stage._out, onPull: HandleOutPull);
        }

        public override void PreStart()
        {
            _onOfferComplete = GetAsyncCallback<RequestEndpoint>(key =>
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
                else if (!HasBeenPulled(_stage._in) && !IsClosed(_stage._in))
                {
                    Pull(_stage._in);
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
                Push(_stage._out, bufferedSource);
            }
            else if (!HasBeenPulled(_stage._in))
            {
                Pull(_stage._in);
            }
        }

        private void HandlePush()
        {
            var item = Grab(_stage._in);
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

            if (!HasBeenPulled(_stage._in) && _pendingSources.Count == 0)
            {
                Pull(_stage._in);
            }
        }

        private void DrainPending(RequestEndpoint key, SubflowState state)
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