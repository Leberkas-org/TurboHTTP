using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.Diagnostics;
using TurboHttp.Internal;
using TurboHttp.Protocol.Http2;

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

                    if (item is not NetworkBuffer buffer)
                    {
                        Pull(stage._in);
                        return;
                    }

                    // Transfer ownership of the NetworkBuffer to the decoder.
                    // The decoder keeps the buffer alive until the next Decode() call,
                    // ensuring returned frame slices remain valid while downstream processes them.
                    // Akka back-pressure guarantees all frames are consumed before the next onPush.
                    var frames = _decoder.Decode(buffer);

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
                            TurboTrace.Protocol.Trace(this, $"Frame received: {f.Type} stream={f.StreamId} length={f.SerializedSize}");
                        }

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
                    Log.Warning("Http20DecoderStage: Upstream failure absorbed: {0}", ex.Message);
                    Log.Debug("Http20DecoderStage: Failing stage due to upstream error: {0}", ex.Message);
                    FailStage(ex);
                });

            SetHandler(stage._out,
                onPull: () => Pull(stage._in),
                onDownstreamFinish: _ => CompleteStage());
        }
    }
}