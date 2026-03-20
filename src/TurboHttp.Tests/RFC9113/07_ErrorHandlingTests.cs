using System.Buffers.Binary;
using TurboHttp.Protocol.RFC9113;

namespace TurboHttp.Tests.RFC9113;

/// <summary>
/// Tests RST_STREAM and PING frame decoding per RFC 9113 §6.3 and §6.4.
/// Verifies correct stream ID, error code, and flag extraction for both frame types.
/// </summary>
/// <remarks>
/// Class under test: <see cref="Http2FrameDecoder"/>.
/// RFC 9113 §6.4: RST_STREAM carries a 4-byte error code and abruptly terminates a stream.
/// RFC 9113 §6.3: PING frames carry 8 bytes of opaque data and may carry the ACK flag.
/// </remarks>
public sealed class Http2RstStreamPingTests
{
    // RST-001..RST-007: RST_STREAM Frame — §6.4

    /// RFC 9113 §6.4 — RST_STREAM decoded with correct StreamId
    [Fact(DisplayName = "RFC9113-6.4-RST-001: RST_STREAM decoded with correct StreamId")]
    public void Should_DecodeWithCorrectStreamId_When_RstStreamFrameReceived()
    {
        var bytes = new RstStreamFrame(5, Http2ErrorCode.Cancel).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Single(frames);
        var frame = Assert.IsType<RstStreamFrame>(frames[0]);
        Assert.Equal(5, frame.StreamId);
    }

    /// RFC 9113 §6.4 — RST_STREAM decoded with correct ErrorCode
    [Fact(DisplayName = "RFC9113-6.4-RST-002: RST_STREAM decoded with correct ErrorCode")]
    public void Should_DecodeWithCorrectErrorCode_When_RstStreamFrameReceived()
    {
        var bytes = new RstStreamFrame(3, Http2ErrorCode.Cancel).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Single(frames);
        var frame = Assert.IsType<RstStreamFrame>(frames[0]);
        Assert.Equal(Http2ErrorCode.Cancel, frame.ErrorCode);
    }

    /// RFC 9113 §6.4 — RST_STREAM has correct FrameType
    [Fact(DisplayName = "RFC9113-6.4-RST-003: RST_STREAM has correct FrameType")]
    public void Should_HaveCorrectFrameType_When_RstStreamFrameReceived()
    {
        var bytes = new RstStreamFrame(1, Http2ErrorCode.NoError).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Single(frames);
        Assert.Equal(FrameType.RstStream, frames[0].Type);
    }

    /// RFC 9113 §6.4 — RST_STREAM with wrong payload length is FRAME_SIZE_ERROR
    [Fact(DisplayName = "RFC9113-6.4-RST-004: RST_STREAM with wrong payload length is FRAME_SIZE_ERROR")]
    public void Should_BeFrameSizeError_When_RstStreamHasWrongPayloadLength()
    {
        // RST_STREAM must be exactly 4 bytes; send 3 bytes.
        var frame = new byte[9 + 3];
        frame[0] = 0; frame[1] = 0; frame[2] = 3;   // length = 3
        frame[3] = 0x3;                               // type = RST_STREAM
        frame[4] = 0x0;                               // no flags
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(5), 1u); // stream 1

