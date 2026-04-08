using System.Buffers;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHTTP.Protocol.Http3.Qpack;

namespace TurboHTTP.Streams.Stages.Encoding;

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
        // Tracks the owner of the most recently pushed memory.
        // Disposed when the next element is pushed (Akka back-pressure guarantees
        // the downstream has consumed the previous element by then) or on stage completion.
        private IMemoryOwner<byte>? _pushedOwner;

        public Logic(QpackEncoderStreamStage stage) : base(stage.Shape)
        {
            SetHandler(stage._in,
                onPush: () =>
                {
                    var instruction = Grab(stage._in);
                    // Dispose the owner from the previous push — downstream has consumed it.
                    _pushedOwner?.Dispose();
                    _pushedOwner = null;

                    var owner = MemoryPool<byte>.Shared.Rent(1024);
                    var span = owner.Memory.Span[..1024];

                    try
                    {
                        int written;
                        switch (instruction.Type)
                        {
                            case EncoderInstructionType.SetDynamicTableCapacity:
                                written = QpackEncoderInstructionWriter.WriteSetDynamicTableCapacity(
                                    instruction.IntValue, ref span);
                                break;

                            case EncoderInstructionType.InsertWithNameReference:
                                written = QpackEncoderInstructionWriter.WriteInsertWithNameReference(
                                    instruction.NameIndex, instruction.IsStatic,
                                    instruction.Value.AsSpan(), ref span);
                                break;

                            case EncoderInstructionType.InsertWithLiteralName:
                                written = QpackEncoderInstructionWriter.WriteInsertWithLiteralName(
                                    instruction.Name.AsSpan(), instruction.Value.AsSpan(), ref span);
                                break;

                            case EncoderInstructionType.Duplicate:
                                written = QpackEncoderInstructionWriter.WriteDuplicate(
                                    instruction.IntValue, ref span);
                                break;

                            default:
                                owner.Dispose();
                                Log.Warning(
                                    "QpackEncoderStreamStage: Unknown instruction type {0}",
                                    instruction.Type);
                                Pull(stage._in);
                                return;
                        }

                        _pushedOwner = owner;
                        Push(stage._out, owner.Memory[..written]);
                    }
                    catch (Exception ex)
                    {
                        owner.Dispose();
                        Log.Warning(
                            "QpackEncoderStreamStage: Failed to encode instruction [{0}]: {1}",
                            instruction.Type, ex.Message);
                        if (!HasBeenPulled(stage._in))
                        {
                            Pull(stage._in);
                        }
                    }
                },
                onUpstreamFinish: () =>
                {
                    _pushedOwner?.Dispose();
                    _pushedOwner = null;
                    CompleteStage();
                },
                onUpstreamFailure: ex =>
                {
                    _pushedOwner?.Dispose();
                    _pushedOwner = null;
                    Log.Warning("QpackEncoderStreamStage: Upstream failure absorbed: {0}", ex.Message);
                    Log.Debug("QpackEncoderStreamStage: Failing stage due to upstream failure");
                    FailStage(ex);
                });

            SetHandler(stage._out,
                onPull: () => Pull(stage._in),
                onDownstreamFinish: _ =>
                {
                    _pushedOwner?.Dispose();
                    _pushedOwner = null;
                    CompleteStage();
                });
        }
    }
}
