using System;
using System.Buffers;
using TurboHttp.Protocol.RFC9204;

namespace TurboHttp.Tests.RFC9204;

/// <summary>
/// Tests for QPACK decoder instruction writer per RFC 9204 §4.4.
/// Covers Section Acknowledgment, Stream Cancellation, and Insert Count Increment.
/// </summary>
public sealed class QpackDecoderInstructionTests
{
    private readonly ArrayBufferWriter<byte> _output = new();

    // ── Section Acknowledgment (§4.4.1) ─────────────────────────────

    /// RFC 9204 §4.4.1 — Section Acknowledgment encodes stream ID with 7-bit prefix
    [Theory(DisplayName = "RFC9204-4.4.1-DI-001: Section Acknowledgment encodes correctly")]
    [InlineData(0, new byte[] { 0x80 })]               // 1_0000000 → streamId=0
    [InlineData(1, new byte[] { 0x81 })]               // 1_0000001 → streamId=1
    [InlineData(126, new byte[] { 0xFE })]             // 1_1111110 → streamId=126
    [InlineData(127, new byte[] { 0xFF, 0x00 })]       // 1_1111111 + 0x00 → streamId=127
    [InlineData(200, new byte[] { 0xFF, 0x49 })]       // 1_1111111 + 73
    public void Should_EncodeSectionAcknowledgment(int streamId, byte[] expected)
    {
        QpackDecoderInstructionWriter.WriteSectionAcknowledgment(streamId, _output);

        Assert.Equal(expected, _output.WrittenSpan.ToArray());
    }

    /// RFC 9204 §4.4.1 — High bit is always set for Section Acknowledgment
    [Fact(DisplayName = "RFC9204-4.4.1-DI-002: Section Acknowledgment sets high bit")]
    public void Should_SetHighBitForSectionAcknowledgment()
    {
        QpackDecoderInstructionWriter.WriteSectionAcknowledgment(0, _output);

        Assert.Equal(0x80, _output.WrittenSpan[0] & 0x80);
    }

    /// RFC 9204 §4.4.1 — Negative stream ID rejected
    [Fact(DisplayName = "RFC9204-4.4.1-DI-003: Section Acknowledgment rejects negative stream ID")]
    public void Should_ThrowForNegativeStreamId_Acknowledgment()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            QpackDecoderInstructionWriter.WriteSectionAcknowledgment(-1, _output));
    }

    // ── Stream Cancellation (§4.4.2) ────────────────────────────────

    /// RFC 9204 §4.4.2 — Stream Cancellation encodes stream ID with 6-bit prefix
    [Theory(DisplayName = "RFC9204-4.4.2-DI-004: Stream Cancellation encodes correctly")]
    [InlineData(0, new byte[] { 0x40 })]               // 01_000000 → streamId=0
    [InlineData(1, new byte[] { 0x41 })]               // 01_000001 → streamId=1
    [InlineData(62, new byte[] { 0x7E })]              // 01_111110 → streamId=62
    [InlineData(63, new byte[] { 0x7F, 0x00 })]       // 01_111111 + 0x00 → streamId=63
    [InlineData(200, new byte[] { 0x7F, 0x89, 0x01 })] // 01_111111 + multi-byte
    public void Should_EncodeStreamCancellation(int streamId, byte[] expected)
    {
        QpackDecoderInstructionWriter.WriteStreamCancellation(streamId, _output);

        Assert.Equal(expected, _output.WrittenSpan.ToArray());
    }

    /// RFC 9204 §4.4.2 — Stream Cancellation has 01 prefix pattern
    [Fact(DisplayName = "RFC9204-4.4.2-DI-005: Stream Cancellation has correct prefix")]
    public void Should_HaveCorrectPrefixForStreamCancellation()
    {
        QpackDecoderInstructionWriter.WriteStreamCancellation(0, _output);

        Assert.Equal(0x40, _output.WrittenSpan[0] & 0xC0);
    }

    /// RFC 9204 §4.4.2 — Negative stream ID rejected
    [Fact(DisplayName = "RFC9204-4.4.2-DI-006: Stream Cancellation rejects negative stream ID")]
    public void Should_ThrowForNegativeStreamId_Cancellation()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            QpackDecoderInstructionWriter.WriteStreamCancellation(-1, _output));
    }

    // ── Insert Count Increment (§4.4.3) ─────────────────────────────

    /// RFC 9204 §4.4.3 — Insert Count Increment encodes with 6-bit prefix
    [Theory(DisplayName = "RFC9204-4.4.3-DI-007: Insert Count Increment encodes correctly")]
    [InlineData(1, new byte[] { 0x01 })]               // 00_000001 → increment=1
    [InlineData(10, new byte[] { 0x0A })]              // 00_001010 → increment=10
    [InlineData(62, new byte[] { 0x3E })]              // 00_111110 → increment=62
    [InlineData(63, new byte[] { 0x3F, 0x00 })]       // 00_111111 + 0x00 → increment=63
    [InlineData(100, new byte[] { 0x3F, 0x25 })]      // 00_111111 + 37
    public void Should_EncodeInsertCountIncrement(int increment, byte[] expected)
    {
        QpackDecoderInstructionWriter.WriteInsertCountIncrement(increment, _output);

        Assert.Equal(expected, _output.WrittenSpan.ToArray());
    }

    /// RFC 9204 §4.4.3 — Insert Count Increment has 00 prefix pattern
    [Fact(DisplayName = "RFC9204-4.4.3-DI-008: Insert Count Increment has correct prefix")]
    public void Should_HaveCorrectPrefixForInsertCountIncrement()
    {
        QpackDecoderInstructionWriter.WriteInsertCountIncrement(1, _output);

        Assert.Equal(0x00, _output.WrittenSpan[0] & 0xC0);
    }

    /// RFC 9204 §4.4.3 — Zero increment rejected (must be positive)
    [Fact(DisplayName = "RFC9204-4.4.3-DI-009: Insert Count Increment rejects zero")]
    public void Should_ThrowForZeroIncrement()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            QpackDecoderInstructionWriter.WriteInsertCountIncrement(0, _output));
    }

    /// RFC 9204 §4.4.3 — Negative increment rejected
    [Fact(DisplayName = "RFC9204-4.4.3-DI-010: Insert Count Increment rejects negative")]
    public void Should_ThrowForNegativeIncrement()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            QpackDecoderInstructionWriter.WriteInsertCountIncrement(-1, _output));
    }

    // ── Disambiguation (§4.4) ───────────────────────────────────────

    /// RFC 9204 §4.4 — All three instruction types are distinguishable by first two bits
    [Fact(DisplayName = "RFC9204-4.4-DI-011: All instructions have distinct prefix patterns")]
    public void Should_HaveDistinctPrefixes()
    {
        var ack = new ArrayBufferWriter<byte>();
        var cancel = new ArrayBufferWriter<byte>();
        var increment = new ArrayBufferWriter<byte>();

        QpackDecoderInstructionWriter.WriteSectionAcknowledgment(1, ack);
        QpackDecoderInstructionWriter.WriteStreamCancellation(1, cancel);
        QpackDecoderInstructionWriter.WriteInsertCountIncrement(1, increment);

        // Section Acknowledgment: 1xxxxxxx
        Assert.Equal(0x80, ack.WrittenSpan[0] & 0x80);
        // Stream Cancellation: 01xxxxxx
        Assert.Equal(0x40, cancel.WrittenSpan[0] & 0xC0);
        // Insert Count Increment: 00xxxxxx
        Assert.Equal(0x00, increment.WrittenSpan[0] & 0xC0);
    }
}