        var decoder = new Http2FrameDecoder();
        var ex = Assert.Throws<Http2Exception>(() => decoder.Decode(frame));
        Assert.Equal(Http2ErrorCode.FrameSizeError, ex.ErrorCode);
        Assert.Equal(Http2ErrorScope.Connection, ex.Scope);
    }

    /// RFC 9113 §6.4 — RST_STREAM round-trip preserves StreamId and ErrorCode
    [Fact(DisplayName = "RFC9113-6.4-RST-005: RST_STREAM round-trip preserves StreamId and ErrorCode")]
    public void Should_PreserveFields_When_RstStreamRoundTrip()
    {
        var original = new RstStreamFrame(7, Http2ErrorCode.InternalError);
        var bytes = original.Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Single(frames);
        var decoded = Assert.IsType<RstStreamFrame>(frames[0]);
        Assert.Equal(original.StreamId, decoded.StreamId);
        Assert.Equal(original.ErrorCode, decoded.ErrorCode);
    }

    /// RFC 9113 §6.4 — RST_STREAM with ProtocolError decoded correctly
    [Fact(DisplayName = "RFC9113-6.4-RST-006: RST_STREAM with ProtocolError decoded correctly")]
    public void Should_DecodeCorrectly_When_RstStreamHasProtocolError()
    {
        var bytes = new RstStreamFrame(11, Http2ErrorCode.ProtocolError).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(bytes);

        var frame = Assert.IsType<RstStreamFrame>(frames[0]);
        Assert.Equal(Http2ErrorCode.ProtocolError, frame.ErrorCode);
        Assert.Equal(11, frame.StreamId);
    }

    /// RFC 9113 §6.4 — RST_STREAM various error codes decoded correctly
    [Theory(DisplayName = "RFC9113-6.4-RST-007: RST_STREAM various error codes decoded correctly")]
    [InlineData(Http2ErrorCode.NoError)]
    [InlineData(Http2ErrorCode.Cancel)]
    [InlineData(Http2ErrorCode.FlowControlError)]
    [InlineData(Http2ErrorCode.RefusedStream)]
    [InlineData(Http2ErrorCode.StreamClosed)]
    public void Should_DecodeCorrectly_When_RstStreamHasVariousErrorCodes(Http2ErrorCode errorCode)
    {
        var bytes = new RstStreamFrame(1, errorCode).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(bytes);

        var frame = Assert.IsType<RstStreamFrame>(frames[0]);
        Assert.Equal(errorCode, frame.ErrorCode);
    }

    // PNG-001..PNG-007: PING Frame — §6.7

    /// RFC 9113 §6.7 — PING decoded with correct Data
    [Fact(DisplayName = "RFC9113-6.7-PNG-001: PING decoded with correct Data")]
    public void Should_DecodeWithCorrectData_When_PingFrameReceived()
    {
        var pingData = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var bytes = new PingFrame(pingData).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Single(frames);
        var frame = Assert.IsType<PingFrame>(frames[0]);
        Assert.True(frame.Data.Span.SequenceEqual(pingData));
    }

    /// RFC 9113 §6.7 — PING IsAck=false when ACK bit not set
    [Fact(DisplayName = "RFC9113-6.7-PNG-002: PING IsAck=false when ACK bit not set")]
    public void Should_HaveIsAckFalse_When_PingIsNonAck()
    {
        var bytes = new PingFrame(new byte[8]).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(bytes);

        var frame = Assert.IsType<PingFrame>(frames[0]);
        Assert.False(frame.IsAck);
    }

    /// RFC 9113 §6.7 — PING ACK decoded with IsAck=true
    [Fact(DisplayName = "RFC9113-6.7-PNG-003: PING ACK decoded with IsAck=true")]
    public void Should_HaveIsAckTrue_When_PingIsAck()
    {
        var bytes = new PingFrame(new byte[8], isAck: true).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(bytes);

        var frame = Assert.IsType<PingFrame>(frames[0]);
        Assert.True(frame.IsAck);
    }

    /// RFC 9113 §6.7 — PING with wrong payload length is FRAME_SIZE_ERROR
    [Fact(DisplayName = "RFC9113-6.7-PNG-004: PING with wrong payload length is FRAME_SIZE_ERROR")]
    public void Should_BeFrameSizeError_When_PingHasWrongPayloadLength()
    {
        // PING must be exactly 8 bytes; send 4 bytes.
        var frame = new byte[9 + 4];
        frame[0] = 0; frame[1] = 0; frame[2] = 4;   // length = 4
        frame[3] = 0x6;                               // type = PING
        frame[4] = 0x0;                               // no flags
        // stream id = 0 (already zero)

        var decoder = new Http2FrameDecoder();
        var ex = Assert.Throws<Http2Exception>(() => decoder.Decode(frame));
        Assert.Equal(Http2ErrorCode.FrameSizeError, ex.ErrorCode);
        Assert.Equal(Http2ErrorScope.Connection, ex.Scope);
    }

    /// RFC 9113 §6.7 — PING on non-zero stream is PROTOCOL_ERROR
    [Fact(DisplayName = "RFC9113-6.7-PNG-005: PING on non-zero stream is PROTOCOL_ERROR")]
    public void Should_BeProtocolError_When_PingOnNonZeroStream()
    {
        // Craft a PING frame with stream ID = 1 (violates RFC 9113 §6.7).
        var frame = new byte[9 + 8];
        frame[0] = 0; frame[1] = 0; frame[2] = 8;   // length = 8
        frame[3] = 0x6;                               // type = PING
        frame[4] = 0x0;                               // no flags
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(5), 1u); // stream 1

        var decoder = new Http2FrameDecoder();
        var ex = Assert.Throws<Http2Exception>(() => decoder.Decode(frame));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.Equal(Http2ErrorScope.Connection, ex.Scope);
    }

    /// RFC 9113 §6.7 — PING has correct FrameType
    [Fact(DisplayName = "RFC9113-6.7-PNG-006: PING has correct FrameType")]
    public void Should_HaveCorrectFrameType_When_PingFrameReceived()
    {
        var bytes = new PingFrame(new byte[8]).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Equal(FrameType.Ping, frames[0].Type);
    }

    /// RFC 9113 §6.7 — PING round-trip preserves Data and IsAck
    [Fact(DisplayName = "RFC9113-6.7-PNG-007: PING round-trip preserves Data and IsAck")]
    public void Should_PreserveFields_When_PingRoundTrip()
    {
        var data = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0xCA, 0xFE, 0xBA, 0xBE };
        var original = new PingFrame(data, isAck: true);
        var bytes = original.Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(bytes);

        var decoded = Assert.IsType<PingFrame>(frames[0]);
        Assert.True(decoded.Data.Span.SequenceEqual(original.Data.Span));
        Assert.Equal(original.IsAck, decoded.IsAck);
    }
}
