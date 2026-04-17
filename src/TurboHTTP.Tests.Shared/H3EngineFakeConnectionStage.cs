using System.Threading.Channels;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHTTP.Internal;

namespace TurboHTTP.Tests.Shared;

/// <summary>
/// Fake TCP connection stage for HTTP/3 engine tests.
/// Intercepts outbound H3 frames and injects pre-queued server frames one per outbound push.
/// </summary>
public sealed class H3EngineFakeConnectionStage : GraphStage<FlowShape<IOutputItem, IInputItem>>
{
    private readonly IReadOnlyList<byte[]> _serverFrames;

    public Channel<(NetworkBuffer Buffer, Http3StreamType? StreamType)> OutboundChannel { get; } =
        Channel.CreateUnbounded<(NetworkBuffer, Http3StreamType?)>();

    public Inlet<IOutputItem> In { get; } = new("h3-engine-fake.in");
    public Outlet<IInputItem> Out { get; } = new("h3-engine-fake.out");

    public override FlowShape<IOutputItem, IInputItem> Shape { get; }

    public H3EngineFakeConnectionStage(params byte[][] serverFrames)
    {
        _serverFrames = serverFrames;
        Shape = new FlowShape<IOutputItem, IInputItem>(In, Out);
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

                    // Extract stream type from Http3NetworkBuffer (control preface, QPACK encoder, etc.)
                    Http3StreamType? streamType = null;
                    if (item is Http3NetworkBuffer h3Buf)
                    {
                        streamType = h3Buf.StreamType != Http3StreamType.None ? h3Buf.StreamType : null;
                    }

                    if (item is NetworkBuffer dataChunk)
                    {
                        stage.OutboundChannel.Writer.TryWrite((NetworkBufferTestExtensions.FromArray(dataChunk.Span.ToArray()), streamType));
                        dataChunk.Dispose();
                    }

                    // Every outbound push (tagged or not) unlocks a server frame.
                    Unlock();

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
            var buf = NetworkBufferTestExtensions.FromArray(frameBytes);

            // First frame is the control stream (SETTINGS), remaining are request stream data.
            var h3Buf = Http3NetworkBuffer.Rent(buf.Length);
            buf.Span.CopyTo(h3Buf.FullMemory.Span);
            h3Buf.Length = buf.Length;
            buf.Dispose();

            if (_serverFrameIndex == 1)
            {
                h3Buf.StreamType = Http3StreamType.Control;
            }
            else
            {
                h3Buf.StreamType = Http3StreamType.Request;
                h3Buf.StreamId = 0;
            }

            IInputItem item = h3Buf;

            Push(_stage.Out, item);

            // HTTP/3 relies on QUIC FIN (upstream completion) to signal stream end.
            // After all server frames are delivered, complete the output to propagate
            // through the decoder → connection → stream pipeline.
            if (_serverFrameIndex >= _stage._serverFrames.Count)
            {
                Complete(_stage.Out);
            }
        }

        public override void PreStart() => Pull(_stage.In);
    }
}