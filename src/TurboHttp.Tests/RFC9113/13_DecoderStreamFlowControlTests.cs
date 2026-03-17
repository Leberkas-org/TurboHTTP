using TurboHttp.Protocol.RFC9113;

namespace TurboHttp.Tests.RFC9113;

/// <summary>
/// RFC 9113 §6.9 — WINDOW_UPDATE Frame: decoder-level stream flow control tests.
///
/// Verifies that <see cref="Http2FrameDecoder"/> decodes WINDOW_UPDATE frames and
/// DATA frames correctly and enforces RFC-mandated wire constraints.
///
/// Covered:
///   §6.9  : WINDOW_UPDATE decoded — connection window (stream 0)
///   §6.9  : WINDOW_UPDATE decoded — stream window (stream N)
///   §6.9  : Zero increment → PROTOCOL_ERROR
///   §6.9  : Wrong payload size → FRAME_SIZE_ERROR
///   §6.9  : DATA frame fields preserved on decode
///   §6.9  : Increment preserved exactly by decoder
/// </summary>
public sealed class Http2DecoderStreamFlowControlTests
{
    /// RFC 9113 §6.9 — WINDOW_UPDATE on stream 0 (connection window) decoded correctly
    [Fact(DisplayName = "RFC-9113-§6.9-dec-001: WINDOW_UPDATE on stream 0 decoded correctly")]
    public void WindowUpdate_Stream0_DecodedCorrectly()
    {
        var frame = new WindowUpdateFrame(0, 32768).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(frame);

        Assert.Single(frames);
        var wu = Assert.IsType<WindowUpdateFrame>(frames[0]);
        Assert.Equal(0, wu.StreamId);
        Assert.Equal(32768, wu.Increment);
    }

    /// RFC 9113 §6.9 — WINDOW_UPDATE on stream N (stream window) decoded correctly
    [Fact(DisplayName = "RFC-9113-§6.9-dec-002: WINDOW_UPDATE on stream N decoded correctly")]
    public void WindowUpdate_StreamN_DecodedCorrectly()
    {
        var frame = new WindowUpdateFrame(3, 8192).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(frame);

        Assert.Single(frames);
        var wu = Assert.IsType<WindowUpdateFrame>(frames[0]);
        Assert.Equal(3, wu.StreamId);
        Assert.Equal(8192, wu.Increment);
    }

    /// RFC 9113 §6.9 — Zero-increment WINDOW_UPDATE on stream 0 causes PROTOCOL_ERROR
    [Fact(DisplayName = "RFC-9113-§6.9-dec-003: Zero-increment WINDOW_UPDATE causes PROTOCOL_ERROR")]
    public void WindowUpdate_ZeroIncrement_ThrowsProtocolError()
    {
        var rawFrame = new byte[]
        {
            0x00, 0x00, 0x04, // length = 4
            0x08,             // WINDOW_UPDATE
            0x00,             // flags
            0x00, 0x00, 0x00, 0x00, // stream = 0
            0x00, 0x00, 0x00, 0x00, // increment = 0 — illegal
        };
        var decoder = new Http2FrameDecoder();
        var ex = Assert.Throws<Http2Exception>(() => decoder.Decode(rawFrame));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    /// RFC 9113 §6.9 — WINDOW_UPDATE with wrong payload size causes FRAME_SIZE_ERROR
    [Fact(DisplayName = "RFC-9113-§6.9-dec-004: WINDOW_UPDATE with wrong payload size causes FRAME_SIZE_ERROR")]
    public void WindowUpdate_WrongPayloadSize_ThrowsFrameSizeError()
    {
        // Payload must be exactly 4 bytes; use 5 bytes.
        var rawFrame = new byte[]
        {
            0x00, 0x00, 0x05, // length = 5 (must be 4)
            0x08,             // WINDOW_UPDATE
            0x00,             // flags
            0x00, 0x00, 0x00, 0x00, // stream = 0
            0x00, 0x00, 0x01, 0x00, 0x00, // 5 payload bytes
        };
        var decoder = new Http2FrameDecoder();
        var ex = Assert.Throws<Http2Exception>(() => decoder.Decode(rawFrame));
        Assert.Equal(Http2ErrorCode.FrameSizeError, ex.ErrorCode);
    }

    /// RFC 9113 §6.9 — DATA frame decoded with correct StreamId, payload and EndStream
    [Fact(DisplayName = "RFC-9113-§6.9-dec-005: DATA frame decoded with correct fields")]
    public void DataFrame_DecodedWithCorrectFields()
    {
        var data = new byte[] { 0x01, 0x02, 0x03 };
        var frame = new DataFrame(1, data, endStream: true).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(frame);

        Assert.Single(frames);
        var df = Assert.IsType<DataFrame>(frames[0]);
        Assert.Equal(1, df.StreamId);
        Assert.Equal(data, df.Data.ToArray());
        Assert.True(df.EndStream);
    }

    /// RFC 9113 §6.9 — WINDOW_UPDATE increment preserved exactly by decoder
    [Fact(DisplayName = "RFC-9113-§6.9-dec-006: WINDOW_UPDATE increment preserved exactly by decoder")]
    public void WindowUpdate_IncrementPreservedExactly()
    {
        // Test several distinct increment values
        var increments = new[] { 1, 100, 65535, 65536, 0x7FFFFFFE, 0x7FFFFFFF };
        foreach (var increment in increments)
        {
            var bytes = new WindowUpdateFrame(1, increment).Serialize();
            var decoder = new Http2FrameDecoder();
            var frames = decoder.Decode(bytes);

            Assert.Single(frames);
            var wu = Assert.IsType<WindowUpdateFrame>(frames[0]);
            Assert.Equal(increment, wu.Increment);
        }
    }
}
