using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.Stage;

namespace TurboHttp.Streams.Stages.Internal;

/// <summary>
/// Lazily materializes a version-specific connection flow based on the first request's
/// <see cref="HttpRequestMessage.Version"/>. Replaces the former Partition(4)→LazyInit×4→Merge(4)
/// version router — since <c>GroupByRequestEndpoint</c> already groups by version, each
/// substream contains a single version, making the 4-way partition redundant.
/// <para>
/// Phase 1 (first element): inspect version → materialize correct flow via <c>SubFusingMaterializer</c>.
/// Phase 2 (subsequent elements): direct passthrough to the materialized inner flow.
/// </para>
/// </summary>
internal sealed class VersionDispatchStage
    : GraphStage<FlowShape<HttpRequestMessage, HttpResponseMessage>>
{
    private readonly Func<Version, Flow<HttpRequestMessage, HttpResponseMessage, NotUsed>> _flowFactory;

    private readonly Inlet<HttpRequestMessage> _in = new("VersionDispatch.In");
    private readonly Outlet<HttpResponseMessage> _out = new("VersionDispatch.Out");

    public override FlowShape<HttpRequestMessage, HttpResponseMessage> Shape { get; }

    public VersionDispatchStage(
        Func<Version, Flow<HttpRequestMessage, HttpResponseMessage, NotUsed>> flowFactory)
    {
        _flowFactory = flowFactory;
        Shape = new FlowShape<HttpRequestMessage, HttpResponseMessage>(_in, _out);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this, inheritedAttributes);

    private sealed class Logic : GraphStageLogic
    {
        private readonly VersionDispatchStage _stage;
        private readonly Attributes _inheritedAttributes;

        // Sink/Source pair connected to the inner flow — set on first element
        private SubSinkInlet<HttpResponseMessage>? _innerSink;
        private SubSourceOutlet<HttpRequestMessage>? _innerSource;
        private bool _initialized;

        public Logic(VersionDispatchStage stage, Attributes inheritedAttributes) : base(stage.Shape)
        {
            _stage = stage;
            _inheritedAttributes = inheritedAttributes;

            SetHandler(stage._in,
                onPush: OnPush,
                onUpstreamFinish: () =>
                {
                    if (_innerSource is not null)
                    {
                        _innerSource.Complete();
                    }
                    else
                    {
                        CompleteStage();
                    }
                },
                onUpstreamFailure: ex =>
                {
                    if (_innerSource is not null)
                    {
                        _innerSource.Fail(ex);
                    }
                    else
                    {
                        FailStage(ex);
                    }
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
                        // First pull — request first element to determine version
                        Pull(stage._in);
                    }
                });
        }

        private void OnPush()
        {
            var request = Grab(_stage._in);

            if (!_initialized)
            {
                // First element — materialize the correct version flow
                _initialized = true;
                MaterializeInnerFlow(request);
                return;
            }

            // Subsequent elements — forward to inner flow
            _innerSource!.Push(request);
        }

        private void MaterializeInnerFlow(HttpRequestMessage firstRequest)
        {
            var version = firstRequest.Version;
            var flow = _stage._flowFactory(version);

            // Create SubSource (we push requests into it) → inner flow → SubSink (we read responses from it)
            _innerSource = new SubSourceOutlet<HttpRequestMessage>(this, "VersionDispatch.InnerSource");
            _innerSink = new SubSinkInlet<HttpResponseMessage>(this, "VersionDispatch.InnerSink");

            // Wire SubSource → inner flow → SubSink
            Source.FromGraph(_innerSource.Source)
                .Via(flow)
                .RunWith(Sink.FromGraph(_innerSink.Sink), Materializer);

            // SubSource: when inner flow pulls, we pull upstream (or push buffered first element)
            var firstElementPushed = false;
            _innerSource.SetHandler(new LambdaOutHandler(
                onPull: () =>
                {
                    if (!firstElementPushed)
                    {
                        firstElementPushed = true;
                        _innerSource.Push(firstRequest);
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
