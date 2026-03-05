using System;
using System.Buffers;
using System.Net.Http;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.Protocol;

namespace TurboHttp.Streams.Stages;

public sealed class Http10DecoderStage : GraphStage<FlowShape<(IMemoryOwner<byte>, int), HttpResponseMessage>>
{
    private readonly Inlet<(IMemoryOwner<byte>, int)> _inlet = new("http10.decoder.in");
    private readonly Outlet<HttpResponseMessage> _outlet = new("http10.decoder.out");

    public override FlowShape<(IMemoryOwner<byte>, int), HttpResponseMessage> Shape { get; }

    public Http10DecoderStage()
    {
        Shape = new FlowShape<(IMemoryOwner<byte>, int), HttpResponseMessage>(_inlet, _outlet);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private readonly Http10Decoder _decoder = new();

        public Logic(Http10DecoderStage stage) : base(stage.Shape)
        {
            SetHandler(stage._inlet,
                onPush: () =>
                {
                    var (owner, length) = Grab(stage._inlet);

                    try
                    {
                        var data = owner.Memory[..length];

                        if (_decoder.TryDecode(data, out var response) && response is not null)
                        {
                            owner.Dispose();
                            Push(stage._outlet, response);
                        }
                        else
                        {
                            // Not enough data yet – return the buffer and wait for more
                            owner.Dispose();
                            Pull(stage._inlet);
                        }
                    }
                    catch (Exception ex)
                    {
                        owner.Dispose();
                        FailStage(ex);
                    }
                },
                onUpstreamFinish: () =>
                {
                    // Flush any partial response buffered in the decoder
                    if (_decoder.TryDecodeEof(out var response) && response is not null)
                    {
                        Emit(stage._outlet, response, CompleteStage);
                    }
                    else
                    {
                        CompleteStage();
                    }
                },
                onUpstreamFailure: FailStage);

            SetHandler(stage._outlet, onPull: () => Pull(stage._inlet), onDownstreamFinish: _ => CompleteStage());
        }
    }
}