using System.Net.Http;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;

namespace TurboHttp.Streams.Stages.Routing;

/// <summary>
/// Allocates monotonically increasing client-initiated bidirectional QUIC stream IDs
/// to each HTTP/3 request. Per RFC 9000 §2.1, client-initiated bidirectional streams
/// use IDs of the form 4n: 0, 4, 8, 12, …
/// </summary>
/// <remarks>
/// Uses <c>long</c> for stream IDs because QUIC stream IDs are 62-bit integers.
/// </remarks>
public sealed class Http30StreamIdAllocatorStage : GraphStage<FlowShape<HttpRequestMessage, (HttpRequestMessage, long)>>
{
    private readonly Inlet<HttpRequestMessage> _in = new("H3StreamIdAllocator.In");
    private readonly Outlet<(HttpRequestMessage, long)> _out = new("H3StreamIdAllocator.Out");

    public override FlowShape<HttpRequestMessage, (HttpRequestMessage, long)> Shape { get; }

    public Http30StreamIdAllocatorStage()
    {
        Shape = new FlowShape<HttpRequestMessage, (HttpRequestMessage, long)>(_in, _out);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private long _nextStreamId;

        public Logic(Http30StreamIdAllocatorStage stage) : base(stage.Shape)
        {
            SetHandler(stage._in,
                onPush: () =>
                {
                    var request = Grab(stage._in);

                    var streamId = _nextStreamId;
                    _nextStreamId += 4;

                    Push(stage._out, (request, streamId));
                },
                onUpstreamFinish: CompleteStage,
                onUpstreamFailure: ex => Log.Warning("Http30StreamIdAllocatorStage: Upstream failure absorbed: {0}", ex.Message));

            SetHandler(stage._out,
                onPull: () =>
                {
                    if (!HasBeenPulled(stage._in))
                    {
                        Pull(stage._in);
                    }
                });
        }
    }
}
