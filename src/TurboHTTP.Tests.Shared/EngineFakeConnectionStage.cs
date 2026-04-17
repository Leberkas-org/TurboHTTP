using System.Threading.Channels;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHTTP.Internal;

namespace TurboHTTP.Tests.Shared;

/// <summary>
/// Fake TCP connection stage for HTTP/1.x engine tests.
/// Intercepts outbound serialised bytes and injects a synthetic response produced by a caller-supplied factory.
/// </summary>
/// <remarks>
/// Exposes <see cref="EngineFakeConnectionStage.OutboundChannel"/> so tests can inspect the raw bytes sent by the encoder.
/// </remarks>
public sealed class EngineFakeConnectionStage : GraphStage<FlowShape<IOutputItem, IInputItem>>
{
    private readonly Func<byte[]> _responseFactory;

    public Channel<NetworkBuffer> OutboundChannel { get; } = Channel.CreateUnbounded<NetworkBuffer>();

    public Inlet<IOutputItem> In { get; } = new("fake-tcp.in");
    public Outlet<IInputItem> Out { get; } = new("fake-tcp.out");

    public override FlowShape<IOutputItem, IInputItem> Shape { get; }

    public EngineFakeConnectionStage(Func<byte[]> responseFactory)
    {
        _responseFactory = responseFactory;
        Shape = new FlowShape<IOutputItem, IInputItem>(In, Out);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private readonly EngineFakeConnectionStage _stage;
        private readonly Queue<NetworkBuffer> _buffer = new();
        private bool _downstreamWaiting;

        public Logic(EngineFakeConnectionStage stage) : base(stage.Shape)
        {
            _stage = stage;

            SetHandler(stage.In,
                onPush: () =>
                {
                    var item = Grab(stage.In);
                    if (item is NetworkBuffer dataChunk)
                    {
                        var copy = new byte[dataChunk.Length];
                        dataChunk.Span.CopyTo(copy);
                        stage.OutboundChannel.Writer.TryWrite(NetworkBufferTestExtensions.FromArray(copy));
                        dataChunk.Dispose();

                        var responseBytes = _stage._responseFactory();

                        if (_downstreamWaiting)
                        {
                            _downstreamWaiting = false;
                            Push(stage.Out, NetworkBufferTestExtensions.FromArray(responseBytes));
                        }
                        else
                        {
                            _buffer.Enqueue(NetworkBufferTestExtensions.FromArray(responseBytes));
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