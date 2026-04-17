using System.Text;
using System.Threading.Channels;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHTTP.Internal;

namespace TurboHTTP.Tests.Shared;

/// <summary>
/// Fake proxy stage that simulates an HTTP CONNECT tunnel at the transport level.
/// When a <see cref="ConnectItem"/> arrives, it immediately responds with
/// "HTTP/1.1 200 Connection Established\r\n\r\n", establishing the tunnel.
/// Subsequent <see cref="NetworkBuffer"/> items are routed through the inner
/// response factory, allowing acceptance tests to verify tunneled request/response
/// flows without a real proxy server.
/// </summary>
public sealed class FakeProxyStage : GraphStage<FlowShape<IOutputItem, IInputItem>>
{
    private static readonly byte[] ConnectEstablishedBytes =
        Encoding.Latin1.GetBytes("HTTP/1.1 200 Connection Established\r\n\r\n");

    private readonly Func<int, byte[], byte[]?> _responseFactory;

    public Channel<NetworkBuffer> OutboundChannel { get; } = Channel.CreateUnbounded<NetworkBuffer>();

    public Inlet<IOutputItem> In { get; } = new("FakeProxy.In");
    public Outlet<IInputItem> Out { get; } = new("FakeProxy.Out");

    public override FlowShape<IOutputItem, IInputItem> Shape { get; }

    /// <param name="responseFactory">
    /// Factory receiving (requestIndex, outboundBytes) and returning response bytes for
    /// each tunneled request. Return <c>null</c> to abort the connection.
    /// </param>
    public FakeProxyStage(Func<int, byte[], byte[]?> responseFactory)
    {
        _responseFactory = responseFactory;
        Shape = new FlowShape<IOutputItem, IInputItem>(In, Out);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private readonly FakeProxyStage _stage;
        private readonly Queue<IInputItem> _buffer = new();
        private bool _downstreamWaiting;
        private bool _tunnelEstablished;
        private int _requestIndex;

        public Logic(FakeProxyStage stage) : base(stage.Shape)
        {
            _stage = stage;

            SetHandler(stage.In,
                onPush: () =>
                {
                    var item = Grab(stage.In);

                    switch (item)
                    {
                        case ConnectItem:
                            EnqueueOrPush(NetworkBufferTestExtensions.FromArray(ConnectEstablishedBytes));
                            _tunnelEstablished = true;
                            break;

                        case NetworkBuffer dataChunk when _tunnelEstablished:
                            var copy = new byte[dataChunk.Length];
                            dataChunk.Span.CopyTo(copy);
                            stage.OutboundChannel.Writer.TryWrite(NetworkBufferTestExtensions.FromArray(copy));
                            dataChunk.Dispose();

                            var responseBytes = _stage._responseFactory(_requestIndex++, copy);
                            if (responseBytes is null)
                            {
                                CompleteStage();
                                return;
                            }

                            EnqueueOrPush(NetworkBufferTestExtensions.FromArray(responseBytes));
                            break;

                        case NetworkBuffer strayChunk:
                            strayChunk.Dispose();
                            break;
                    }

                    if (!IsClosed(stage.In))
                    {
                        Pull(stage.In);
                    }
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

        private void EnqueueOrPush(IInputItem item)
        {
            if (_downstreamWaiting)
            {
                _downstreamWaiting = false;
                Push(_stage.Out, item);
            }
            else
            {
                _buffer.Enqueue(item);
            }
        }

        public override void PreStart() => Pull(_stage.In);
    }
}
