using System.Collections.Concurrent;
using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.Stage;
using TurboHTTP.Internal;

namespace TurboHTTP.Streams.Stages.Internal;

/// <summary>
/// Lazily materializes a version-specific connection flow based on the first request's
/// <see cref="RequestEndpoint"/>. Replaces the former Partition(4)→LazyInit×4→Merge(4)
/// version router — since <c>GroupByRequestEndpoint</c> already groups by endpoint, each
/// substream contains a single endpoint, making the 4-way partition redundant.
/// <para>
/// Phase 1 (first element): inspect endpoint → look up or create the flow blueprint in the
/// shared cache, then materialize via <c>SubFusingMaterializer</c>.
/// Phase 2 (subsequent elements): direct passthrough to the materialized inner flow.
/// </para>
/// <para>
/// The flow blueprint cache is keyed by <see cref="RequestEndpoint"/> and shared across all
/// <see cref="Logic"/> instances (one per substream). Since there are at most 4 HTTP versions
/// (1.0, 1.1, 2.0, 3.0) × N distinct host:port combinations, the cache grows proportionally
/// to the number of unique endpoints. This avoids re-running the flow factory (which creates
/// engine instances, transport stages, and graph objects) for every new substream with the
/// same endpoint.
/// </para>
/// </summary>
internal sealed class EndpointDispatchStage
    : GraphStage<FlowShape<HttpRequestMessage, HttpResponseMessage>>
{
    private readonly Func<RequestEndpoint, Flow<HttpRequestMessage, HttpResponseMessage, NotUsed>> _flowFactory;

    /// <summary>
    /// Cache of flow blueprints keyed by <see cref="RequestEndpoint"/>. Shared across all Logic
    /// instances (substreams). Thread-safe via <see cref="ConcurrentDictionary{TKey,TValue}"/>
    /// because different substreams may run on different dispatchers/threads.
    /// </summary>
    private readonly ConcurrentDictionary<RequestEndpoint, Flow<HttpRequestMessage, HttpResponseMessage, NotUsed>> _flowCache = new();

    private readonly Inlet<HttpRequestMessage> _in = new("EndpointDispatch.In");
    private readonly Outlet<HttpResponseMessage> _out = new("EndpointDispatch.Out");

    public override FlowShape<HttpRequestMessage, HttpResponseMessage> Shape { get; }

    public EndpointDispatchStage(
        Func<RequestEndpoint, Flow<HttpRequestMessage, HttpResponseMessage, NotUsed>> flowFactory)
    {
        _flowFactory = flowFactory;
        Shape = new FlowShape<HttpRequestMessage, HttpResponseMessage>(_in, _out);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this, inheritedAttributes);

    private sealed class Logic : GraphStageLogic
    {
        private readonly EndpointDispatchStage _stage;
        private readonly Attributes _inheritedAttributes;

        // Sink/Source pair connected to the inner flow — set on first element
        private SubSinkInlet<HttpResponseMessage>? _innerSink;
        private SubSourceOutlet<HttpRequestMessage>? _innerSource;
        private bool _initialized;

        // Tracks whether upstream finished before the inner flow pulled the first element.
        // When true, the SubSource is completed after pushing the buffered first element.
        private bool _upstreamFinished;
        private Exception? _upstreamFailure;
        private bool _pendingFirstElement;

        public Logic(EndpointDispatchStage stage, Attributes inheritedAttributes) : base(stage.Shape)
        {
            _stage = stage;
            _inheritedAttributes = inheritedAttributes;

            SetHandler(stage._in,
                onPush: OnPush,
                onUpstreamFinish: () =>
                {
                    _upstreamFinished = true;
                    if (_innerSource is not null && !_pendingFirstElement)
                    {
                        _innerSource.Complete();
                    }
                    else if (_innerSource is null)
                    {
                        CompleteStage();
                    }
                    // else: first element not yet pushed — defer completion to onPull
                },
                onUpstreamFailure: ex =>
                {
                    _upstreamFinished = true;
                    _upstreamFailure = ex;
                    if (_innerSource is not null && !_pendingFirstElement)
                    {
                        _innerSource.Fail(ex);
                    }
                    else if (_innerSource is null)
                    {
                        FailStage(ex);
                    }
                    // else: first element not yet pushed — defer failure to onPull
                });

            SetHandler(stage._out,
                onPull: () =>
                {
                    if (_innerSink is not null)
                    {
                        _innerSink.Pull();
                    }
                    else
                    {
                        // First pull — request first element to determine endpoint
                        Pull(stage._in);
                    }
                });
        }

        private void OnPush()
        {
            var request = Grab(_stage._in);

            if (!_initialized)
            {
                // First element — materialize the correct endpoint flow
                _initialized = true;
                MaterializeInnerFlow(request);
                return;
            }

            // Subsequent elements — forward to inner flow
            _innerSource!.Push(request);
        }

        private void MaterializeInnerFlow(HttpRequestMessage firstRequest)
        {
            var endpoint = RequestEndpoint.FromRequest(firstRequest);

            // Look up or create the flow blueprint — shared across all substreams with the same endpoint.
            // The blueprint is an immutable graph description; each materialization creates fresh
            // stage Logic instances, so sharing is safe.
            var flow = _stage._flowCache.GetOrAdd(endpoint, _stage._flowFactory);

            // Create SubSource (we push requests into it) → inner flow → SubSink (we read responses from it)
            _innerSource = new SubSourceOutlet<HttpRequestMessage>(this, "EndpointDispatch.InnerSource");
            _innerSink = new SubSinkInlet<HttpResponseMessage>(this, "EndpointDispatch.InnerSink");

            // Wire SubSource → inner flow → SubSink
            Source.FromGraph(_innerSource.Source)
                .Via(flow.Async())
                .RunWith(Sink.FromGraph(_innerSink.Sink), SubFusingMaterializer);

            // SubSource: when inner flow pulls, we pull upstream (or push buffered first element)
            _pendingFirstElement = true;
            _innerSource.SetHandler(new LambdaOutHandler(
                onPull: () =>
                {
                    if (_pendingFirstElement)
                    {
                        _pendingFirstElement = false;
                        _innerSource.Push(firstRequest);

                        // If upstream finished while the first element was pending, propagate now
                        if (_upstreamFinished)
                        {
                            if (_upstreamFailure is not null)
                            {
                                _innerSource.Fail(_upstreamFailure);
                            }
                            else
                            {
                                _innerSource.Complete();
                            }
                        }
                    }
                    else if (!IsClosed(_stage._in) && !HasBeenPulled(_stage._in))
                    {
                        Pull(_stage._in);
                    }
                },
                onDownstreamFinish: cause =>
                {
                    if (!IsClosed(_stage._in))
                    {
                        Cancel(_stage._in, cause);
                    }
                }));

            // SubSink: when inner flow pushes a response, we push downstream
            _innerSink.SetHandler(new LambdaInHandler(
                onPush: () =>
                {
                    var response = _innerSink.Grab();
                    Push(_stage._out, response);
                },
                onUpstreamFinish: CompleteStage,
                onUpstreamFailure: FailStage));

            // If downstream already demanded, start pulling from inner sink
            if (IsAvailable(_stage._out))
            {
                _innerSink.Pull();
            }
        }
    }
}
