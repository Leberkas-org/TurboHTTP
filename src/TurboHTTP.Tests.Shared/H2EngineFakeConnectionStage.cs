using System.Threading.Channels;
using Akka.Streams;
using Akka.Streams.Stage;
using Servus.Akka.Transport;

namespace TurboHTTP.Tests.Shared;

internal sealed class H2EngineFakeConnectionStage : GraphStage<FlowShape<ITransportOutbound, ITransportInbound>>
{
    private readonly IReadOnlyList<byte[]> _serverFrames;

    public Channel<TransportBuffer> OutboundChannel { get; } =
        Channel.CreateUnbounded<TransportBuffer>();

    public Inlet<ITransportOutbound> In { get; } = new("h2-engine-fake.in");
    public Outlet<ITransportInbound> Out { get; } = new("h2-engine-fake.out");

    public override FlowShape<ITransportOutbound, ITransportInbound> Shape { get; }

    public H2EngineFakeConnectionStage(params byte[][] serverFrames)
    {
        _serverFrames = serverFrames;
        Shape = new FlowShape<ITransportOutbound, ITransportInbound>(In, Out);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private static ReadOnlySpan<byte> H2Preface => "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"u8;

        private readonly H2EngineFakeConnectionStage _stage;
        private int _serverFrameIndex;
        private int _unlockedFrames;
        private bool _downstreamWaiting;

        public Logic(H2EngineFakeConnectionStage stage) : base(stage.Shape)
        {
            _stage = stage;

            SetHandler(stage.In,
                onPush: () =>
                {
                    var item = Grab(stage.In);
                    if (item is ConnectTransport)
                    {
                        Unlock();
                    }
                    else if (item is TransportData { Buffer: var dataChunk })
                    {
                        var span = dataChunk.Span;
                        if (span.Length >= 24 && span[..24].SequenceEqual(H2Preface))
                        {
                            var remainder = span[24..];
                            if (remainder.Length > 0)
                            {
                                stage.OutboundChannel.Writer.TryWrite(TransportBufferTestExtensions.FromArray(remainder.ToArray()));
                            }
                        }
                        else
                        {
                            stage.OutboundChannel.Writer.TryWrite(TransportBufferTestExtensions.FromArray(span.ToArray()));
                        }

                        dataChunk.Dispose();
                        Unlock();
                    }

                    Pull(stage.In);
                },
                onUpstreamFinish: CompleteStage,
                onUpstreamFailure: FailStage);

            SetHandler(stage.Out,
                onPull: () =>
                {
                    if (_unlockedFrames > 0 && _serverFrameIndex < _stage._serverFrames.Count)
                    {
                        _unlockedFrames--;
                        PushNextFrame();
                    }
                    else
                    {
                        _downstreamWaiting = true;
                    }
                },
                onDownstreamFinish: _ => CompleteStage());
        }

        private void Unlock()
        {
            if (_downstreamWaiting && _serverFrameIndex < _stage._serverFrames.Count)
            {
                _downstreamWaiting = false;
                PushNextFrame();
            }
            else
            {
                _unlockedFrames++;
            }
        }

        private void PushNextFrame()
        {
            var frameBytes = _stage._serverFrames[_serverFrameIndex++];
            Push(_stage.Out, new TransportData(TransportBufferTestExtensions.FromArray(frameBytes)));
        }

        public override void PreStart() => Pull(_stage.In);
    }
}
