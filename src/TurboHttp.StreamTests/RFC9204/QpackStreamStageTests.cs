using System;
using System.Buffers;
using System.Text;
using Akka.Streams.Dsl;
using TurboHttp.Protocol.RFC9204;
using TurboHttp.Streams.Stages.Decoding;
using TurboHttp.Streams.Stages.Encoding;

namespace TurboHttp.StreamTests.RFC9204;

/// <summary>
/// Tests QPACK encoder and decoder stream stages per RFC 9204 §4.3 and §4.4.
/// Verifies that encoder instructions are correctly serialised to bytes and
/// decoder instructions are correctly deserialised from bytes.
/// </summary>
/// <remarks>
/// Stages under test:
/// <see cref="QpackEncoderStreamStage"/> — serialises <see cref="EncoderInstruction"/> → bytes.
/// <see cref="QpackDecoderStreamStage"/> — deserialises bytes → <see cref="DecoderInstruction"/>.
/// </remarks>
public sealed class QpackStreamStageTests : StreamTestBase
{
    #region QpackEncoderStreamStage Tests

    [Fact(Timeout = 10_000, DisplayName = "RFC9204-4.3-QES-001: SetDynamicTableCapacity instruction is serialised correctly")]
    public async Task Should_Encode_SetDynamicTableCapacity()
    {
        var instruction = new EncoderInstruction
        {
            Type = EncoderInstructionType.SetDynamicTableCapacity,
            IntValue = 4096
        };

        var results = await Source.Single(instruction)
            .Via(Flow.FromGraph(new QpackEncoderStreamStage()))
            .RunWith(Sink.Seq<ReadOnlyMemory<byte>>(), Materializer);

        Assert.Single(results);

        // Verify roundtrip: decode the produced bytes back
        var decoder = new QpackInstructionDecoder();
        var decoded = decoder.DecodeAllEncoderInstructions(results[0].Span);
        Assert.Single(decoded);
        Assert.Equal(EncoderInstructionType.SetDynamicTableCapacity, decoded[0].Type);
        Assert.Equal(4096, decoded[0].IntValue);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9204-4.3-QES-002: InsertWithNameReference instruction is serialised correctly")]
    public async Task Should_Encode_InsertWithNameReference()
    {
        var instruction = new EncoderInstruction
        {
            Type = EncoderInstructionType.InsertWithNameReference,
            NameIndex = 1,
            IsStatic = true,
            Value = Encoding.UTF8.GetBytes("example.com")
        };

        var results = await Source.Single(instruction)
            .Via(Flow.FromGraph(new QpackEncoderStreamStage()))
            .RunWith(Sink.Seq<ReadOnlyMemory<byte>>(), Materializer);

        Assert.Single(results);

        var decoder = new QpackInstructionDecoder();
        var decoded = decoder.DecodeAllEncoderInstructions(results[0].Span);
        Assert.Single(decoded);
        Assert.Equal(EncoderInstructionType.InsertWithNameReference, decoded[0].Type);
        Assert.Equal(1, decoded[0].NameIndex);
        Assert.True(decoded[0].IsStatic);
        Assert.Equal("example.com", decoded[0].ValueString);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9204-4.3-QES-003: InsertWithLiteralName instruction is serialised correctly")]
    public async Task Should_Encode_InsertWithLiteralName()
    {
        var instruction = new EncoderInstruction
        {
            Type = EncoderInstructionType.InsertWithLiteralName,
            Name = Encoding.UTF8.GetBytes("x-custom"),
            Value = Encoding.UTF8.GetBytes("value123")
        };

        var results = await Source.Single(instruction)
            .Via(Flow.FromGraph(new QpackEncoderStreamStage()))
            .RunWith(Sink.Seq<ReadOnlyMemory<byte>>(), Materializer);

        Assert.Single(results);

        var decoder = new QpackInstructionDecoder();
        var decoded = decoder.DecodeAllEncoderInstructions(results[0].Span);
        Assert.Single(decoded);
        Assert.Equal(EncoderInstructionType.InsertWithLiteralName, decoded[0].Type);
        Assert.Equal("x-custom", decoded[0].NameString);
        Assert.Equal("value123", decoded[0].ValueString);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9204-4.3-QES-004: Duplicate instruction is serialised correctly")]
    public async Task Should_Encode_Duplicate()
    {
        var instruction = new EncoderInstruction
        {
            Type = EncoderInstructionType.Duplicate,
            IntValue = 3
        };

        var results = await Source.Single(instruction)
            .Via(Flow.FromGraph(new QpackEncoderStreamStage()))
            .RunWith(Sink.Seq<ReadOnlyMemory<byte>>(), Materializer);

        Assert.Single(results);

        var decoder = new QpackInstructionDecoder();
        var decoded = decoder.DecodeAllEncoderInstructions(results[0].Span);
        Assert.Single(decoded);
        Assert.Equal(EncoderInstructionType.Duplicate, decoded[0].Type);
        Assert.Equal(3, decoded[0].IntValue);
    }

    #endregion

    #region QpackDecoderStreamStage Tests

    [Fact(Timeout = 10_000, DisplayName = "RFC9204-4.4-QDS-001: SectionAcknowledgment is decoded from bytes")]
    public async Task Should_Decode_SectionAcknowledgment()
    {
        // Encode a SectionAcknowledgment instruction to bytes
        var writer = new ArrayBufferWriter<byte>();
        QpackDecoderInstructionWriter.WriteSectionAcknowledgment(4, writer);
        ReadOnlyMemory<byte> data = writer.WrittenMemory.ToArray();

        var results = await Source.Single(data)
            .Via(Flow.FromGraph(new QpackDecoderStreamStage()))
            .RunWith(Sink.Seq<DecoderInstruction>(), Materializer);

        Assert.Single(results);
        Assert.Equal(DecoderInstructionType.SectionAcknowledgment, results[0].Type);
        Assert.Equal(4, results[0].IntValue);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9204-4.4-QDS-002: StreamCancellation is decoded from bytes")]
    public async Task Should_Decode_StreamCancellation()
    {
        var writer = new ArrayBufferWriter<byte>();
        QpackDecoderInstructionWriter.WriteStreamCancellation(8, writer);
        ReadOnlyMemory<byte> data = writer.WrittenMemory.ToArray();

        var results = await Source.Single(data)
            .Via(Flow.FromGraph(new QpackDecoderStreamStage()))
            .RunWith(Sink.Seq<DecoderInstruction>(), Materializer);

        Assert.Single(results);
        Assert.Equal(DecoderInstructionType.StreamCancellation, results[0].Type);
        Assert.Equal(8, results[0].IntValue);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9204-4.4-QDS-003: InsertCountIncrement is decoded from bytes")]
    public async Task Should_Decode_InsertCountIncrement()
    {
        var writer = new ArrayBufferWriter<byte>();
        QpackDecoderInstructionWriter.WriteInsertCountIncrement(5, writer);
        ReadOnlyMemory<byte> data = writer.WrittenMemory.ToArray();

        var results = await Source.Single(data)
            .Via(Flow.FromGraph(new QpackDecoderStreamStage()))
            .RunWith(Sink.Seq<DecoderInstruction>(), Materializer);

        Assert.Single(results);
        Assert.Equal(DecoderInstructionType.InsertCountIncrement, results[0].Type);
        Assert.Equal(5, results[0].IntValue);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9204-4.4-QDS-004: Multiple instructions in single buffer are all decoded")]
    public async Task Should_Decode_Multiple_Instructions_In_Single_Buffer()
    {
        var writer = new ArrayBufferWriter<byte>();
        QpackDecoderInstructionWriter.WriteSectionAcknowledgment(0, writer);
        QpackDecoderInstructionWriter.WriteInsertCountIncrement(2, writer);
        QpackDecoderInstructionWriter.WriteStreamCancellation(6, writer);
        ReadOnlyMemory<byte> data = writer.WrittenMemory.ToArray();

        var results = await Source.Single(data)
            .Via(Flow.FromGraph(new QpackDecoderStreamStage()))
            .RunWith(Sink.Seq<DecoderInstruction>(), Materializer);

        Assert.Equal(3, results.Count);
        Assert.Equal(DecoderInstructionType.SectionAcknowledgment, results[0].Type);
        Assert.Equal(0, results[0].IntValue);
        Assert.Equal(DecoderInstructionType.InsertCountIncrement, results[1].Type);
        Assert.Equal(2, results[1].IntValue);
        Assert.Equal(DecoderInstructionType.StreamCancellation, results[2].Type);
        Assert.Equal(6, results[2].IntValue);
    }

    #endregion
}
