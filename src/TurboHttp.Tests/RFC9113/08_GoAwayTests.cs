using System.Buffers.Binary;
using TurboHttp.Protocol.RFC9113;

namespace TurboHttp.Tests.RFC9113;

/// <summary>
/// Tests GOAWAY frame decoding per RFC 9113 §6.8.
/// Verifies last-stream-ID, error code, and optional debug data extraction.
/// </summary>
/// <remarks>
/// Class under test: <see cref="Http2FrameDecoder"/>.
/// RFC 9113 §6.8: GOAWAY initiates graceful shutdown, carrying the highest processed stream ID and an error code.
/// </remarks>
public sealed class Http2GoAwayTests
{
    // GA-001..GA-005: GOAWAY basic field decoding

    /// RFC 9113 §6.8 — GOAWAY decoded with correct LastStreamId
    [Fact(DisplayName = "RFC9113-6.8-GA-001: GOAWAY decoded with correct LastStreamId")]
    public void Should_DecodeWithCorrectLastStreamId_When_GoAwayFrameReceived()
    {
        var bytes = new GoAwayFrame(7, Http2ErrorCode.NoError).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Single(frames);
        var frame = Assert.IsType<GoAwayFrame>(frames[0]);
        Assert.Equal(7, frame.LastStreamId);
    }

    /// RFC 9113 §6.8 — GOAWAY decoded with correct ErrorCode
    [Fact(DisplayName = "RFC9113-6.8-GA-002: GOAWAY decoded with correct ErrorCode")]
    public void Should_DecodeWithCorrectErrorCode_When_GoAwayFrameReceived()
    {
        var bytes = new GoAwayFrame(3, Http2ErrorCode.ProtocolError).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Single(frames);
        var frame = Assert.IsType<GoAwayFrame>(frames[0]);
        Assert.Equal(Http2ErrorCode.ProtocolError, frame.ErrorCode);
    }

    /// RFC 9113 §6.8 — GOAWAY has FrameType GoAway
    [Fact(DisplayName = "RFC9113-6.8-GA-003: GOAWAY has FrameType GoAway")]
    public void Should_HaveCorrectFrameType_When_GoAwayFrameReceived()
    {
        var bytes = new GoAwayFrame(1, Http2ErrorCode.NoError).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Equal(FrameType.GoAway, frames[0].Type);
    }

    /// RFC 9113 §6.8 — GOAWAY StreamId is always 0
    [Fact(DisplayName = "RFC9113-6.8-GA-004: GOAWAY StreamId is always 0")]
    public void Should_HaveZeroStreamId_When_GoAwayFrameReceived()
    {
        var bytes = new GoAwayFrame(5, Http2ErrorCode.NoError).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Equal(0, frames[0].StreamId);
    }

    /// RFC 9113 §6.8 — GOAWAY without debug data has empty DebugData
    [Fact(DisplayName = "RFC9113-6.8-GA-005: GOAWAY without debug data has empty DebugData")]
    public void Should_HaveEmptyDebugData_When_GoAwayHasNoDebugData()
    {
        var bytes = new GoAwayFrame(1, Http2ErrorCode.NoError).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(bytes);

        var frame = Assert.IsType<GoAwayFrame>(frames[0]);
        Assert.True(frame.DebugData.IsEmpty);
    }

    // GA-006..GA-007: GOAWAY debug data and special lastStreamId values

    /// RFC 9113 §6.8 — GOAWAY with debug data decoded correctly
    [Fact(DisplayName = "RFC9113-6.8-GA-006: GOAWAY with debug data decoded correctly")]
    public void Should_DecodeCorrectly_When_GoAwayHasDebugData()
    {
        var debugData = "graceful shutdown"u8.ToArray();
        var bytes = new GoAwayFrame(3, Http2ErrorCode.NoError, debugData).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(bytes);

        var frame = Assert.IsType<GoAwayFrame>(frames[0]);
        Assert.True(frame.DebugData.Span.SequenceEqual(debugData));
    }

