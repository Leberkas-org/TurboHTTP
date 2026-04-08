using Akka.Streams.Dsl;
using TurboHTTP.Protocol.Http3.Qpack;
using TurboHTTP.Streams.Stages.Decoding;

namespace TurboHTTP.StreamTests.Http3.Qpack;

/// <summary>
/// Tests the QPACK decoder feedback pipeline: <see cref="QpackDecoderStreamStage"/> deserialises
/// decoder instruction bytes, and <see cref="QpackDecoderFeedbackStage"/> applies them to the
/// <see cref="QpackEncoder"/> to update its Known Received Count and pending section tracking.
/// </summary>
/// <remarks>
/// RFC 9204 §4.4 — The decoder sends instructions to the encoder on a unidirectional stream
/// of type 0x03. These instructions update the encoder's view of which dynamic table entries
/// the decoder has received.
/// </remarks>
public sealed class QpackDecoderFeedbackSpec : StreamTestBase
{
    /// <summary>
    /// Runs the decoder stream → feedback stage pipeline and waits for completion.
    /// </summary>
    private async Task RunFeedbackPipeline(QpackEncoder encoder, params ReadOnlyMemory<byte>[] buffers)
    {
        var completionTask = Source.From(buffers)
            .Via(Flow.FromGraph(new QpackDecoderStreamStage()))
            .WatchTermination(Keep.Right)
            .To(Sink.FromGraph(new QpackDecoderFeedbackStage(encoder)))
            .Run(Materializer);

        await completionTask;
    }

    private static ReadOnlyMemory<byte> WriteSectionAcknowledgment(int streamId)
    {
        var buf = new byte[16];
        Span<byte> span = buf;
        var n = QpackDecoderInstructionWriter.WriteSectionAcknowledgment(streamId, ref span);
        return buf[..n];
    }

    private static ReadOnlyMemory<byte> WriteStreamCancellation(int streamId)
    {
        var buf = new byte[16];
        Span<byte> span = buf;
        var n = QpackDecoderInstructionWriter.WriteStreamCancellation(streamId, ref span);
        return buf[..n];
    }

    private static ReadOnlyMemory<byte> WriteInsertCountIncrement(int increment)
    {
        var buf = new byte[16];
        Span<byte> span = buf;
        var n = QpackDecoderInstructionWriter.WriteInsertCountIncrement(increment, ref span);
        return buf[..n];
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9204-4.4")]
    public async Task QpackDecoderFeedback_should_update_encoder_when_section_acknowledgment_received()
    {
        var encoder = new QpackEncoder(maxTableCapacity: 4096);
        encoder.TrackSection(streamId: 4, requiredInsertCount: 3);

        await RunFeedbackPipeline(encoder, WriteSectionAcknowledgment(4));

        Assert.Equal(3, encoder.KnownReceivedCount);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9204-4.4")]
    public async Task QpackDecoderFeedback_should_update_encoder_when_insert_count_increment_received()
    {
        var encoder = new QpackEncoder(maxTableCapacity: 4096);

        await RunFeedbackPipeline(encoder, WriteInsertCountIncrement(5));

        Assert.Equal(5, encoder.KnownReceivedCount);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9204-4.4")]
    public async Task QpackDecoderFeedback_should_remove_pending_section_when_stream_cancellation_received()
    {
        var encoder = new QpackEncoder(maxTableCapacity: 4096);
        encoder.TrackSection(streamId: 8, requiredInsertCount: 4);

        await RunFeedbackPipeline(encoder, WriteStreamCancellation(8));

        Assert.Equal(0, encoder.KnownReceivedCount);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9204-4.4")]
    public async Task QpackDecoderFeedback_should_apply_all_instructions_when_multiple_in_single_buffer()
    {
        var encoder = new QpackEncoder(maxTableCapacity: 4096);
        encoder.TrackSection(streamId: 0, requiredInsertCount: 2);
        encoder.TrackSection(streamId: 4, requiredInsertCount: 5);

        var buf = new byte[64];
        Span<byte> span = buf;
        var n = 0;
        n += QpackDecoderInstructionWriter.WriteInsertCountIncrement(1, ref span);
        n += QpackDecoderInstructionWriter.WriteSectionAcknowledgment(4, ref span);
        n += QpackDecoderInstructionWriter.WriteSectionAcknowledgment(0, ref span);
        ReadOnlyMemory<byte> data = buf[..n];

        await RunFeedbackPipeline(encoder, data);

        // Increment +1 → KRC=1, then ack stream 4 (ric=5) → KRC=5, then ack stream 0 (ric=2) → still 5
        Assert.Equal(5, encoder.KnownReceivedCount);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9204-4.4")]
    public async Task QpackDecoderFeedback_should_apply_all_instructions_when_split_across_buffers()
    {
        var encoder = new QpackEncoder(maxTableCapacity: 4096);
        encoder.TrackSection(streamId: 0, requiredInsertCount: 3);

        await RunFeedbackPipeline(encoder, WriteInsertCountIncrement(2), WriteSectionAcknowledgment(0));

        // Increment +2 → KRC=2, then ack stream 0 (ric=3) → KRC=3
        Assert.Equal(3, encoder.KnownReceivedCount);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9204-4.4")]
    public async Task QpackDecoderFeedback_should_complete_cleanly_when_no_instructions()
    {
        var encoder = new QpackEncoder(maxTableCapacity: 4096);

        var completionTask = Source.Empty<ReadOnlyMemory<byte>>()
            .Via(Flow.FromGraph(new QpackDecoderStreamStage()))
            .WatchTermination(Keep.Right)
            .To(Sink.FromGraph(new QpackDecoderFeedbackStage(encoder)))
            .Run(Materializer);

        await completionTask;

        Assert.Equal(0, encoder.KnownReceivedCount);
    }
}
