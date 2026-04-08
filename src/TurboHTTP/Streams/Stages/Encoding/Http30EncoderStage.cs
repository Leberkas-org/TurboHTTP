using Akka.Streams;
using Akka.Streams.Stage;
using TurboHTTP.Internal;
using TurboHTTP.Protocol.Http3;

namespace TurboHTTP.Streams.Stages.Encoding;

public sealed class Http30EncoderStage : GraphStage<FlowShape<Http3Frame, IOutputItem>>
{
    private readonly Inlet<Http3Frame> _in = new("Http30Encoder.In");
    private readonly Outlet<IOutputItem> _out = new("Http30Encoder.Out");

    public override FlowShape<Http3Frame, IOutputItem> Shape => new(_in, _out);

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        public Logic(Http30EncoderStage stage) : base(stage.Shape)
        {
            SetHandler(stage._in, () =>
            {
                var frame = Grab(stage._in);

                var buf = NetworkBuffer.Rent(frame.SerializedSize);
                var span = buf.FullMemory.Span;

                frame.WriteTo(ref span);
                buf.Length = frame.SerializedSize;

                Push(stage._out, buf);
            });

            SetHandler(stage._out, () => Pull(stage._in));
        }
    }
}
