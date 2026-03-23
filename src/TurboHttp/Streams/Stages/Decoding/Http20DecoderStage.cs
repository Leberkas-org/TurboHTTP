using System.Collections.Generic;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.Diagnostics;
using TurboHttp.Internal;
using TurboHttp.Protocol.RFC9113;

namespace TurboHttp.Streams.Stages.Decoding;

public sealed class Http20DecoderStage : GraphStage<FlowShape<IInputItem, Http2Frame>>
{
    private readonly Inlet<IInputItem> _in = new("Http20Decoder.In");
    private readonly Outlet<Http2Frame> _out = new("Http20Decoder.Out");

    public override FlowShape<IInputItem, Http2Frame> Shape { get; }


    public Http20DecoderStage()
    {
        Shape = new FlowShape<IInputItem, Http2Frame>(_in, _out);
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
            SetHandler(stage._in,
                onPush: () =>
                {
                    var item = Grab(stage._in);

                    if (item is not DataItem dataItem)
                    {
                        Pull(stage._in);
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
                    // Avoid LINQ allocation: scan once to check for unknown frames before deciding.
                    var hasUnknown = false;
                    for (var i = 0; i < frames.Count; i++)
                    {
                        if (frames[i] is UnknownFrame)
                        {
                            hasUnknown = true;
                            break;
                        }
                    }

                    IReadOnlyList<Http2Frame> visible;
                    if (hasUnknown)
                    {
                        var filtered = new List<Http2Frame>(frames.Count);
                        for (var i = 0; i < frames.Count; i++)
                        {
                            if (frames[i] is not UnknownFrame)
                            {
                                filtered.Add(frames[i]);
                            }
                        }
                        visible = filtered;
                    }
                    else
                    {
                        visible = frames;
                    }

                    if (visible.Count > 0)
                    {
                        for (var i = 0; i < visible.Count; i++)
                        {
                            var f = visible[i];
                            TurboHttpEventSource.Log.FrameReceived(f.Type.ToString(), f.StreamId, f.SerializedSize);
                        }

                        EmitMultiple(stage._out, visible);
                    }
                    else
                    {
                        Pull(stage._in);
                    }
                },
                onUpstreamFinish: CompleteStage,
                onUpstreamFailure: ex => Log.Warning("Http20DecoderStage: Upstream failure absorbed: {0}", ex.Message));

            SetHandler(stage._out,
                onPull: () => Pull(stage._in),
                onDownstreamFinish: _ => CompleteStage());
        }
    }
}