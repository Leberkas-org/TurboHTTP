using System;
using System.Buffers;
using System.Net.Http;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.Protocol;

namespace TurboHttp.Streams.Stages;

public sealed class Http11EncoderStage : GraphStage<FlowShape<HttpRequestMessage, (IMemoryOwner<byte>, int)>>
    {
        private readonly Inlet<HttpRequestMessage> _inlet = new("http11.encoder.in");
        private readonly Outlet<(IMemoryOwner<byte>, int)> _outlet = new("http11.encoder.out");

        public Http11EncoderStage()
        {
            Shape = new FlowShape<HttpRequestMessage, (IMemoryOwner<byte>, int)>(_inlet, _outlet);
        }

        public override FlowShape<HttpRequestMessage, (IMemoryOwner<byte>, int)> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            return new Logic(this);
        }

        private sealed class Logic : GraphStageLogic
        {
            private const int MinBufferSize = 4 * 1024; // 4 KB
            private const int MaxBufferSize = 256 * 1024; // 256 KB

            public Logic(Http11EncoderStage stage) : base(stage.Shape)
            {
                SetHandler(stage._inlet,
                    onPush: () =>
                    {
                        var request = Grab(stage._inlet);

                        try
                        {
                            var contentLength = Convert.ToInt32(request.Content?.Headers.ContentLength ?? 0);
                            var estimatedSize = MinBufferSize + contentLength;
                            var bufferSize = Math.Min(estimatedSize, MaxBufferSize);
                            var owner = MemoryPool<byte>.Shared.Rent(bufferSize);
                            var buffer = owner.Memory.Span;

                            var written = Http11Encoder.Encode(request, ref buffer);

                            Push(stage._outlet, (owner, written));
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
