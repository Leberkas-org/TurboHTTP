using System.Net.Http;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;

namespace TurboHttp.Streams.Stages.Routing;

public sealed class Http20StreamIdAllocatorStage : GraphStage<FlowShape<HttpRequestMessage, (HttpRequestMessage, int)>>
{
    private readonly Inlet<HttpRequestMessage> _in = new("StreamIdAllocator.In");
    private readonly Outlet<(HttpRequestMessage, int)> _out = new("StreamIdAllocator.Out");
    private readonly int _startStreamId;

    public override FlowShape<HttpRequestMessage, (HttpRequestMessage, int)> Shape { get; }


    public Http20StreamIdAllocatorStage(int startStreamId = 1)
    {
        _startStreamId = startStreamId;
        Shape = new FlowShape<HttpRequestMessage, (HttpRequestMessage, int)>(_in, _out);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private int _nextStreamId;

        public Logic(Http20StreamIdAllocatorStage stage) : base(stage.Shape)
        {
            _nextStreamId = stage._startStreamId;
            SetHandler(stage._in,
                onPush: () =>
                {
                    var request = Grab(stage._in);

                    var streamId = _nextStreamId;
                    _nextStreamId += 2;

                    Push(stage._out, (request, streamId));
                },
                onUpstreamFinish: CompleteStage,
                onUpstreamFailure: ex =>
                {
                    Log.Warning("StreamIdAllocatorStage: Upstream failure absorbed: {0}", ex.Message);
                    Log.Debug("StreamIdAllocatorStage: Failing stage due to upstream error: {0}", ex.Message);
                    FailStage(ex);
                });

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