using TurboHttp.Protocol.Http3.Qpack;

namespace TurboHttp.Tests.Http3.Qpack;

/// <summary>
/// Tests for QPACK decoder instruction feedback per RFC 9204 §4.4.
/// Verifies that <see cref="QpackEncoder.ApplyDecoderInstruction"/> correctly
/// updates the encoder's Known Received Count and pending section tracking
/// in response to Section Acknowledgment, Insert Count Increment, and
/// Stream Cancellation instructions.
/// </summary>
public sealed class QpackDecoderFeedbackSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.4.1")]
    public void Should_UpdateKnownReceivedCount_When_SectionAcknowledged()
    {
        var encoder = new QpackEncoder(maxTableCapacity: 4096);
        encoder.TrackSection(streamId: 4, requiredInsertCount: 3);

        encoder.ApplyDecoderInstruction(new DecoderInstruction
        {
            Type = DecoderInstructionType.SectionAcknowledgment,
            IntValue = 4
        });

        Assert.Equal(3, encoder.KnownReceivedCount);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.4.1")]
    public void Should_TakeMax_When_AcknowledgingLowerInsertCount()
    {
        var encoder = new QpackEncoder(maxTableCapacity: 4096);
        encoder.TrackSection(streamId: 4, requiredInsertCount: 5);
        encoder.TrackSection(streamId: 8, requiredInsertCount: 2);

        // Acknowledge stream 4 first (ric=5)
        encoder.ApplyDecoderInstruction(new DecoderInstruction
        {
            Type = DecoderInstructionType.SectionAcknowledgment,
            IntValue = 4
        });
        Assert.Equal(5, encoder.KnownReceivedCount);

        // Acknowledge stream 8 (ric=2) — should not decrease
        encoder.ApplyDecoderInstruction(new DecoderInstruction
        {
            Type = DecoderInstructionType.SectionAcknowledgment,
            IntValue = 8
        });
        Assert.Equal(5, encoder.KnownReceivedCount);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.4.1")]
    public void Should_NoOp_When_AcknowledgingUnknownStream()
    {
        var encoder = new QpackEncoder(maxTableCapacity: 4096);

        // No tracked section — should not throw or change state
        encoder.ApplyDecoderInstruction(new DecoderInstruction
        {
            Type = DecoderInstructionType.SectionAcknowledgment,
            IntValue = 99
        });

        Assert.Equal(0, encoder.KnownReceivedCount);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.4.3")]
    public void Should_IncrementKnownReceivedCount_When_InsertCountIncrementReceived()
    {
        var encoder = new QpackEncoder(maxTableCapacity: 4096);

        encoder.ApplyDecoderInstruction(new DecoderInstruction
        {
            Type = DecoderInstructionType.InsertCountIncrement,
            IntValue = 3
        });

        Assert.Equal(3, encoder.KnownReceivedCount);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.4.3")]
    public void Should_AccumulateIncrements_When_MultipleCalls()
    {
        var encoder = new QpackEncoder(maxTableCapacity: 4096);

        encoder.ApplyDecoderInstruction(new DecoderInstruction
        {
            Type = DecoderInstructionType.InsertCountIncrement,
            IntValue = 2
        });
        encoder.ApplyDecoderInstruction(new DecoderInstruction
        {
            Type = DecoderInstructionType.InsertCountIncrement,
            IntValue = 3
        });

        Assert.Equal(5, encoder.KnownReceivedCount);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.4.3")]
    public void Should_Throw_When_InsertCountIncrementIsZero()
    {
        var encoder = new QpackEncoder(maxTableCapacity: 4096);

        Assert.Throws<QpackException>(() =>
            encoder.ApplyDecoderInstruction(new DecoderInstruction
            {
                Type = DecoderInstructionType.InsertCountIncrement,
                IntValue = 0
            }));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.4.2")]
    public void Should_RemovePendingSection_When_StreamCancelled()
    {
        var encoder = new QpackEncoder(maxTableCapacity: 4096);
        encoder.TrackSection(streamId: 4, requiredInsertCount: 3);

        encoder.ApplyDecoderInstruction(new DecoderInstruction
        {
            Type = DecoderInstructionType.StreamCancellation,
            IntValue = 4
        });

        // Subsequent acknowledgment for same stream should be a no-op
        encoder.ApplyDecoderInstruction(new DecoderInstruction
        {
            Type = DecoderInstructionType.SectionAcknowledgment,
            IntValue = 4
        });

        Assert.Equal(0, encoder.KnownReceivedCount);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.4.2")]
    public void Should_NoOp_When_CancellingUnknownStream()
    {
        var encoder = new QpackEncoder(maxTableCapacity: 4096);

        // Should not throw
        encoder.ApplyDecoderInstruction(new DecoderInstruction
        {
            Type = DecoderInstructionType.StreamCancellation,
            IntValue = 99
        });

        Assert.Equal(0, encoder.KnownReceivedCount);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.4")]
    public void Should_NotTrack_When_RequiredInsertCountIsZero()
    {
        var encoder = new QpackEncoder(maxTableCapacity: 4096);
        encoder.TrackSection(streamId: 4, requiredInsertCount: 0);

        // Acknowledge should be a no-op since nothing was tracked
        encoder.ApplyDecoderInstruction(new DecoderInstruction
        {
            Type = DecoderInstructionType.SectionAcknowledgment,
            IntValue = 4
        });

        Assert.Equal(0, encoder.KnownReceivedCount);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.4")]
    public void Should_Throw_When_InstructionIsNull()
    {
        var encoder = new QpackEncoder(maxTableCapacity: 4096);

        Assert.Throws<ArgumentNullException>(() =>
            encoder.ApplyDecoderInstruction(null!));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.4")]
    public void Should_HandleMixedInstructions_Correctly()
    {
        var encoder = new QpackEncoder(maxTableCapacity: 4096);
        encoder.TrackSection(streamId: 0, requiredInsertCount: 2);
        encoder.TrackSection(streamId: 4, requiredInsertCount: 5);
        encoder.TrackSection(streamId: 8, requiredInsertCount: 3);

        // Increment by 1
        encoder.ApplyDecoderInstruction(new DecoderInstruction
        {
            Type = DecoderInstructionType.InsertCountIncrement,
            IntValue = 1
        });
        Assert.Equal(1, encoder.KnownReceivedCount);

        // Cancel stream 8
        encoder.ApplyDecoderInstruction(new DecoderInstruction
        {
            Type = DecoderInstructionType.StreamCancellation,
            IntValue = 8
        });

        // Acknowledge stream 4 (ric=5, which is > current 1)
        encoder.ApplyDecoderInstruction(new DecoderInstruction
        {
            Type = DecoderInstructionType.SectionAcknowledgment,
            IntValue = 4
        });
        Assert.Equal(5, encoder.KnownReceivedCount);

        // Acknowledge stream 0 (ric=2, which is < current 5)
        encoder.ApplyDecoderInstruction(new DecoderInstruction
        {
            Type = DecoderInstructionType.SectionAcknowledgment,
            IntValue = 0
        });
        Assert.Equal(5, encoder.KnownReceivedCount);
    }
}
