using System.Threading.Channels;
using Akka.Streams;
using Akka.Streams.Stage;
using Servus.Akka.Transport;
using TurboHTTP.Protocol.Http3;

namespace TurboHTTP.Tests.Shared;

internal sealed class H3EngineFakeConnectionStage : GraphStage<FlowShape<ITransportOutbound, ITransportInbound>>
{
    private readonly IReadOnlyList<byte[]> _serverFrames;

    public Channel<(TransportBuffer Buffer, long? StreamType)> OutboundChannel { get; } =
        Channel.CreateUnbounded<(TransportBuffer, long?)>();

    public Inlet<ITransportOutbound> In { get; } = new("h3-engine-fake.in");
    public Outlet<ITransportInbound> Out { get; } = new("h3-engine-fake.out");

    public override FlowShape<ITransportOutbound, ITransportInbound> Shape { get; }

    public H3EngineFakeConnectionStage(params byte[][] serverFrames)
    {
        _serverFrames = serverFrames;
        Shape = new FlowShape<ITransportOutbound, ITransportInbound>(In, Out);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private readonly H3EngineFakeConnectionStage _stage;
        private int _serverFrameIndex;
        private int _unlockedFrames;
        private bool _downstreamWaiting;

        public Logic(H3EngineFakeConnectionStage stage) : base(stage.Shape)
        {
            _stage = stage;

            SetHandler(stage.In,
                onPush: () =>
                {
                    var item = Grab(stage.In);

                    long? streamType = null;
                    if (item is MultiplexedData h3Data)
                    {
                        streamType = h3Data.StreamId;
                    }

                    if (item is TransportData { Buffer: var dataChunk } || item is MultiplexedData { Buffer: var buf })
                    {
                        var buffer = item is TransportData { Buffer: var d } ? d : (item as MultiplexedData)!.Buffer;
                        stage.OutboundChannel.Writer.TryWrite((
                            TransportBufferTestExtensions.FromArray(buffer.Span.ToArray()), streamType));
                        buffer.Dispose();

                        Unlock();
                    }

                    if (!IsClosed(stage.In))
                    {
                        Pull(stage.In);
                    }
                },
                onUpstreamFinish: () =>
                {
                    if (!IsClosed(stage.Out))
                    {
                        Complete(stage.Out);
                    }
                },
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
                onDownstreamFinish: _ =>
                {
                    if (!IsClosed(stage.In))
                    {
                        Cancel(stage.In);
                    }
                });
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
            var buf = TransportBufferTestExtensions.FromArray(frameBytes);

            long streamId;
            if (_serverFrameIndex == 1)
            {
                streamId = (long)StreamType.Control;
            }
            else
            {
                streamId = 0;
            }

            ITransportInbound item = new MultiplexedData(buf, streamId);
            Push(_stage.Out, item);

            if (_serverFrameIndex >= _stage._serverFrames.Count)
            {
                Complete(_stage.Out);
            }
        }

        public override void PreStart() => Pull(_stage.In);
    }
}