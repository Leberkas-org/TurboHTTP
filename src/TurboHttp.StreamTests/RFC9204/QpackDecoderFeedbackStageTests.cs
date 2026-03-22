using System;
using System.Buffers;
using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.Protocol.RFC9204;
using TurboHttp.Streams.Stages.Decoding;

namespace TurboHttp.StreamTests.RFC9204;

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
public sealed class QpackDecoderFeedbackStageTests : StreamTestBase
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

    [Fact(Timeout = 10_000, DisplayName = "RFC9204-4.4-QDFB-001: SectionAcknowledgment flows through pipeline to encoder")]
    public async Task Should_UpdateEncoder_When_SectionAcknowledgmentReceived()
    {
        var encoder = new QpackEncoder(maxTableCapacity: 4096);
        encoder.TrackSection(streamId: 4, requiredInsertCount: 3);

        var writer = new ArrayBufferWriter<byte>();
        QpackDecoderInstructionWriter.WriteSectionAcknowledgment(4, writer);
        ReadOnlyMemory<byte> data = writer.WrittenMemory.ToArray();

        await RunFeedbackPipeline(encoder, data);

        Assert.Equal(3, encoder.KnownReceivedCount);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9204-4.4-QDFB-002: InsertCountIncrement flows through pipeline to encoder")]
    public async Task Should_UpdateEncoder_When_InsertCountIncrementReceived()
    {
        var encoder = new QpackEncoder(maxTableCapacity: 4096);

        var writer = new ArrayBufferWriter<byte>();
        QpackDecoderInstructionWriter.WriteInsertCountIncrement(5, writer);
        ReadOnlyMemory<byte> data = writer.WrittenMemory.ToArray();

        await RunFeedbackPipeline(encoder, data);

        Assert.Equal(5, encoder.KnownReceivedCount);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9204-4.4-QDFB-003: StreamCancellation removes pending section via pipeline")]
    public async Task Should_RemovePendingSection_When_StreamCancellationReceived()
    {
        var encoder = new QpackEncoder(maxTableCapacity: 4096);
        encoder.TrackSection(streamId: 8, requiredInsertCount: 4);

        var writer = new ArrayBufferWriter<byte>();
        QpackDecoderInstructionWriter.WriteStreamCancellation(8, writer);
        ReadOnlyMemory<byte> data = writer.WrittenMemory.ToArray();

        await RunFeedbackPipeline(encoder, data);

        Assert.Equal(0, encoder.KnownReceivedCount);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9204-4.4-QDFB-004: Multiple instructions in single buffer all applied")]
    public async Task Should_ApplyAllInstructions_When_MultipleInSingleBuffer()
    {
        var encoder = new QpackEncoder(maxTableCapacity: 4096);
        encoder.TrackSection(streamId: 0, requiredInsertCount: 2);
        encoder.TrackSection(streamId: 4, requiredInsertCount: 5);

        var writer = new ArrayBufferWriter<byte>();
        QpackDecoderInstructionWriter.WriteInsertCountIncrement(1, writer);
        QpackDecoderInstructionWriter.WriteSectionAcknowledgment(4, writer);
        QpackDecoderInstructionWriter.WriteSectionAcknowledgment(0, writer);
        ReadOnlyMemory<byte> data = writer.WrittenMemory.ToArray();

        await RunFeedbackPipeline(encoder, data);

        // Increment +1 → KRC=1, then ack stream 4 (ric=5) → KRC=5, then ack stream 0 (ric=2) → still 5
        Assert.Equal(5, encoder.KnownReceivedCount);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9204-4.4-QDFB-005: Multiple separate buffers all applied in sequence")]
    public async Task Should_ApplyAllInstructions_When_SplitAcrossBuffers()
    {
        var encoder = new QpackEncoder(maxTableCapacity: 4096);
        encoder.TrackSection(streamId: 0, requiredInsertCount: 3);

        var writer1 = new ArrayBufferWriter<byte>();
        QpackDecoderInstructionWriter.WriteInsertCountIncrement(2, writer1);
        ReadOnlyMemory<byte> data1 = writer1.WrittenMemory.ToArray();

        var writer2 = new ArrayBufferWriter<byte>();
        QpackDecoderInstructionWriter.WriteSectionAcknowledgment(0, writer2);
        ReadOnlyMemory<byte> data2 = writer2.WrittenMemory.ToArray();

        await RunFeedbackPipeline(encoder, data1, data2);

        // Increment +2 → KRC=2, then ack stream 0 (ric=3) → KRC=3
        Assert.Equal(3, encoder.KnownReceivedCount);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9204-4.4-QDFB-006: Empty source completes without error")]
    public async Task Should_CompleteCleanly_When_NoInstructions()
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
