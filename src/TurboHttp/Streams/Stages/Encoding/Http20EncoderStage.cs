using System.Buffers;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.Internal;
using TurboHttp.Protocol.RFC9113;

namespace TurboHttp.Streams.Stages.Encoding;

public sealed class Http20EncoderStage : GraphStage<FlowShape<Http2Frame, IOutputItem>>
{
    private readonly Inlet<Http2Frame> _in = new("Http20Encoder.In");
    private readonly Outlet<IOutputItem> _out = new("Http20Encoder.Out");

    public override FlowShape<Http2Frame, IOutputItem> Shape => new(_in, _out);


    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private RequestEndpoint _endpoint;

        public Logic(Http20EncoderStage stage) : base(stage.Shape)
        {
            SetHandler(stage._in, () =>
            {
                var frame = Grab(stage._in);

                if (_endpoint == default && frame.Endpoint.HasValue)
                {
                    _endpoint = frame.Endpoint.Value;
                }

                var owner = MemoryPool<byte>.Shared.Rent(frame.SerializedSize);
                var span = owner.Memory.Span;

                frame.WriteTo(ref span);

                Push(stage._out, new DataItem(owner, frame.SerializedSize)
                {
                    Key = _endpoint
                });
            });

            SetHandler(stage._out, () => Pull(stage._in));
        }
    }
}