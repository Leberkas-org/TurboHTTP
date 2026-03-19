using System;
using System.Buffers;
using System.Net.Http;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.Internal;
using TurboHttp.IO.Stages;
using TurboHttp.Protocol.RFC1945;

namespace TurboHttp.Streams.Stages;

public sealed class Http10EncoderStage : GraphStage<FlowShape<HttpRequestMessage, IOutputItem>>
{
    private readonly Inlet<HttpRequestMessage> _inlet = new("http10.encoder.in");
    private readonly Outlet<IOutputItem> _outlet = new("http10.encoder.out");

    public Http10EncoderStage()
    {
        Shape = new FlowShape<HttpRequestMessage, IOutputItem>(_inlet, _outlet);
    }

    public override FlowShape<HttpRequestMessage, IOutputItem> Shape { get; }


    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this, inheritedAttributes);

    private sealed class Logic : GraphStageLogic
    {
        private readonly int _minBufferSize;
        private readonly int _maxBufferSize;

        public Logic(Http10EncoderStage stage, Attributes inheritedAttributes) : base(stage.Shape)
        {
            var memoryBuffer = inheritedAttributes.GetAttribute(new TurboAttributes.MemoryBuffer(4 * 1024, 256 * 1024));
            _minBufferSize = memoryBuffer.Initial;
            _maxBufferSize = memoryBuffer.Max;

            SetHandler(stage._inlet,
                onPush: () =>
                {
                    var request = Grab(stage._inlet);

                    try
                    {
                        var key = RequestEndpoint.FromRequest(request);
                        var contentLength = Convert.ToInt32(request.Content?.Headers.ContentLength ?? 0);
                        var estimatedSize = _minBufferSize + contentLength;
                        var bufferSize = Math.Min(estimatedSize, _maxBufferSize);
                        var owner = MemoryPool<byte>.Shared.Rent(bufferSize);
                        var buffer = owner.Memory;

                        var written = Http10Encoder.Encode(request, ref buffer);

                        Push(stage._outlet, new DataItem(owner, written) { Key = key });
                    }
                    catch (Exception ex)
                    {
                        FailStage(ex);
                    }
                },
                onUpstreamFinish: CompleteStage,
                onUpstreamFailure: FailStage);

            SetHandler(stage._outlet,
                onPull: () => Pull(stage._inlet),
                onDownstreamFinish: _ => CompleteStage());
        }
    }
}