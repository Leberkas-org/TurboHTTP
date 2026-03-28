using System.Collections.Generic;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.Internal;
using TurboHttp.Protocol.RFC9114;

namespace TurboHttp.Streams.Stages.Decoding;

public sealed class Http30DecoderStage : GraphStage<FlowShape<IInputItem, Http3Frame>>
{
    private readonly Inlet<IInputItem> _in = new("Http30Decoder.In");
    private readonly Outlet<Http3Frame> _out = new("Http30Decoder.Out");

    public override FlowShape<IInputItem, Http3Frame> Shape { get; }

    public Http30DecoderStage()
    {
        Shape = new FlowShape<IInputItem, Http3Frame>(_in, _out);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
    {
        return new Logic(this);
    }

    private sealed class Logic : GraphStageLogic
    {
        private readonly Http3FrameDecoder _decoder = new();

        public Logic(Http30DecoderStage stage) : base(stage.Shape)
        {
            SetHandler(stage._in,
                onPush: () =>
                {
                    var item = Grab(stage._in);

                    if (item is not DataItem dataItem)
                    {
                        Pull(stage._in);
                        return;
                    }

                    IReadOnlyList<Http3Frame> frames;
                    try
                    {
                        var data = dataItem.Memory.Memory.Span[..dataItem.Length];
                        frames = _decoder.DecodeAll(data, out _);
                    }
                    finally
                    {
                        dataItem.Memory.Dispose();
                    }

                    // Filter out null frames (unknown frame types skipped per RFC 9114 §7.2.8)
                    var visible = new List<Http3Frame>(frames.Count);
                    for (var i = 0; i < frames.Count; i++)
                    {
                        visible.Add(frames[i]);
                    }

                    if (visible.Count > 0)
                    {
                        EmitMultiple(stage._out, visible);
                    }
                    else
                    {
                        Pull(stage._in);
                    }
                },
                onUpstreamFinish: CompleteStage,
                onUpstreamFailure: ex =>
                {
                    Log.Warning("Http30DecoderStage: Upstream failure absorbed: {0}", ex.Message);
                    Log.Debug("Http30DecoderStage: Failing stage due to upstream error: {0}", ex.Message);
                    FailStage(ex);
                });

            SetHandler(stage._out,
                onPull: () => Pull(stage._in),
                onDownstreamFinish: _ => CompleteStage());
        }
    }
}
