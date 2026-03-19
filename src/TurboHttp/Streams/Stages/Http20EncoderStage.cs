using System.Buffers;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.Internal;
using TurboHttp.IO.Stages;
using TurboHttp.Protocol.RFC9113;

namespace TurboHttp.Streams.Stages;

public sealed class Http20EncoderStage : GraphStage<FlowShape<Http2Frame, IOutputItem>>
{
    private readonly Inlet<Http2Frame> _inlet = new("frameEncoder.in");
    private readonly Outlet<IOutputItem> _outlet = new("frameEncoder.out");

    public override FlowShape<Http2Frame, IOutputItem> Shape => new(_inlet, _outlet);


    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private RequestEndpoint _endpoint;

        public Logic(Http20EncoderStage stage) : base(stage.Shape)
        {
            SetHandler(stage._inlet, () =>
            {
                var frame = Grab(stage._inlet);

                if (_endpoint == default && frame.Endpoint.HasValue)
                {
                    _endpoint = frame.Endpoint.Value;
                }

                var owner = MemoryPool<byte>.Shared.Rent(frame.SerializedSize);
                var span = owner.Memory.Span;

                frame.WriteTo(ref span);

                Push(stage._outlet, new DataItem(owner, frame.SerializedSize)
                {
                    Key = _endpoint
                });
            });

            SetHandler(stage._outlet, () => Pull(stage._inlet));
        }
    }
}