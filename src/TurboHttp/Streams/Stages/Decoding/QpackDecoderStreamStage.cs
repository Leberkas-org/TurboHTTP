using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.Protocol.Http3.Qpack;

namespace TurboHttp.Streams.Stages.Decoding;

/// <summary>
/// RFC 9204 §4.4 — Deserialises bytes from the inbound QPACK decoder stream
/// (HTTP/3 unidirectional stream type 0x03) into <see cref="DecoderInstruction"/> objects.
///
/// The decoder stream carries feedback from decoder to encoder:
/// Section Acknowledgment, Stream Cancellation, Insert Count Increment.
///
/// Maintains internal remainder state across partial reads via <see cref="QpackInstructionDecoder"/>.
/// </summary>
public sealed class QpackDecoderStreamStage : GraphStage<FlowShape<ReadOnlyMemory<byte>, DecoderInstruction>>
{
    private readonly Inlet<ReadOnlyMemory<byte>> _in = new("QpackDecoder.In");
    private readonly Outlet<DecoderInstruction> _out = new("QpackDecoder.Out");

    public QpackDecoderStreamStage()
    {
        Shape = new FlowShape<ReadOnlyMemory<byte>, DecoderInstruction>(_in, _out);
    }

    public override FlowShape<ReadOnlyMemory<byte>, DecoderInstruction> Shape { get; }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
    {
        return new Logic(this);
    }

    private sealed class Logic : GraphStageLogic
    {
        private readonly QpackInstructionDecoder _decoder = new();

        public Logic(QpackDecoderStreamStage stage) : base(stage.Shape)
        {
            SetHandler(stage._in,
                onPush: () =>
                {
                    var data = Grab(stage._in);

                    try
                    {
                        var instructions = _decoder.DecodeAllDecoderInstructions(data.Span);

                        if (instructions.Length > 0)
                        {
                            EmitMultiple(stage._out, instructions);
                        }
                        else
                        {
                            Pull(stage._in);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(
                            "QpackDecoderStreamStage: Failed to decode instructions: {0}",
                            ex.Message);
                        if (!HasBeenPulled(stage._in))
                        {
                            Pull(stage._in);
                        }
                    }
                },
                onUpstreamFinish: CompleteStage,
                onUpstreamFailure: ex =>
                {
                    Log.Warning("QpackDecoderStreamStage: Upstream failure absorbed: {0}", ex.Message);
                    Log.Debug("QpackDecoderStreamStage: Failing stage due to upstream failure");
                    FailStage(ex);
                });

            SetHandler(stage._out,
                onPull: () => Pull(stage._in),
                onDownstreamFinish: _ => CompleteStage());
            
        }
    }
}
