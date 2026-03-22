using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.Protocol.RFC9204;

namespace TurboHttp.Streams.Stages.Decoding;

/// <summary>
/// RFC 9204 §4.4 — Applies decoded QPACK decoder instructions to the encoder's state.
///
/// Receives <see cref="DecoderInstruction"/> items (deserialized by <see cref="QpackDecoderStreamStage"/>)
/// and feeds them back to the <see cref="QpackEncoder"/> so it can update its Known Received Count
/// and pending section tracking.
///
/// This is a sink stage: it consumes instructions and produces no output.
/// </summary>
public sealed class QpackDecoderFeedbackStage : GraphStage<SinkShape<DecoderInstruction>>
{
    private readonly Inlet<DecoderInstruction> _in = new("QpackDecoderFeedback.In");
    private readonly QpackEncoder _encoder;

    public QpackDecoderFeedbackStage(QpackEncoder encoder)
    {
        _encoder = encoder;
        Shape = new SinkShape<DecoderInstruction>(_in);
    }

    public override SinkShape<DecoderInstruction> Shape { get; }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
    {
        return new Logic(this);
    }

    private sealed class Logic : GraphStageLogic
    {
        private readonly QpackDecoderFeedbackStage _stage;

        public Logic(QpackDecoderFeedbackStage stage) : base(stage.Shape)
        {
            _stage = stage;

            SetHandler(stage._in,
                onPush: () =>
                {
                    var instruction = Grab(stage._in);
                    stage._encoder.ApplyDecoderInstruction(instruction);
                    Pull(stage._in);
                },
                onUpstreamFinish: CompleteStage,
                onUpstreamFailure: _ => CompleteStage());
        }

        public override void PreStart()
        {
            Pull(_stage._in);
        }
    }
}
