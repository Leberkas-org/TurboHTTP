using System.Threading.Channels;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHTTP.Internal;

namespace TurboHTTP.Tests.Shared;

/// <summary>
/// Fake TCP connection stage for HTTP/2 engine tests.
/// Intercepts outbound H2 frames (skipping the preface) and injects pre-queued server frames one per request.
/// </summary>
/// <remarks>
/// Exposes <see cref="H2EngineFakeConnectionStage.OutboundChannel"/> so tests can decode and inspect outbound H2 frames.
/// </remarks>
public sealed class H2EngineFakeConnectionStage : GraphStage<FlowShape<IOutputItem, IInputItem>>
{
    private readonly IReadOnlyList<byte[]> _serverFrames;

    public Channel<NetworkBuffer> OutboundChannel { get; } =
        Channel.CreateUnbounded<NetworkBuffer>();

    public Inlet<IOutputItem> In { get; } = new("h2-engine-fake.in");
    public Outlet<IInputItem> Out { get; } = new("h2-engine-fake.out");

    public override FlowShape<IOutputItem, IInputItem> Shape { get; }

    public H2EngineFakeConnectionStage(params byte[][] serverFrames)
    {
        _serverFrames = serverFrames;
        Shape = new FlowShape<IOutputItem, IInputItem>(In, Out);
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
                    if (item is ConnectItem)
                    {
                        Unlock();
                    }
                    else if (item is NetworkBuffer dataChunk)
                    {
                        var span = dataChunk.Span;
                        if (span.Length >= 24 && span[..24].SequenceEqual(H2Preface))
                        {
                            var remainder = span[24..];
                            if (remainder.Length > 0)
                            {
                                stage.OutboundChannel.Writer.TryWrite(NetworkBufferTestExtensions.FromArray(remainder.ToArray()));
                            }
                        }
                        else
                        {
                            stage.OutboundChannel.Writer.TryWrite(NetworkBufferTestExtensions.FromArray(span.ToArray()));
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
            Push(_stage.Out, NetworkBufferTestExtensions.FromArray(frameBytes));
        }

        public override void PreStart() => Pull(_stage.In);
    }
}