using System;
using System.Buffers;
using System.Text;
using TurboHttp.Protocol.RFC9204;

namespace TurboHttp.Tests.RFC9204;

/// <summary>
/// Tests for QPACK encoder instruction writer per RFC 9204 §4.3.
/// Covers Set Dynamic Table Capacity, Insert With Name Reference,
/// Insert With Literal Name, and Duplicate instructions.
/// </summary>
public sealed class QpackEncoderInstructionTests
{
    private readonly ArrayBufferWriter<byte> _output = new();

    // ── Set Dynamic Table Capacity (§4.3.1) ───────────────────────────

    /// RFC 9204 §4.3.1 — Set Dynamic Table Capacity with small value fits in one byte
    [Theory(DisplayName = "RFC9204-4.3.1-EI-001: Set Dynamic Table Capacity encodes correctly")]
    [InlineData(0, new byte[] { 0x20 })]           // 001_00000 → capacity=0
    [InlineData(30, new byte[] { 0x3E })]           // 001_11110 → capacity=30
    [InlineData(31, new byte[] { 0x3F, 0x00 })]    // 001_11111 + 0x00 → capacity=31
    [InlineData(4096, new byte[] { 0x3F, 0xE1, 0x1F })] // 001_11111 + multi-byte
    public void Should_EncodeSetDynamicTableCapacity(int capacity, byte[] expected)
    {
        QpackEncoderInstructionWriter.WriteSetDynamicTableCapacity(capacity, _output);

        Assert.Equal(expected, _output.WrittenSpan.ToArray());
    }

    /// RFC 9204 §4.3.1 — Negative capacity rejected
    [Fact(DisplayName = "RFC9204-4.3.1-EI-002: Set Dynamic Table Capacity rejects negative")]
    public void Should_ThrowForNegativeCapacity()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            QpackEncoderInstructionWriter.WriteSetDynamicTableCapacity(-1, _output));
    }

    // ── Insert With Name Reference (§4.3.2) ──────────────────────────

    /// RFC 9204 §4.3.2 — Insert with static table name reference
    [Fact(DisplayName = "RFC9204-4.3.2-EI-003: Insert With Name Reference (static) encodes correctly")]
    public void Should_EncodeInsertWithNameReference_Static()
    {
        // Static table index 1 = :path, value = "/index.html"
        // First byte: 1(T=1)xxxxxx → 0xC0 | 1 = 0xC1
        QpackEncoderInstructionWriter.WriteInsertWithNameReference(1, true, "/index.html", _output);

        var data = _output.WrittenSpan;
        Assert.True(data.Length >= 2);

        // First byte: 1 1 000001 = 0xC1 (static ref, index 1)
        Assert.Equal(0xC1, data[0]);

        // Decode the value string to verify round-trip
        var pos = 1;
        var valueBytes = QpackStringCodec.Decode(data.ToArray(), ref pos, 7);
        Assert.Equal("/index.html", Encoding.UTF8.GetString(valueBytes));
        Assert.Equal(data.Length, pos);
    }

    /// RFC 9204 §4.3.2 — Insert with dynamic table name reference
    [Fact(DisplayName = "RFC9204-4.3.2-EI-004: Insert With Name Reference (dynamic) encodes correctly")]
    public void Should_EncodeInsertWithNameReference_Dynamic()
    {
        // Dynamic table index 0, value = "bar"
        // First byte: 1(T=0)xxxxxx → 0x80 | 0 = 0x80
        QpackEncoderInstructionWriter.WriteInsertWithNameReference(0, false, "bar", _output);

        var data = _output.WrittenSpan;
        Assert.True(data.Length >= 2);

        // First byte: 1 0 000000 = 0x80 (dynamic ref, index 0)
        Assert.Equal(0x80, data[0]);

        var pos = 1;
        var valueBytes = QpackStringCodec.Decode(data.ToArray(), ref pos, 7);
        Assert.Equal("bar", Encoding.UTF8.GetString(valueBytes));
    }

    /// RFC 9204 §4.3.2 — Negative name index rejected
    [Fact(DisplayName = "RFC9204-4.3.2-EI-005: Insert With Name Reference rejects negative index")]
    public void Should_ThrowForNegativeNameIndex()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            QpackEncoderInstructionWriter.WriteInsertWithNameReference(-1, true, "val"u8, _output));
    }

    // ── Insert With Literal Name (§4.3.3) ────────────────────────────

    /// RFC 9204 §4.3.3 — Insert with literal name and value
    [Fact(DisplayName = "RFC9204-4.3.3-EI-006: Insert With Literal Name encodes correctly")]
    public void Should_EncodeInsertWithLiteralName()
    {
        QpackEncoderInstructionWriter.WriteInsertWithLiteralName("x-custom", "hello", _output);

        var data = _output.WrittenSpan;
        Assert.True(data.Length >= 4);

        // First byte high bits: 01 → 0x40 mask
        Assert.Equal(0x40, data[0] & 0xC0);

        // Decode name
        var pos = 0;
        var nameBytes = QpackStringCodec.Decode(data.ToArray(), ref pos, 5);
        Assert.Equal("x-custom", Encoding.UTF8.GetString(nameBytes));

        // Decode value
        var valueBytes = QpackStringCodec.Decode(data.ToArray(), ref pos, 7);
        Assert.Equal("hello", Encoding.UTF8.GetString(valueBytes));
        Assert.Equal(data.Length, pos);
    }

    /// RFC 9204 §4.3.3 — Insert with empty value
    [Fact(DisplayName = "RFC9204-4.3.3-EI-007: Insert With Literal Name handles empty value")]
    public void Should_EncodeInsertWithLiteralName_EmptyValue()
    {
        QpackEncoderInstructionWriter.WriteInsertWithLiteralName("x-empty", "", _output);

        var data = _output.WrittenSpan;

        // Decode name
        var pos = 0;
        var nameBytes = QpackStringCodec.Decode(data.ToArray(), ref pos, 5);
        Assert.Equal("x-empty", Encoding.UTF8.GetString(nameBytes));

        // Decode value — should be empty
        var valueBytes = QpackStringCodec.Decode(data.ToArray(), ref pos, 7);
        Assert.Empty(valueBytes);
    }

    // ── Duplicate (§4.3.4) ───────────────────────────────────────────

    /// RFC 9204 §4.3.4 — Duplicate instruction
    [Theory(DisplayName = "RFC9204-4.3.4-EI-008: Duplicate encodes correctly")]
    [InlineData(0, new byte[] { 0x00 })]           // 000_00000 → index=0
    [InlineData(7, new byte[] { 0x07 })]            // 000_00111 → index=7
    [InlineData(31, new byte[] { 0x1F, 0x00 })]    // 000_11111 + 0x00 → index=31
    [InlineData(100, new byte[] { 0x1F, 0x45 })]   // 000_11111 + 69
    public void Should_EncodeDuplicate(int index, byte[] expected)
    {
        QpackEncoderInstructionWriter.WriteDuplicate(index, _output);

        Assert.Equal(expected, _output.WrittenSpan.ToArray());
    }

    /// RFC 9204 §4.3.4 — Negative duplicate index rejected
    [Fact(DisplayName = "RFC9204-4.3.4-EI-009: Duplicate rejects negative index")]
    public void Should_ThrowForNegativeDuplicateIndex()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            QpackEncoderInstructionWriter.WriteDuplicate(-1, _output));
    }
}
