using System.Text;
using Akka.Streams.Dsl;
using TurboHTTP.Protocol.Http3.Qpack;
using TurboHTTP.Streams.Stages.Decoding;
using TurboHTTP.Streams.Stages.Encoding;

namespace TurboHTTP.StreamTests.Http3.Qpack;

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
public sealed class QpackStreamStageSpec : StreamTestBase
{
    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9204-4.3")]
    public async Task QpackStreamStage_should_encode_set_dynamic_table_capacity()
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

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9204-4.3")]
    public async Task QpackStreamStage_should_encode_insert_with_name_reference()
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

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9204-4.3")]
    public async Task QpackStreamStage_should_encode_insert_with_literal_name()
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

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9204-4.3")]
    public async Task QpackStreamStage_should_encode_duplicate()
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

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9204-4.4")]
    public async Task QpackStreamStage_should_decode_section_acknowledgment()
    {
        // Encode a SectionAcknowledgment instruction to bytes
        var buf = new byte[16];
        Span<byte> span = buf;
        var n = QpackDecoderInstructionWriter.WriteSectionAcknowledgment(4, ref span);
        ReadOnlyMemory<byte> data = buf[..n];

        var results = await Source.Single(data)
            .Via(Flow.FromGraph(new QpackDecoderStreamStage()))
            .RunWith(Sink.Seq<DecoderInstruction>(), Materializer);

        Assert.Single(results);
        Assert.Equal(DecoderInstructionType.SectionAcknowledgment, results[0].Type);
        Assert.Equal(4, results[0].IntValue);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9204-4.4")]
    public async Task QpackStreamStage_should_decode_stream_cancellation()
    {
        var buf = new byte[16];
        Span<byte> span = buf;
        var n = QpackDecoderInstructionWriter.WriteStreamCancellation(8, ref span);
        ReadOnlyMemory<byte> data = buf[..n];

        var results = await Source.Single(data)
            .Via(Flow.FromGraph(new QpackDecoderStreamStage()))
            .RunWith(Sink.Seq<DecoderInstruction>(), Materializer);

        Assert.Single(results);
        Assert.Equal(DecoderInstructionType.StreamCancellation, results[0].Type);
        Assert.Equal(8, results[0].IntValue);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9204-4.4")]
    public async Task QpackStreamStage_should_decode_insert_count_increment()
    {
        var buf = new byte[16];
        Span<byte> span = buf;
        var n = QpackDecoderInstructionWriter.WriteInsertCountIncrement(5, ref span);
        ReadOnlyMemory<byte> data = buf[..n];

        var results = await Source.Single(data)
            .Via(Flow.FromGraph(new QpackDecoderStreamStage()))
            .RunWith(Sink.Seq<DecoderInstruction>(), Materializer);

        Assert.Single(results);
        Assert.Equal(DecoderInstructionType.InsertCountIncrement, results[0].Type);
        Assert.Equal(5, results[0].IntValue);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9204-4.4")]
    public async Task QpackStreamStage_should_decode_multiple_instructions_in_single_buffer()
    {
        var buf = new byte[64];
        Span<byte> span = buf;
        var n = 0;
        n += QpackDecoderInstructionWriter.WriteSectionAcknowledgment(0, ref span);
        n += QpackDecoderInstructionWriter.WriteInsertCountIncrement(2, ref span);
        n += QpackDecoderInstructionWriter.WriteStreamCancellation(6, ref span);
        ReadOnlyMemory<byte> data = buf[..n];

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
}
