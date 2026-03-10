using System.Net.Http;
using Akka.Streams;
using Akka.Streams.Stage;

namespace TurboHttp.Streams.Stages;

public sealed class StreamIdAllocatorStage : GraphStage<FlowShape<HttpRequestMessage, (HttpRequestMessage, int)>>
{
    private readonly Inlet<HttpRequestMessage> _in = new("allocator.in");
    private readonly Outlet<(HttpRequestMessage, int)> _out = new("allocator.out");

    public override FlowShape<HttpRequestMessage, (HttpRequestMessage, int)> Shape { get; }

    public StreamIdAllocatorStage()
    {
        Shape = new FlowShape<HttpRequestMessage, (HttpRequestMessage, int)>(_in, _out);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private int _nextStreamId = 1;

        public Logic(StreamIdAllocatorStage stage) : base(stage.Shape)
        {
            SetHandler(stage._in,
                onPush: () =>
                {
                    var request = Grab(stage._in);

                    var streamId = _nextStreamId;
                    _nextStreamId += 2;

                    Push(stage._out, (request, streamId));
                },
                onUpstreamFinish: CompleteStage);

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