    /// RFC 9113 §6.8 — GOAWAY with lastStreamId=0 decoded correctly
    [Fact(DisplayName = "RFC9113-6.8-GA-007: GOAWAY with lastStreamId=0 decoded correctly")]
    public void Should_DecodeCorrectly_When_GoAwayLastStreamIdIsZero()
    {
        var bytes = new GoAwayFrame(0, Http2ErrorCode.NoError).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(bytes);

        var frame = Assert.IsType<GoAwayFrame>(frames[0]);
        Assert.Equal(0, frame.LastStreamId);
    }

    // GA-008: GOAWAY on non-zero stream → PROTOCOL_ERROR

    /// RFC 9113 §6.8 — GOAWAY on non-zero stream is PROTOCOL_ERROR
    [Fact(DisplayName = "RFC9113-6.8-GA-008: GOAWAY on non-zero stream is PROTOCOL_ERROR")]
    public void Should_BeProtocolError_When_GoAwayOnNonZeroStream()
    {
        // Craft a GOAWAY frame with stream ID = 1 (violates RFC 9113 §6.8).
        var frame = new byte[9 + 8];
        frame[0] = 0; frame[1] = 0; frame[2] = 8;   // length = 8
        frame[3] = 0x7;                               // type = GOAWAY
        frame[4] = 0x0;                               // no flags
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(5), 1u); // stream 1
        // lastStreamId=0, errorCode=0

        var decoder = new Http2FrameDecoder();
        var ex = Assert.Throws<Http2Exception>(() => decoder.Decode(frame));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.Equal(Http2ErrorScope.Connection, ex.Scope);
    }

    // GA-009: Round-trip

    /// RFC 9113 §6.8 — GOAWAY round-trip preserves LastStreamId, ErrorCode, and DebugData
    [Fact(DisplayName = "RFC9113-6.8-GA-009: GOAWAY round-trip preserves LastStreamId, ErrorCode, and DebugData")]
    public void Should_PreserveAllFields_When_GoAwayRoundTrip()
    {
        var debugData = new byte[] { 0xAB, 0xCD };
        var original = new GoAwayFrame(9, Http2ErrorCode.InternalError, debugData);
        var bytes = original.Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(bytes);

        var decoded = Assert.IsType<GoAwayFrame>(frames[0]);
        Assert.Equal(original.LastStreamId, decoded.LastStreamId);
        Assert.Equal(original.ErrorCode, decoded.ErrorCode);
        Assert.True(decoded.DebugData.Span.SequenceEqual(original.DebugData.Span));
    }

    // GA-010: Various error codes

    /// RFC 9113 §6.8 — GOAWAY various error codes decoded correctly
    [Theory(DisplayName = "RFC9113-6.8-GA-010: GOAWAY various error codes decoded correctly")]
    [InlineData(Http2ErrorCode.NoError)]
    [InlineData(Http2ErrorCode.ProtocolError)]
    [InlineData(Http2ErrorCode.InternalError)]
    [InlineData(Http2ErrorCode.Cancel)]
    [InlineData(Http2ErrorCode.FlowControlError)]
    [InlineData(Http2ErrorCode.CompressionError)]
    public void Should_DecodeCorrectly_When_GoAwayHasVariousErrorCodes(Http2ErrorCode errorCode)
    {
        var bytes = new GoAwayFrame(1, errorCode).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(bytes);

        var frame = Assert.IsType<GoAwayFrame>(frames[0]);
        Assert.Equal(errorCode, frame.ErrorCode);
    }

    // GA-011: Reserved high bit in lastStreamId stripped

    /// RFC 9113 §6.8 — Reserved high bit in LastStreamId field is stripped
    [Fact(DisplayName = "RFC9113-6.8-GA-011: Reserved high bit in LastStreamId field is stripped")]
    public void Should_Strip_When_GoAwayLastStreamIdHasReservedHighBit()
    {
        // Craft a GOAWAY frame with the reserved high bit set in lastStreamId.
        var frame = new byte[9 + 8];
        frame[0] = 0; frame[1] = 0; frame[2] = 8;   // length = 8
        frame[3] = 0x7;                               // type = GOAWAY
        frame[4] = 0x0;                               // no flags
        // stream id = 0
        // lastStreamId with high bit set = 0x80000005 → should decode as 5
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(9), 0x80000005u);
        // errorCode = 0

        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(frame);

        var decoded = Assert.IsType<GoAwayFrame>(frames[0]);
        Assert.Equal(5, decoded.LastStreamId);
    }
}
