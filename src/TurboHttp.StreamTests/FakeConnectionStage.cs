using System.Buffers;
using System.Threading.Channels;
using Akka.Streams;
using Akka.Streams.Stage;

namespace TurboHttp.StreamTests;

public sealed class FakeConnectionStage : GraphStage<FlowShape<(IMemoryOwner<byte>, int), (IMemoryOwner<byte>, int)>>
{
    public Channel<(IMemoryOwner<byte>, int)> OutboundChannel { get; } =
        Channel.CreateUnbounded<(IMemoryOwner<byte>, int)>();

    public Inlet<(IMemoryOwner<byte>, int)> In { get; } = new("fake-tcp.in");
    public Outlet<(IMemoryOwner<byte>, int)> Out { get; } = new("fake-tcp.out");

    public override FlowShape<(IMemoryOwner<byte>, int), (IMemoryOwner<byte>, int)> Shape { get; }

    public FakeConnectionStage()
    {
        Shape = new FlowShape<(IMemoryOwner<byte>, int), (IMemoryOwner<byte>, int)>(In, Out);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private readonly FakeConnectionStage _stage;
        private readonly Queue<(IMemoryOwner<byte>, int)> _buffer = new();
        private bool _downstreamWaiting;

        public Logic(FakeConnectionStage stage) : base(stage.Shape)
        {
            _stage = stage;

            SetHandler(stage.In,
                onPush: () =>
                {
                    var (owner, length) = Grab(stage.In);

                    var copy = new byte[length];
                    owner.Memory.Span[..length].CopyTo(copy);
                    stage.OutboundChannel.Writer.TryWrite((new SimpleMemoryOwner(copy), length));

                    if (_downstreamWaiting)
                    {
                        _downstreamWaiting = false;
                        Push(stage.Out, (owner, length));
                    }
                    else
                    {
                        _buffer.Enqueue((owner, length));
                    }

                    Pull(stage.In);
                },
                onUpstreamFinish: CompleteStage,
                onUpstreamFailure: FailStage);

            SetHandler(stage.Out,
                onPull: () =>
                {
                    if (_buffer.TryDequeue(out var chunk))
                        Push(stage.Out, chunk);
                    else
                        _downstreamWaiting = true;
                },
                onDownstreamFinish: _ => CompleteStage());
        }

        public override void PreStart() => Pull(_stage.In);
    }
}