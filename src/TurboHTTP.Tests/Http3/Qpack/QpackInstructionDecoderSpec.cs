using TurboHTTP.Protocol.Http3.Qpack;

namespace TurboHTTP.Tests.Http3.Qpack;

public sealed class QpackInstructionDecoderSpec
{
    private readonly QpackInstructionDecoder _decoder = new();
    private readonly byte[] _buffer = new byte[1024];

    private Span<byte> CreateSpan() => _buffer.AsSpan();

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.3.1")]
    [InlineData(0)]
    [InlineData(100)]
    [InlineData(4096)]
    public void Should_DecodeSetDynamicTableCapacity(int capacity)
    {
        var span = CreateSpan();
        var written = QpackEncoderInstructionWriter.WriteSetDynamicTableCapacity(capacity, ref span);

        var status = _decoder.TryDecodeEncoderInstruction(_buffer.AsSpan(0, written), out var instruction);

        Assert.Equal(QpackDecodeStatus.Success, status);
        Assert.NotNull(instruction);
        Assert.Equal(EncoderInstructionType.SetDynamicTableCapacity, instruction.Type);
        Assert.Equal(capacity, instruction.IntValue);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.3.2")]
    public void Should_DecodeInsertWithNameReference_Static()
    {
        var span = CreateSpan();
        var written = QpackEncoderInstructionWriter.WriteInsertWithNameReference(15, true, "example.com", ref span);

        var status = _decoder.TryDecodeEncoderInstruction(_buffer.AsSpan(0, written), out var instruction);

        Assert.Equal(QpackDecodeStatus.Success, status);
        Assert.NotNull(instruction);
        Assert.Equal(EncoderInstructionType.InsertWithNameReference, instruction.Type);
        Assert.Equal(15, instruction.NameIndex);
        Assert.True(instruction.IsStatic);
        Assert.Equal("example.com", instruction.ValueString);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.3.2")]
    public void Should_DecodeInsertWithNameReference_Dynamic()
    {
        var span = CreateSpan();
        var written = QpackEncoderInstructionWriter.WriteInsertWithNameReference(3, false, "bar", ref span);

        var status = _decoder.TryDecodeEncoderInstruction(_buffer.AsSpan(0, written), out var instruction);

        Assert.Equal(QpackDecodeStatus.Success, status);
        Assert.NotNull(instruction);
        Assert.Equal(EncoderInstructionType.InsertWithNameReference, instruction.Type);
        Assert.Equal(3, instruction.NameIndex);
        Assert.False(instruction.IsStatic);
        Assert.Equal("bar", instruction.ValueString);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.3.3")]
    public void Should_DecodeInsertWithLiteralName()
    {
        var span = CreateSpan();
        var written = QpackEncoderInstructionWriter.WriteInsertWithLiteralName("x-custom", "hello", ref span);

        var status = _decoder.TryDecodeEncoderInstruction(_buffer.AsSpan(0, written), out var instruction);

        Assert.Equal(QpackDecodeStatus.Success, status);
        Assert.NotNull(instruction);
        Assert.Equal(EncoderInstructionType.InsertWithLiteralName, instruction.Type);
        Assert.Equal("x-custom", instruction.NameString);
        Assert.Equal("hello", instruction.ValueString);
    }

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.3.4")]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(100)]
    public void Should_DecodeDuplicate(int index)
    {
        var span = CreateSpan();
        var written = QpackEncoderInstructionWriter.WriteDuplicate(index, ref span);

        var status = _decoder.TryDecodeEncoderInstruction(_buffer.AsSpan(0, written), out var instruction);

        Assert.Equal(QpackDecodeStatus.Success, status);
        Assert.NotNull(instruction);
        Assert.Equal(EncoderInstructionType.Duplicate, instruction.Type);
        Assert.Equal(index, instruction.IntValue);
    }

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.4.1")]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(200)]
    public void Should_DecodeSectionAcknowledgment(int streamId)
    {
        var span = CreateSpan();
        var written = QpackDecoderInstructionWriter.WriteSectionAcknowledgment(streamId, ref span);

        var status = _decoder.TryDecodeDecoderInstruction(_buffer.AsSpan(0, written), out var instruction);

        Assert.Equal(QpackDecodeStatus.Success, status);
        Assert.NotNull(instruction);
        Assert.Equal(DecoderInstructionType.SectionAcknowledgment, instruction.Type);
        Assert.Equal(streamId, instruction.IntValue);
    }

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.4.2")]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(63)]
    public void Should_DecodeStreamCancellation(int streamId)
    {
        var span = CreateSpan();
        var written = QpackDecoderInstructionWriter.WriteStreamCancellation(streamId, ref span);

        var status = _decoder.TryDecodeDecoderInstruction(_buffer.AsSpan(0, written), out var instruction);

        Assert.Equal(QpackDecodeStatus.Success, status);
        Assert.NotNull(instruction);
        Assert.Equal(DecoderInstructionType.StreamCancellation, instruction.Type);
        Assert.Equal(streamId, instruction.IntValue);
    }

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.4.3")]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(100)]
    public void Should_DecodeInsertCountIncrement(int increment)
    {
        var span = CreateSpan();
        var written = QpackDecoderInstructionWriter.WriteInsertCountIncrement(increment, ref span);

        var status = _decoder.TryDecodeDecoderInstruction(_buffer.AsSpan(0, written), out var instruction);

        Assert.Equal(QpackDecodeStatus.Success, status);
        Assert.NotNull(instruction);
        Assert.Equal(DecoderInstructionType.InsertCountIncrement, instruction.Type);
        Assert.Equal(increment, instruction.IntValue);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.3")]
    public void Should_ReturnNeedMoreData_WhenEncoderInstructionTruncated()
    {
        // Write a full Insert With Literal Name instruction
        var span = CreateSpan();
        var written = QpackEncoderInstructionWriter.WriteInsertWithLiteralName("x-test", "value", ref span);
        var full = _buffer[..written].ToArray();

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

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.4")]
    public void Should_ReturnNeedMoreData_WhenDecoderInstructionTruncated()
    {
        // Multi-byte integer: stream ID 200 → 0xFF 0x49
        var span = CreateSpan();
        var written = QpackDecoderInstructionWriter.WriteSectionAcknowledgment(200, ref span);
        var full = _buffer[..written].ToArray();
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

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.3")]
    public void Should_ReturnNeedMoreData_WhenEmpty()
    {
        var status = _decoder.TryDecodeEncoderInstruction(ReadOnlySpan<byte>.Empty, out var instruction);

        Assert.Equal(QpackDecodeStatus.NeedMoreData, status);
        Assert.Null(instruction);
        Assert.False(_decoder.HasRemainder);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.3")]
    public void Should_DecodeMultipleEncoderInstructions()
    {
        var span = CreateSpan();
        var total = 0;
        total += QpackEncoderInstructionWriter.WriteSetDynamicTableCapacity(4096, ref span);
        total += QpackEncoderInstructionWriter.WriteInsertWithLiteralName("content-type", "text/html", ref span);
        total += QpackEncoderInstructionWriter.WriteDuplicate(0, ref span);

        var instructions = _decoder.DecodeAllEncoderInstructions(_buffer.AsSpan(0, total));

        Assert.Equal(3, instructions.Length);
        Assert.Equal(EncoderInstructionType.SetDynamicTableCapacity, instructions[0].Type);
        Assert.Equal(4096, instructions[0].IntValue);
        Assert.Equal(EncoderInstructionType.InsertWithLiteralName, instructions[1].Type);
        Assert.Equal("content-type", instructions[1].NameString);
        Assert.Equal("text/html", instructions[1].ValueString);
        Assert.Equal(EncoderInstructionType.Duplicate, instructions[2].Type);
        Assert.Equal(0, instructions[2].IntValue);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.4")]
    public void Should_DecodeMultipleDecoderInstructions()
    {
        var span = CreateSpan();
        var total = 0;
        total += QpackDecoderInstructionWriter.WriteSectionAcknowledgment(4, ref span);
        total += QpackDecoderInstructionWriter.WriteStreamCancellation(8, ref span);
        total += QpackDecoderInstructionWriter.WriteInsertCountIncrement(3, ref span);

        var instructions = _decoder.DecodeAllDecoderInstructions(_buffer.AsSpan(0, total));

        Assert.Equal(3, instructions.Length);
        Assert.Equal(DecoderInstructionType.SectionAcknowledgment, instructions[0].Type);
        Assert.Equal(4, instructions[0].IntValue);
        Assert.Equal(DecoderInstructionType.StreamCancellation, instructions[1].Type);
        Assert.Equal(8, instructions[1].IntValue);
        Assert.Equal(DecoderInstructionType.InsertCountIncrement, instructions[2].Type);
        Assert.Equal(3, instructions[2].IntValue);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.3")]
    public void Should_ClearRemainder_WhenReset()
    {
        // Feed partial data
        _decoder.TryDecodeEncoderInstruction([0xFF], out _);
        Assert.True(_decoder.HasRemainder);

        _decoder.Reset();

        Assert.False(_decoder.HasRemainder);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.3")]
    public void Should_RoundtripInsertWithNameReference_EmptyValue()
    {
        var span = CreateSpan();
        var written = QpackEncoderInstructionWriter.WriteInsertWithNameReference(0, true, "", ref span);

        var status = _decoder.TryDecodeEncoderInstruction(_buffer.AsSpan(0, written), out var instruction);

        Assert.Equal(QpackDecodeStatus.Success, status);
        Assert.NotNull(instruction);
        Assert.Equal(EncoderInstructionType.InsertWithNameReference, instruction.Type);
        Assert.Equal(0, instruction.NameIndex);
        Assert.True(instruction.IsStatic);
        Assert.Equal("", instruction.ValueString);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.3")]
    public void Should_RoundtripDuplicate_LargeIndex()
    {
        var span = CreateSpan();
        var written = QpackEncoderInstructionWriter.WriteDuplicate(1000, ref span);

        var status = _decoder.TryDecodeEncoderInstruction(_buffer.AsSpan(0, written), out var instruction);

        Assert.Equal(QpackDecodeStatus.Success, status);
        Assert.NotNull(instruction);
        Assert.Equal(EncoderInstructionType.Duplicate, instruction.Type);
        Assert.Equal(1000, instruction.IntValue);
    }
}