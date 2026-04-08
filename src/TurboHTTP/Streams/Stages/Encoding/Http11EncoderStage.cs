using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHTTP.Internal;
using TurboHTTP.Protocol.Http11;

namespace TurboHTTP.Streams.Stages.Encoding;

public sealed class Http11EncoderStage : GraphStage<FlowShape<HttpRequestMessage, IOutputItem>>
{
    private readonly Inlet<HttpRequestMessage> _in = new("Http11Encoder.In");
    private readonly Outlet<IOutputItem> _out = new("Http11Encoder.Out");

    public Http11EncoderStage()
    {
        Shape = new FlowShape<HttpRequestMessage, IOutputItem>(_in, _out);
    }

    public override FlowShape<HttpRequestMessage, IOutputItem> Shape { get; }


    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
    {
        return new Logic(this, inheritedAttributes);
    }

    private sealed class Logic : GraphStageLogic
    {
        private readonly int _minBufferSize;
        private readonly int _maxBufferSize;

        public Logic(Http11EncoderStage stage, Attributes inheritedAttributes) : base(stage.Shape)
        {
            var memoryBuffer = inheritedAttributes.GetAttribute(new TurboAttributes.MemoryBuffer(4 * 1024, 256 * 1024));
            _minBufferSize = memoryBuffer.Initial;
            _maxBufferSize = memoryBuffer.Max;

            SetHandler(stage._in,
                onPush: () =>
                {
                    var request = Grab(stage._in);
                    NetworkBuffer? item = null;

                    try
                    {
                        var key = RequestEndpoint.FromRequest(request);
                        var contentLength = Convert.ToInt32(request.Content?.Headers.ContentLength ?? 0);
                        var estimatedSize = _minBufferSize + contentLength;
                        var bufferSize = Math.Min(estimatedSize, _maxBufferSize);
                        item = NetworkBuffer.Rent(bufferSize);
                        item.Key = key;
                        var span = item.FullMemory.Span;

                        var written = Http11Encoder.Encode(request, ref span);
                        item.Length = written;

                        Push(stage._out, item);
                    }
                    catch (Exception ex)
                    {
                        item?.Dispose();
                        Log.Warning("Http11EncoderStage: Failed to encode request [{0}]: {1}",
                            request.RequestUri, ex.Message);
                        if (!HasBeenPulled(stage._in))
                        {
                            Pull(stage._in);
                        }
                    }
                },
                onUpstreamFinish: CompleteStage,
                onUpstreamFailure: ex =>
                {
                    Log.Warning("Http11EncoderStage: Upstream failure absorbed: {0}", ex.Message);
                    CompleteStage();
                });

            SetHandler(stage._out,
                onPull: () => Pull(stage._in),
                onDownstreamFinish: _ => CompleteStage());
        }
    }
}