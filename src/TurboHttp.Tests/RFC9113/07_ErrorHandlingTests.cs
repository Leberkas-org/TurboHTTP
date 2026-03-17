using System.Buffers.Binary;
using TurboHttp.Protocol.RFC9113;

namespace TurboHttp.Tests.RFC9113;

/// <summary>
/// RFC 9113 §6.4 — RST_STREAM Frame
/// RFC 9113 §6.7 — PING Frame
///
/// Tests verify that <see cref="Http2FrameDecoder"/> correctly decodes RST_STREAM
/// and PING frames and enforces wire-format constraints as specified in §6.4 and §6.7.
///
/// Covered (RST_STREAM §6.4):
///   - StreamId, ErrorCode fields decoded correctly
///   - FrameType is RstStream
///   - Wrong payload length → FRAME_SIZE_ERROR (connection error)
///   - Various error codes: NoError, Cancel, ProtocolError, InternalError
///   - Round-trip: serialize then decode preserves all fields
///
/// Covered (PING §6.7):
///   - Data (8 bytes) decoded correctly
///   - IsAck flag (ACK bit) decoded correctly
///   - FrameType is Ping
///   - Wrong payload length → FRAME_SIZE_ERROR (connection error)
///   - PING on non-zero stream → PROTOCOL_ERROR (connection error)
///   - Round-trip: serialize then decode preserves all fields
///
/// Test IDs: RST-001..RST-007, PNG-001..PNG-007
/// </summary>
public sealed class Http2RstStreamPingTests
{
    // =========================================================================
    // RST-001..RST-007: RST_STREAM Frame — §6.4
    // =========================================================================

    /// RFC 9113 §6.4 — RST_STREAM decoded with correct StreamId
    [Fact(DisplayName = "RFC-9113-§6.4-RST-001: RST_STREAM decoded with correct StreamId")]
    public void RstStream_DecodedWithCorrectStreamId()
    {
        var bytes = new RstStreamFrame(5, Http2ErrorCode.Cancel).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Single(frames);
        var frame = Assert.IsType<RstStreamFrame>(frames[0]);
        Assert.Equal(5, frame.StreamId);
    }

    /// RFC 9113 §6.4 — RST_STREAM decoded with correct ErrorCode
    [Fact(DisplayName = "RFC-9113-§6.4-RST-002: RST_STREAM decoded with correct ErrorCode")]
    public void RstStream_DecodedWithCorrectErrorCode()
    {
        var bytes = new RstStreamFrame(3, Http2ErrorCode.Cancel).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Single(frames);
        var frame = Assert.IsType<RstStreamFrame>(frames[0]);
        Assert.Equal(Http2ErrorCode.Cancel, frame.ErrorCode);
    }

    /// RFC 9113 §6.4 — RST_STREAM has correct FrameType
    [Fact(DisplayName = "RFC-9113-§6.4-RST-003: RST_STREAM has correct FrameType")]
    public void RstStream_HasCorrectFrameType()
    {
        var bytes = new RstStreamFrame(1, Http2ErrorCode.NoError).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Single(frames);
        Assert.Equal(FrameType.RstStream, frames[0].Type);
    }

    /// RFC 9113 §6.4 — RST_STREAM with wrong payload length is FRAME_SIZE_ERROR
    [Fact(DisplayName = "RFC-9113-§6.4-RST-004: RST_STREAM with wrong payload length is FRAME_SIZE_ERROR")]
    public void RstStream_WrongPayloadLength_IsFrameSizeError()
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
    [Fact(DisplayName = "RFC-9113-§6.4-RST-005: RST_STREAM round-trip preserves StreamId and ErrorCode")]
    public void RstStream_RoundTrip_PreservesFields()
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
    [Fact(DisplayName = "RFC-9113-§6.4-RST-006: RST_STREAM with ProtocolError decoded correctly")]
    public void RstStream_ProtocolError_DecodedCorrectly()
    {
        var bytes = new RstStreamFrame(11, Http2ErrorCode.ProtocolError).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(bytes);

        var frame = Assert.IsType<RstStreamFrame>(frames[0]);
        Assert.Equal(Http2ErrorCode.ProtocolError, frame.ErrorCode);
        Assert.Equal(11, frame.StreamId);
    }

