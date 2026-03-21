using System;
using System.Buffers;
using TurboHttp.Protocol.RFC9204;

namespace TurboHttp.Tests.RFC9204;

/// <summary>
/// Tests for QPACK instruction stream parser per RFC 9204 §4.3 and §4.4.
/// Covers encoder instructions, decoder instructions, and partial data handling.
/// </summary>
public sealed class QpackInstructionDecoderTests
{
    private readonly QpackInstructionDecoder _decoder = new();
    private readonly ArrayBufferWriter<byte> _output = new();

    // ── Encoder Instructions: Set Dynamic Table Capacity (§4.3.1) ────

    [Theory(DisplayName = "RFC9204-4.3.1-ID-001: Decode Set Dynamic Table Capacity")]
    [InlineData(0)]
    [InlineData(100)]
    [InlineData(4096)]
    public void Should_DecodeSetDynamicTableCapacity(int capacity)
    {
        QpackEncoderInstructionWriter.WriteSetDynamicTableCapacity(capacity, _output);

        var status = _decoder.TryDecodeEncoderInstruction(_output.WrittenSpan, out var instruction);

        Assert.Equal(QpackDecodeStatus.Success, status);
        Assert.NotNull(instruction);
        Assert.Equal(EncoderInstructionType.SetDynamicTableCapacity, instruction.Type);
        Assert.Equal(capacity, instruction.IntValue);
    }

    // ── Encoder Instructions: Insert With Name Reference (§4.3.2) ────

    [Fact(DisplayName = "RFC9204-4.3.2-ID-002: Decode Insert With Name Reference (static)")]
    public void Should_DecodeInsertWithNameReference_Static()
    {
        QpackEncoderInstructionWriter.WriteInsertWithNameReference(15, true, "example.com", _output);

        var status = _decoder.TryDecodeEncoderInstruction(_output.WrittenSpan, out var instruction);

        Assert.Equal(QpackDecodeStatus.Success, status);
        Assert.NotNull(instruction);
        Assert.Equal(EncoderInstructionType.InsertWithNameReference, instruction.Type);
        Assert.Equal(15, instruction.NameIndex);
        Assert.True(instruction.IsStatic);
        Assert.Equal("example.com", instruction.ValueString);
    }

    [Fact(DisplayName = "RFC9204-4.3.2-ID-003: Decode Insert With Name Reference (dynamic)")]
    public void Should_DecodeInsertWithNameReference_Dynamic()
    {
        QpackEncoderInstructionWriter.WriteInsertWithNameReference(3, false, "bar", _output);

        var status = _decoder.TryDecodeEncoderInstruction(_output.WrittenSpan, out var instruction);

        Assert.Equal(QpackDecodeStatus.Success, status);
        Assert.NotNull(instruction);
        Assert.Equal(EncoderInstructionType.InsertWithNameReference, instruction.Type);
        Assert.Equal(3, instruction.NameIndex);
        Assert.False(instruction.IsStatic);
        Assert.Equal("bar", instruction.ValueString);
    }

    // ── Encoder Instructions: Insert With Literal Name (§4.3.3) ─────

    [Fact(DisplayName = "RFC9204-4.3.3-ID-004: Decode Insert With Literal Name")]
    public void Should_DecodeInsertWithLiteralName()
    {
        QpackEncoderInstructionWriter.WriteInsertWithLiteralName("x-custom", "hello", _output);

        var status = _decoder.TryDecodeEncoderInstruction(_output.WrittenSpan, out var instruction);

        Assert.Equal(QpackDecodeStatus.Success, status);
        Assert.NotNull(instruction);
        Assert.Equal(EncoderInstructionType.InsertWithLiteralName, instruction.Type);
        Assert.Equal("x-custom", instruction.NameString);
        Assert.Equal("hello", instruction.ValueString);
    }

    // ── Encoder Instructions: Duplicate (§4.3.4) ────────────────────

