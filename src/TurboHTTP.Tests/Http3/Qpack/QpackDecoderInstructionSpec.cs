using TurboHTTP.Protocol.Http3.Qpack;

namespace TurboHTTP.Tests.Http3.Qpack;

/// <summary>
/// Tests for QPACK decoder instruction writer per RFC 9204 §4.4.
/// Covers Section Acknowledgment, Stream Cancellation, and Insert Count Increment.
/// </summary>
public sealed class QpackDecoderInstructionSpec
{
    /// RFC 9204 §4.4.1 — Section Acknowledgment encodes stream ID with 7-bit prefix
    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.4.1")]
    [InlineData(0, new byte[] { 0x80 })]               // 1_0000000 → streamId=0
    [InlineData(1, new byte[] { 0x81 })]               // 1_0000001 → streamId=1
    [InlineData(126, new byte[] { 0xFE })]             // 1_1111110 → streamId=126
    [InlineData(127, new byte[] { 0xFF, 0x00 })]       // 1_1111111 + 0x00 → streamId=127
    [InlineData(200, new byte[] { 0xFF, 0x49 })]       // 1_1111111 + 73
    public void Should_EncodeSectionAcknowledgment(int streamId, byte[] expected)
    {
        Span<byte> output = new byte[16];
        var span = output;
        var n = QpackDecoderInstructionWriter.WriteSectionAcknowledgment(streamId, ref span);

        Assert.Equal(expected, output[..n].ToArray());
    }

    /// RFC 9204 §4.4.1 — High bit is always set for Section Acknowledgment
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.4.1")]
    public void Should_SetHighBitForSectionAcknowledgment()
    {
        Span<byte> output = new byte[16];
        var span = output;
        QpackDecoderInstructionWriter.WriteSectionAcknowledgment(0, ref span);

        Assert.Equal(0x80, output[0] & 0x80);
    }

    /// RFC 9204 §4.4.1 — Negative stream ID rejected
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.4.1")]
    public void Should_ThrowForNegativeStreamId_Acknowledgment()
    {
        Assert.Throws<ArgumentOutOfRangeException>(ThrowHelper_NegativeAck);

        static void ThrowHelper_NegativeAck()
        {
            Span<byte> output = new byte[16];
            QpackDecoderInstructionWriter.WriteSectionAcknowledgment(-1, ref output);
        }
    }


    /// RFC 9204 §4.4.2 — Stream Cancellation encodes stream ID with 6-bit prefix
    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.4.2")]
    [InlineData(0, new byte[] { 0x40 })]               // 01_000000 → streamId=0
    [InlineData(1, new byte[] { 0x41 })]               // 01_000001 → streamId=1
    [InlineData(62, new byte[] { 0x7E })]              // 01_111110 → streamId=62
    [InlineData(63, new byte[] { 0x7F, 0x00 })]       // 01_111111 + 0x00 → streamId=63
    [InlineData(200, new byte[] { 0x7F, 0x89, 0x01 })] // 01_111111 + multi-byte
    public void Should_EncodeStreamCancellation(int streamId, byte[] expected)
    {
        Span<byte> output = new byte[16];
        var span = output;
        var n = QpackDecoderInstructionWriter.WriteStreamCancellation(streamId, ref span);

        Assert.Equal(expected, output[..n].ToArray());
    }

    /// RFC 9204 §4.4.2 — Stream Cancellation has 01 prefix pattern
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.4.2")]
    public void Should_HaveCorrectPrefixForStreamCancellation()
    {
        Span<byte> output = new byte[16];
        var span = output;
        QpackDecoderInstructionWriter.WriteStreamCancellation(0, ref span);

        Assert.Equal(0x40, output[0] & 0xC0);
    }

    /// RFC 9204 §4.4.2 — Negative stream ID rejected
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.4.2")]
    public void Should_ThrowForNegativeStreamId_Cancellation()
    {
        Assert.Throws<ArgumentOutOfRangeException>(ThrowHelper_NegativeCancel);

        static void ThrowHelper_NegativeCancel()
        {
            Span<byte> output = new byte[16];
            QpackDecoderInstructionWriter.WriteStreamCancellation(-1, ref output);
        }
    }


    /// RFC 9204 §4.4.3 — Insert Count Increment encodes with 6-bit prefix
    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.4.3")]
    [InlineData(1, new byte[] { 0x01 })]               // 00_000001 → increment=1
    [InlineData(10, new byte[] { 0x0A })]              // 00_001010 → increment=10
    [InlineData(62, new byte[] { 0x3E })]              // 00_111110 → increment=62
    [InlineData(63, new byte[] { 0x3F, 0x00 })]       // 00_111111 + 0x00 → increment=63
    [InlineData(100, new byte[] { 0x3F, 0x25 })]      // 00_111111 + 37
    public void Should_EncodeInsertCountIncrement(int increment, byte[] expected)
    {
        Span<byte> output = new byte[16];
        var span = output;
        var n = QpackDecoderInstructionWriter.WriteInsertCountIncrement(increment, ref span);

        Assert.Equal(expected, output[..n].ToArray());
    }

    /// RFC 9204 §4.4.3 — Insert Count Increment has 00 prefix pattern
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.4.3")]
    public void Should_HaveCorrectPrefixForInsertCountIncrement()
    {
        Span<byte> output = new byte[16];
        var span = output;
        QpackDecoderInstructionWriter.WriteInsertCountIncrement(1, ref span);

        Assert.Equal(0x00, output[0] & 0xC0);
    }

    /// RFC 9204 §4.4.3 — Zero increment rejected (must be positive)
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.4.3")]
    public void Should_ThrowForZeroIncrement()
    {
        Assert.Throws<ArgumentOutOfRangeException>(ThrowHelper_ZeroIncrement);

        static void ThrowHelper_ZeroIncrement()
        {
            Span<byte> output = new byte[16];
            QpackDecoderInstructionWriter.WriteInsertCountIncrement(0, ref output);
        }
    }

    /// RFC 9204 §4.4.3 — Negative increment rejected
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.4.3")]
    public void Should_ThrowForNegativeIncrement()
    {
        Assert.Throws<ArgumentOutOfRangeException>(ThrowHelper_NegativeIncrement);

        static void ThrowHelper_NegativeIncrement()
        {
            Span<byte> output = new byte[16];
            QpackDecoderInstructionWriter.WriteInsertCountIncrement(-1, ref output);
        }
    }


    /// RFC 9204 §4.4 — All three instruction types are distinguishable by first two bits
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.4")]
    public void Should_HaveDistinctPrefixes()
    {
        Span<byte> ackBuf = new byte[16];
        Span<byte> cancelBuf = new byte[16];
        Span<byte> incrementBuf = new byte[16];

        var ack = ackBuf;
        var cancel = cancelBuf;
        var increment = incrementBuf;

        QpackDecoderInstructionWriter.WriteSectionAcknowledgment(1, ref ack);
        QpackDecoderInstructionWriter.WriteStreamCancellation(1, ref cancel);
        QpackDecoderInstructionWriter.WriteInsertCountIncrement(1, ref increment);

        // Section Acknowledgment: 1xxxxxxx
        Assert.Equal(0x80, ackBuf[0] & 0x80);
        // Stream Cancellation: 01xxxxxx
        Assert.Equal(0x40, cancelBuf[0] & 0xC0);
        // Insert Count Increment: 00xxxxxx
        Assert.Equal(0x00, incrementBuf[0] & 0xC0);
    }
}
