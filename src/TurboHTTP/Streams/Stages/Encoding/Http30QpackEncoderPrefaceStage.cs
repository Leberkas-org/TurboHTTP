using System.Buffers;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHTTP.Internal;

namespace TurboHTTP.Streams.Stages.Encoding;

/// <summary>
/// Prepends the QPACK encoder instruction stream type (VarInt 0x02) on first emission,
/// wraps all instruction bytes as <see cref="Http3OutputTaggedItem"/> with
/// <see cref="OutputStreamType.QpackEncoder"/>, and filters empty instruction buffers.
/// </summary>
/// <remarks>
/// RFC 9204 §4.2: The encoder sends instructions on a unidirectional stream of type 0x02.
/// The stream type VarInt is emitted exactly once as a prefix before the first instruction bytes.
/// </remarks>
public sealed class Http30QpackEncoderPrefaceStage : GraphStage<FlowShape<ReadOnlyMemory<byte>, IOutputItem>>
{
    private readonly Inlet<ReadOnlyMemory<byte>> _in = new("Http30QpackEncoderPreface.In");
    private readonly Outlet<IOutputItem> _out = new("Http30QpackEncoderPreface.Out");

    public override FlowShape<ReadOnlyMemory<byte>, IOutputItem> Shape => new(_in, _out);

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private bool _prefaceSent;

        public Logic(Http30QpackEncoderPrefaceStage stage) : base(stage.Shape)
        {
            SetHandler(stage._in, onPush: () =>
            {
                var instructions = Grab(stage._in);

                // Filter empty instruction buffers — pull again.
                if (instructions.Length == 0)
                {
                    Pull(stage._in);
                    return;
                }

                int totalLength;
                using var owner = MemoryPool<byte>.Shared.Rent(1 + instructions.Length);
                var span = owner.Memory.Span;

                if (!_prefaceSent)
                {
                    _prefaceSent = true;

                    // Prepend stream type VarInt(0x02) = single byte 0x02
                    span[0] = 0x02;
                    instructions.Span.CopyTo(span[1..]);
                    totalLength = 1 + instructions.Length;
                }
                else
                {
                    instructions.Span.CopyTo(span);
                    totalLength = instructions.Length;
                }

                var buf = NetworkBuffer.Rent(totalLength);
                owner.Memory.Span[..totalLength].CopyTo(buf.FullMemory.Span);
                buf.Length = totalLength;

                Push(stage._out, new Http3OutputTaggedItem(buf, OutputStreamType.QpackEncoder));
            },
            onUpstreamFinish: CompleteStage,
            onUpstreamFailure: ex =>
            {
                Log.Warning("Http30QpackEncoderPrefaceStage: Upstream failure absorbed: {0}", ex.Message);
                Log.Debug("Http30QpackEncoderPrefaceStage: Failing stage due to upstream error: {0}", ex.Message);
                FailStage(ex);
            });

            SetHandler(stage._out, onPull: () => Pull(stage._in));
        }
    }
}