    [Theory(DisplayName = "RFC9204-4.3.4-ID-005: Decode Duplicate")]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(100)]
    public void Should_DecodeDuplicate(int index)
    {
        QpackEncoderInstructionWriter.WriteDuplicate(index, _output);

        var status = _decoder.TryDecodeEncoderInstruction(_output.WrittenSpan, out var instruction);

        Assert.Equal(QpackDecodeStatus.Success, status);
        Assert.NotNull(instruction);
        Assert.Equal(EncoderInstructionType.Duplicate, instruction.Type);
        Assert.Equal(index, instruction.IntValue);
    }

    // ── Decoder Instructions: Section Acknowledgment (§4.4.1) ───────

    [Theory(DisplayName = "RFC9204-4.4.1-ID-006: Decode Section Acknowledgment")]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(200)]
    public void Should_DecodeSectionAcknowledgment(int streamId)
    {
        QpackDecoderInstructionWriter.WriteSectionAcknowledgment(streamId, _output);

        var status = _decoder.TryDecodeDecoderInstruction(_output.WrittenSpan, out var instruction);

        Assert.Equal(QpackDecodeStatus.Success, status);
        Assert.NotNull(instruction);
        Assert.Equal(DecoderInstructionType.SectionAcknowledgment, instruction.Type);
        Assert.Equal(streamId, instruction.IntValue);
    }

    // ── Decoder Instructions: Stream Cancellation (§4.4.2) ──────────

    [Theory(DisplayName = "RFC9204-4.4.2-ID-007: Decode Stream Cancellation")]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(63)]
    public void Should_DecodeStreamCancellation(int streamId)
    {
        QpackDecoderInstructionWriter.WriteStreamCancellation(streamId, _output);

        var status = _decoder.TryDecodeDecoderInstruction(_output.WrittenSpan, out var instruction);

        Assert.Equal(QpackDecodeStatus.Success, status);
        Assert.NotNull(instruction);
        Assert.Equal(DecoderInstructionType.StreamCancellation, instruction.Type);
        Assert.Equal(streamId, instruction.IntValue);
    }

    // ── Decoder Instructions: Insert Count Increment (§4.4.3) ───────

    [Theory(DisplayName = "RFC9204-4.4.3-ID-008: Decode Insert Count Increment")]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(100)]
    public void Should_DecodeInsertCountIncrement(int increment)
    {
        QpackDecoderInstructionWriter.WriteInsertCountIncrement(increment, _output);

        var status = _decoder.TryDecodeDecoderInstruction(_output.WrittenSpan, out var instruction);

        Assert.Equal(QpackDecodeStatus.Success, status);
        Assert.NotNull(instruction);
        Assert.Equal(DecoderInstructionType.InsertCountIncrement, instruction.Type);
        Assert.Equal(increment, instruction.IntValue);
    }

    // ── Partial Instruction Handling ─────────────────────────────────

    [Fact(DisplayName = "RFC9204-4.3-ID-009: Partial encoder instruction returns NeedMoreData")]
    public void Should_ReturnNeedMoreData_WhenEncoderInstructionTruncated()
    {
        // Write a full Insert With Literal Name instruction
        QpackEncoderInstructionWriter.WriteInsertWithLiteralName("x-test", "value", _output);
        var full = _output.WrittenSpan.ToArray();

        // Feed only the first byte — not enough to decode name+value
        var status = _decoder.TryDecodeEncoderInstruction(full.AsSpan(0, 1), out var instruction);

        Assert.Equal(QpackDecodeStatus.NeedMoreData, status);
        Assert.Null(instruction);
        Assert.True(_decoder.HasRemainder);

        // Feed the rest
        status = _decoder.TryDecodeEncoderInstruction(full.AsSpan(1), out instruction);

        Assert.Equal(QpackDecodeStatus.Success, status);
        Assert.NotNull(instruction);
        Assert.Equal(EncoderInstructionType.InsertWithLiteralName, instruction.Type);
        Assert.Equal("x-test", instruction.NameString);
        Assert.Equal("value", instruction.ValueString);
    }

    [Fact(DisplayName = "RFC9204-4.4-ID-010: Partial decoder instruction returns NeedMoreData")]
    public void Should_ReturnNeedMoreData_WhenDecoderInstructionTruncated()
    {
        // Multi-byte integer: stream ID 200 → 0xFF 0x49
        QpackDecoderInstructionWriter.WriteSectionAcknowledgment(200, _output);
        var full = _output.WrittenSpan.ToArray();
        Assert.True(full.Length > 1); // Must be multi-byte

        // Feed only first byte
        var status = _decoder.TryDecodeDecoderInstruction(full.AsSpan(0, 1), out var instruction);

        Assert.Equal(QpackDecodeStatus.NeedMoreData, status);
        Assert.Null(instruction);

        // Feed rest
        status = _decoder.TryDecodeDecoderInstruction(full.AsSpan(1), out instruction);

        Assert.Equal(QpackDecodeStatus.Success, status);
        Assert.NotNull(instruction);
        Assert.Equal(DecoderInstructionType.SectionAcknowledgment, instruction.Type);
        Assert.Equal(200, instruction.IntValue);
    }

    [Fact(DisplayName = "RFC9204-4.3-ID-011: Empty input returns NeedMoreData")]
    public void Should_ReturnNeedMoreData_WhenEmpty()
    {
        var status = _decoder.TryDecodeEncoderInstruction(ReadOnlySpan<byte>.Empty, out var instruction);

        Assert.Equal(QpackDecodeStatus.NeedMoreData, status);
        Assert.Null(instruction);
        Assert.False(_decoder.HasRemainder);
    }

    // ── Batch Decoding ───────────────────────────────────────────────

    [Fact(DisplayName = "RFC9204-4.3-ID-012: DecodeAll parses multiple encoder instructions")]
    public void Should_DecodeMultipleEncoderInstructions()
    {
        QpackEncoderInstructionWriter.WriteSetDynamicTableCapacity(4096, _output);
        QpackEncoderInstructionWriter.WriteInsertWithLiteralName("content-type", "text/html", _output);
        QpackEncoderInstructionWriter.WriteDuplicate(0, _output);

        var instructions = _decoder.DecodeAllEncoderInstructions(_output.WrittenSpan);

        Assert.Equal(3, instructions.Length);
        Assert.Equal(EncoderInstructionType.SetDynamicTableCapacity, instructions[0].Type);
        Assert.Equal(4096, instructions[0].IntValue);
        Assert.Equal(EncoderInstructionType.InsertWithLiteralName, instructions[1].Type);
        Assert.Equal("content-type", instructions[1].NameString);
        Assert.Equal("text/html", instructions[1].ValueString);
        Assert.Equal(EncoderInstructionType.Duplicate, instructions[2].Type);
        Assert.Equal(0, instructions[2].IntValue);
    }

    [Fact(DisplayName = "RFC9204-4.4-ID-013: DecodeAll parses multiple decoder instructions")]
    public void Should_DecodeMultipleDecoderInstructions()
    {
        QpackDecoderInstructionWriter.WriteSectionAcknowledgment(4, _output);
        QpackDecoderInstructionWriter.WriteStreamCancellation(8, _output);
        QpackDecoderInstructionWriter.WriteInsertCountIncrement(3, _output);

        var instructions = _decoder.DecodeAllDecoderInstructions(_output.WrittenSpan);

        Assert.Equal(3, instructions.Length);
        Assert.Equal(DecoderInstructionType.SectionAcknowledgment, instructions[0].Type);
        Assert.Equal(4, instructions[0].IntValue);
        Assert.Equal(DecoderInstructionType.StreamCancellation, instructions[1].Type);
        Assert.Equal(8, instructions[1].IntValue);
        Assert.Equal(DecoderInstructionType.InsertCountIncrement, instructions[2].Type);
        Assert.Equal(3, instructions[2].IntValue);
    }

    // ── Reset ────────────────────────────────────────────────────────

    [Fact(DisplayName = "RFC9204-4.3-ID-014: Reset clears remainder")]
    public void Should_ClearRemainder_WhenReset()
    {
        // Feed partial data
        _decoder.TryDecodeEncoderInstruction(new byte[] { 0xFF }, out _);
        Assert.True(_decoder.HasRemainder);

        _decoder.Reset();

        Assert.False(_decoder.HasRemainder);
    }

    // ── Roundtrip: Write → Decode ────────────────────────────────────

    [Fact(DisplayName = "RFC9204-4.3-ID-015: Encoder instruction roundtrip with empty value")]
    public void Should_RoundtripInsertWithNameReference_EmptyValue()
    {
        QpackEncoderInstructionWriter.WriteInsertWithNameReference(0, true, "", _output);

        var status = _decoder.TryDecodeEncoderInstruction(_output.WrittenSpan, out var instruction);

        Assert.Equal(QpackDecodeStatus.Success, status);
        Assert.NotNull(instruction);
        Assert.Equal(EncoderInstructionType.InsertWithNameReference, instruction.Type);
        Assert.Equal(0, instruction.NameIndex);
        Assert.True(instruction.IsStatic);
        Assert.Equal("", instruction.ValueString);
    }

    [Fact(DisplayName = "RFC9204-4.3-ID-016: Encoder instruction roundtrip with large index")]
    public void Should_RoundtripDuplicate_LargeIndex()
    {
        QpackEncoderInstructionWriter.WriteDuplicate(1000, _output);

        var status = _decoder.TryDecodeEncoderInstruction(_output.WrittenSpan, out var instruction);

        Assert.Equal(QpackDecodeStatus.Success, status);
        Assert.NotNull(instruction);
        Assert.Equal(EncoderInstructionType.Duplicate, instruction.Type);
        Assert.Equal(1000, instruction.IntValue);
    }
}
