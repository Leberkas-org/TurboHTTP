using System.Text;
using System.Threading.Channels;
using Akka.Streams;
using Akka.Streams.Stage;
using Servus.Akka.Transport;

namespace TurboHTTP.Tests.Shared;

internal sealed class FakeProxyStage : GraphStage<FlowShape<ITransportOutbound, ITransportInbound>>
{
    private static readonly byte[] ConnectEstablishedBytes =
        Encoding.Latin1.GetBytes("HTTP/1.1 200 Connection Established\r\n\r\n");

    private readonly Func<int, byte[], byte[]?> _responseFactory;

    public Channel<TransportBuffer> OutboundChannel { get; } = Channel.CreateUnbounded<TransportBuffer>();

    public Inlet<ITransportOutbound> In { get; } = new("FakeProxy.In");
    public Outlet<ITransportInbound> Out { get; } = new("FakeProxy.Out");

    public override FlowShape<ITransportOutbound, ITransportInbound> Shape { get; }

    public FakeProxyStage(Func<int, byte[], byte[]?> responseFactory)
    {
        _responseFactory = responseFactory;
        Shape = new FlowShape<ITransportOutbound, ITransportInbound>(In, Out);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private readonly FakeProxyStage _stage;
        private readonly Queue<ITransportInbound> _buffer = new();
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
                        case ConnectTransport:
                            EnqueueOrPush(new TransportData(TransportBufferTestExtensions.FromArray(ConnectEstablishedBytes)));
                            _tunnelEstablished = true;
                            break;

                        case TransportData { Buffer: var dataChunk } when _tunnelEstablished:
                            var copy = new byte[dataChunk.Length];
                            dataChunk.Span.CopyTo(copy);
                            stage.OutboundChannel.Writer.TryWrite(TransportBufferTestExtensions.FromArray(copy));
                            dataChunk.Dispose();

                            var responseBytes = _stage._responseFactory(_requestIndex++, copy);
                            if (responseBytes is null)
                            {
                                CompleteStage();
                                return;
                            }

                            EnqueueOrPush(new TransportData(TransportBufferTestExtensions.FromArray(responseBytes)));
                            break;

                        case TransportData { Buffer: var strayChunk }:
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

        private void EnqueueOrPush(ITransportInbound item)
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
