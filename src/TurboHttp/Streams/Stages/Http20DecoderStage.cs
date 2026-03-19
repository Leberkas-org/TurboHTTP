using System.Collections.Generic;
using System.Linq;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.Internal;
using TurboHttp.IO.Stages;
using TurboHttp.Protocol.RFC9113;

namespace TurboHttp.Streams.Stages;

public sealed class Http20DecoderStage : GraphStage<FlowShape<IInputItem, Http2Frame>>
{
    private readonly Inlet<IInputItem> _inlet = new("http20.tcp.in");
    private readonly Outlet<Http2Frame> _outlet = new("http20.frame.out");

    public override FlowShape<IInputItem, Http2Frame> Shape { get; }


    public Http20DecoderStage()
    {
        Shape = new FlowShape<IInputItem, Http2Frame>(_inlet, _outlet);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
    {
        return new Logic(this);
    }

    private sealed class Logic : GraphStageLogic
    {
        private readonly Http2FrameDecoder _decoder = new();

        public Logic(Http20DecoderStage stage) : base(stage.Shape)
        {
            SetHandler(stage._inlet,
                onPush: () =>
                {
                    var item = Grab(stage._inlet);

                    if (item is not DataItem dataItem)
                    {
                        Pull(stage._inlet);
                        return;
                    }

                    IReadOnlyList<Http2Frame> frames;
                    try
                    {
                        var data = dataItem.Memory.Memory[..dataItem.Length];
                        frames = _decoder.Decode(data);
                    }
                    finally
                    {
                        dataItem.Memory.Dispose();
                    }

                    // Filter out UnknownFrame — RFC 9113 §5.5: unknown types MUST be ignored.
                    var visible = frames.Where(f => f is not UnknownFrame).ToList();

                    if (visible.Count > 0)
                    {
                        EmitMultiple(stage._outlet, visible);
                    }
                    else
                    {
                        Pull(stage._inlet);
                    }
                },
                onUpstreamFinish: CompleteStage,
                onUpstreamFailure: FailStage);

            SetHandler(stage._outlet,
                onPull: () => Pull(stage._inlet),
                onDownstreamFinish: _ => CompleteStage());
        }
    }
}