    /// RFC 9113 §6.4 — RST_STREAM various error codes decoded correctly
    [Theory(DisplayName = "RFC-9113-§6.4-RST-007: RST_STREAM various error codes decoded correctly")]
    [InlineData(Http2ErrorCode.NoError)]
    [InlineData(Http2ErrorCode.Cancel)]
    [InlineData(Http2ErrorCode.FlowControlError)]
    [InlineData(Http2ErrorCode.RefusedStream)]
    [InlineData(Http2ErrorCode.StreamClosed)]
    public void RstStream_VariousErrorCodes_DecodedCorrectly(Http2ErrorCode errorCode)
    {
        var bytes = new RstStreamFrame(1, errorCode).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(bytes);

        var frame = Assert.IsType<RstStreamFrame>(frames[0]);
        Assert.Equal(errorCode, frame.ErrorCode);
    }

    // =========================================================================
    // PNG-001..PNG-007: PING Frame — §6.7
    // =========================================================================

    /// RFC 9113 §6.7 — PING decoded with correct Data
    [Fact(DisplayName = "RFC-9113-§6.7-PNG-001: PING decoded with correct Data")]
    public void Ping_DecodedWithCorrectData()
    {
        var pingData = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var bytes = new PingFrame(pingData).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Single(frames);
        var frame = Assert.IsType<PingFrame>(frames[0]);
        Assert.Equal(pingData, frame.Data);
    }

    /// RFC 9113 §6.7 — PING IsAck=false when ACK bit not set
    [Fact(DisplayName = "RFC-9113-§6.7-PNG-002: PING IsAck=false when ACK bit not set")]
    public void Ping_NonAck_IsAckFalse()
    {
        var bytes = new PingFrame(new byte[8]).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(bytes);

        var frame = Assert.IsType<PingFrame>(frames[0]);
        Assert.False(frame.IsAck);
    }

    /// RFC 9113 §6.7 — PING ACK decoded with IsAck=true
    [Fact(DisplayName = "RFC-9113-§6.7-PNG-003: PING ACK decoded with IsAck=true")]
    public void Ping_Ack_IsAckTrue()
    {
        var bytes = new PingFrame(new byte[8], isAck: true).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(bytes);

        var frame = Assert.IsType<PingFrame>(frames[0]);
        Assert.True(frame.IsAck);
    }

    /// RFC 9113 §6.7 — PING with wrong payload length is FRAME_SIZE_ERROR
    [Fact(DisplayName = "RFC-9113-§6.7-PNG-004: PING with wrong payload length is FRAME_SIZE_ERROR")]
    public void Ping_WrongPayloadLength_IsFrameSizeError()
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
    [Fact(DisplayName = "RFC-9113-§6.7-PNG-005: PING on non-zero stream is PROTOCOL_ERROR")]
    public void Ping_OnNonZeroStream_IsProtocolError()
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
    [Fact(DisplayName = "RFC-9113-§6.7-PNG-006: PING has correct FrameType")]
    public void Ping_HasCorrectFrameType()
    {
        var bytes = new PingFrame(new byte[8]).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Equal(FrameType.Ping, frames[0].Type);
    }

    /// RFC 9113 §6.7 — PING round-trip preserves Data and IsAck
    [Fact(DisplayName = "RFC-9113-§6.7-PNG-007: PING round-trip preserves Data and IsAck")]
    public void Ping_RoundTrip_PreservesFields()
    {
        var data = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0xCA, 0xFE, 0xBA, 0xBE };
        var original = new PingFrame(data, isAck: true);
        var bytes = original.Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(bytes);

        var decoded = Assert.IsType<PingFrame>(frames[0]);
        Assert.Equal(original.Data, decoded.Data);
        Assert.Equal(original.IsAck, decoded.IsAck);
    }
}
