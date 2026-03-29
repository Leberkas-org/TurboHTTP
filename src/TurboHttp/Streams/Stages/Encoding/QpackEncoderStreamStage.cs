using System.Buffers;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.Protocol.RFC9204;

namespace TurboHttp.Streams.Stages.Encoding;

/// <summary>
/// RFC 9204 §4.3 — Serialises <see cref="EncoderInstruction"/> objects into bytes
/// for the outbound QPACK encoder stream (HTTP/3 unidirectional stream type 0x02).
///
/// The encoder stream carries dynamic-table mutations from encoder to decoder:
/// Set Dynamic Table Capacity, Insert With Name Reference, Insert With Literal Name, Duplicate.
/// </summary>
public sealed class QpackEncoderStreamStage : GraphStage<FlowShape<EncoderInstruction, ReadOnlyMemory<byte>>>
{
    private readonly Inlet<EncoderInstruction> _in = new("QpackEncoder.In");
    private readonly Outlet<ReadOnlyMemory<byte>> _out = new("QpackEncoder.Out");

    public QpackEncoderStreamStage()
    {
        Shape = new FlowShape<EncoderInstruction, ReadOnlyMemory<byte>>(_in, _out);
    }

    public override FlowShape<EncoderInstruction, ReadOnlyMemory<byte>> Shape { get; }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
    {
        return new Logic(this);
    }

    private sealed class Logic : GraphStageLogic
    {
        public Logic(QpackEncoderStreamStage stage) : base(stage.Shape)
        {
            SetHandler(stage._in,
                onPush: () =>
                {
                    var instruction = Grab(stage._in);
                    var writer = new ArrayBufferWriter<byte>();

                    try
                    {
                        switch (instruction.Type)
                        {
                            case EncoderInstructionType.SetDynamicTableCapacity:
                                QpackEncoderInstructionWriter.WriteSetDynamicTableCapacity(
                                    instruction.IntValue, writer);
                                break;

                            case EncoderInstructionType.InsertWithNameReference:
                                QpackEncoderInstructionWriter.WriteInsertWithNameReference(
                                    instruction.NameIndex, instruction.IsStatic,
                                    instruction.Value.AsSpan(), writer);
                                break;

                            case EncoderInstructionType.InsertWithLiteralName:
                                QpackEncoderInstructionWriter.WriteInsertWithLiteralName(
                                    instruction.Name.AsSpan(), instruction.Value.AsSpan(), writer);
                                break;

                            case EncoderInstructionType.Duplicate:
                                QpackEncoderInstructionWriter.WriteDuplicate(
                                    instruction.IntValue, writer);
                                break;

                            default:
                                Log.Warning(
                                    "QpackEncoderStreamStage: Unknown instruction type {0}",
                                    instruction.Type);
                                Pull(stage._in);
                                return;
                        }

                        Push(stage._out, writer.WrittenMemory.ToArray());
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(
                            "QpackEncoderStreamStage: Failed to encode instruction [{0}]: {1}",
                            instruction.Type, ex.Message);
                        if (!HasBeenPulled(stage._in))
                        {
                            Pull(stage._in);
                        }
                    }
                },
                onUpstreamFinish: CompleteStage,
                onUpstreamFailure: ex =>
                {
                    Log.Warning("QpackEncoderStreamStage: Upstream failure absorbed: {0}", ex.Message);
                    Log.Debug("QpackEncoderStreamStage: Failing stage due to upstream failure");
                    FailStage(ex);
                });

            SetHandler(stage._out,
                onPull: () => Pull(stage._in),
                onDownstreamFinish: _ => CompleteStage());
        }
    }
}
