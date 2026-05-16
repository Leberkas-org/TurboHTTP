using TurboHTTP.Protocol;
using TurboHTTP.Protocol.Syntax.Http3.Qpack;

namespace TurboHTTP.Tests.Protocol.Syntax.Http3.Qpack;

public sealed class QpackDecoderInstructionSpec
{
    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.4.1")]
    [InlineData(0, new byte[] { 0x80 })] // 1_0000000 → streamId=0
    [InlineData(1, new byte[] { 0x81 })] // 1_0000001 → streamId=1
    [InlineData(126, new byte[] { 0xFE })] // 1_1111110 → streamId=126
    [InlineData(127, new byte[] { 0xFF, 0x00 })] // 1_1111111 + 0x00 → streamId=127
    [InlineData(200, new byte[] { 0xFF, 0x49 })] // 1_1111111 + 73
    public void Should_EncodeSectionAcknowledgment(int streamId, byte[] expected)
    {
        var output = new byte[16];
        var writer = SpanWriter.Create(output);
        var n = QpackDecoderInstructionWriter.WriteSectionAcknowledgment(streamId, ref writer);

        Assert.Equal(expected, output[..n].ToArray());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.4.1")]
    public void Should_SetHighBitForSectionAcknowledgment()
    {
        var output = new byte[16];
        var writer = SpanWriter.Create(output);
        QpackDecoderInstructionWriter.WriteSectionAcknowledgment(0, ref writer);

        Assert.Equal(0x80, output[0] & 0x80);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.4.1")]
    public void Should_ThrowForNegativeStreamId_Acknowledgment()
    {
        Assert.Throws<ArgumentOutOfRangeException>(ThrowHelperNegativeAck);
        return;

        static void ThrowHelperNegativeAck()
        {
            var output = new byte[16];
            var writer = SpanWriter.Create(output);
            QpackDecoderInstructionWriter.WriteSectionAcknowledgment(-1, ref writer);
        }
    }

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.4.2")]
    [InlineData(0, new byte[] { 0x40 })] // 01_000000 → streamId=0
    [InlineData(1, new byte[] { 0x41 })] // 01_000001 → streamId=1
    [InlineData(62, new byte[] { 0x7E })] // 01_111110 → streamId=62
    [InlineData(63, new byte[] { 0x7F, 0x00 })] // 01_111111 + 0x00 → streamId=63
    [InlineData(200, new byte[] { 0x7F, 0x89, 0x01 })] // 01_111111 + multi-byte
    public void Should_EncodeStreamCancellation(int streamId, byte[] expected)
    {
        var output = new byte[16];
        var writer = SpanWriter.Create(output);
        var n = QpackDecoderInstructionWriter.WriteStreamCancellation(streamId, ref writer);

        Assert.Equal(expected, output[..n].ToArray());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.4.2")]
    public void Should_HaveCorrectPrefixForStreamCancellation()
    {
        var output = new byte[16];
        var writer = SpanWriter.Create(output);
        QpackDecoderInstructionWriter.WriteStreamCancellation(0, ref writer);

        Assert.Equal(0x40, output[0] & 0xC0);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.4.2")]
    public void Should_ThrowForNegativeStreamId_Cancellation()
    {
        Assert.Throws<ArgumentOutOfRangeException>(ThrowHelper_NegativeCancel);
        return;

        static void ThrowHelper_NegativeCancel()
        {
            var output = new byte[16];
            var writer = SpanWriter.Create(output);
            QpackDecoderInstructionWriter.WriteStreamCancellation(-1, ref writer);
        }
    }


    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.4.3")]
    [InlineData(1, new byte[] { 0x01 })] // 00_000001 → increment=1
    [InlineData(10, new byte[] { 0x0A })] // 00_001010 → increment=10
    [InlineData(62, new byte[] { 0x3E })] // 00_111110 → increment=62
    [InlineData(63, new byte[] { 0x3F, 0x00 })] // 00_111111 + 0x00 → increment=63
    [InlineData(100, new byte[] { 0x3F, 0x25 })] // 00_111111 + 37
    public void Should_EncodeInsertCountIncrement(int increment, byte[] expected)
    {
        var output = new byte[16];
        var writer = SpanWriter.Create(output);
        var n = QpackDecoderInstructionWriter.WriteInsertCountIncrement(increment, ref writer);

        Assert.Equal(expected, output[..n].ToArray());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.4.3")]
    public void Should_HaveCorrectPrefixForInsertCountIncrement()
    {
        var output = new byte[16];
        var writer = SpanWriter.Create(output);
        QpackDecoderInstructionWriter.WriteInsertCountIncrement(1, ref writer);

        Assert.Equal(0x00, output[0] & 0xC0);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.4.3")]
    public void Should_ThrowForZeroIncrement()
    {
        Assert.Throws<ArgumentOutOfRangeException>(ThrowHelper_ZeroIncrement);

        static void ThrowHelper_ZeroIncrement()
        {
            var output = new byte[16];
            var writer = SpanWriter.Create(output);
            QpackDecoderInstructionWriter.WriteInsertCountIncrement(0, ref writer);
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.4.3")]
    public void Should_ThrowForNegativeIncrement()
    {
        Assert.Throws<ArgumentOutOfRangeException>(ThrowHelperNegativeIncrement);

        static void ThrowHelperNegativeIncrement()
        {
            var output = new byte[16];
            var writer = SpanWriter.Create(output);
            QpackDecoderInstructionWriter.WriteInsertCountIncrement(-1, ref writer);
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.4")]
    public void Should_HaveDistinctPrefixes()
    {
        var ackBuf = new byte[16];
        var cancelBuf = new byte[16];
        var incrementBuf = new byte[16];

        var ack = SpanWriter.Create(ackBuf);
        var cancel = SpanWriter.Create(cancelBuf);
        var increment = SpanWriter.Create(incrementBuf);

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