using System;
using System.Buffers;
using System.Net.Http;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.Internal;
using TurboHttp.IO.Stages;
using TurboHttp.Protocol.RFC9112;

namespace TurboHttp.Streams.Stages;

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
                    IMemoryOwner<byte>? owner = null;

                    try
                    {
                        var key = RequestEndpoint.FromRequest(request);
                        var contentLength = Convert.ToInt32(request.Content?.Headers.ContentLength ?? 0);
                        var estimatedSize = _minBufferSize + contentLength;
                        var bufferSize = Math.Min(estimatedSize, _maxBufferSize);
                        owner = MemoryPool<byte>.Shared.Rent(bufferSize);
                        var buffer = owner.Memory.Span;

                        var written = Http11Encoder.Encode(request, ref buffer);

                        Push(stage._out, new DataItem(owner, written) { Key = key });
                    }
                    catch (Exception ex)
                    {
                        owner?.Dispose();
                        Log.Warning("Http11EncoderStage: Failed to encode request [{0}]: {1}",
                            request.RequestUri, ex.Message);
                        if (!HasBeenPulled(stage._in))
                        {
                            Pull(stage._in);
                        }
                    }
                },
                onUpstreamFinish: CompleteStage,
                onUpstreamFailure: ex => Log.Warning("Http11EncoderStage: Upstream failure absorbed: {0}", ex.Message));

            SetHandler(stage._out,
                onPull: () => Pull(stage._in),
                onDownstreamFinish: _ => CompleteStage());
        }
    }
}