using System.Buffers;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.Protocol;

namespace TurboHttp.Streams.Stages;

public sealed class Http20EncoderStage : GraphStage<FlowShape<Http2Frame, (IMemoryOwner<byte>, int)>>
{
    private readonly Inlet<Http2Frame> _inlet = new("frameEncoder.in");
    private readonly Outlet<(IMemoryOwner<byte>, int)> _outlet = new("frameEncoder.out");

    public override FlowShape<Http2Frame, (IMemoryOwner<byte>, int)> Shape =>
        new(_inlet, _outlet);

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        public Logic(Http20EncoderStage stage)
            : base(stage.Shape)
        {
            SetHandler(stage._inlet, () =>
            {
                var frame = Grab(stage._inlet);

                var owner = MemoryPool<byte>.Shared.Rent(frame.SerializedSize);
                var span = owner.Memory.Span;

                frame.WriteTo(ref span);

                Push(stage._outlet, (owner, frame.SerializedSize));
            });

            SetHandler(stage._outlet, () => Pull(stage._inlet));
        }
    }
}