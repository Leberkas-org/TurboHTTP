using System.Threading.Channels;
using Akka.Streams;
using Akka.Streams.Stage;
using Servus.Akka.Transport;

namespace TurboHTTP.Tests.Shared;

internal sealed class EngineFakeConnectionStage : GraphStage<FlowShape<ITransportOutbound, ITransportInbound>>
{
    private readonly Func<byte[]> _responseFactory;

    public Channel<TransportBuffer> OutboundChannel { get; } = Channel.CreateUnbounded<TransportBuffer>();

    public Inlet<ITransportOutbound> In { get; } = new("fake-tcp.in");
    public Outlet<ITransportInbound> Out { get; } = new("fake-tcp.out");

    public override FlowShape<ITransportOutbound, ITransportInbound> Shape { get; }

    public EngineFakeConnectionStage(Func<byte[]> responseFactory)
    {
        _responseFactory = responseFactory;
        Shape = new FlowShape<ITransportOutbound, ITransportInbound>(In, Out);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private readonly EngineFakeConnectionStage _stage;
        private readonly Queue<ITransportInbound> _buffer = new();
        private bool _downstreamWaiting;

        public Logic(EngineFakeConnectionStage stage) : base(stage.Shape)
        {
            _stage = stage;

            SetHandler(stage.In,
                onPush: () =>
                {
                    var item = Grab(stage.In);
                    if (item is TransportData { Buffer: var dataChunk })
                    {
                        var copy = new byte[dataChunk.Length];
                        dataChunk.Span.CopyTo(copy);
                        stage.OutboundChannel.Writer.TryWrite(TransportBufferTestExtensions.FromArray(copy));
                        dataChunk.Dispose();

                        var responseBytes = _stage._responseFactory();

                        if (_downstreamWaiting)
                        {
                            _downstreamWaiting = false;
                            Push(stage.Out, new TransportData(TransportBufferTestExtensions.FromArray(responseBytes)));
                        }
                        else
                        {
                            _buffer.Enqueue(new TransportData(TransportBufferTestExtensions.FromArray(responseBytes)));
                        }
                    }

                    Pull(stage.In);
                },
                onUpstreamFinish: CompleteStage,
                onUpstreamFailure: FailStage);

            SetHandler(stage.Out,
                onPull: () =>
                {
                    if (_buffer.TryDequeue(out var chunk))
                    {
                        Push(stage.Out, chunk);
                    }
                    else
                    {
                        _downstreamWaiting = true;
                    }
                },
                onDownstreamFinish: _ => CompleteStage());
        }

        public override void PreStart() => Pull(_stage.In);
    }
}