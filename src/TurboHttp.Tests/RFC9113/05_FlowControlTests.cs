using System.Buffers.Binary;
using TurboHttp.Protocol.RFC9113;

namespace TurboHttp.Tests.RFC9113;

/// <summary>
/// RFC 9113 §6.9 — WINDOW_UPDATE Frame
/// RFC 7540 §6.9 — WINDOW_UPDATE Frame
///
/// Tests verify that <see cref="Http2FrameDecoder"/> correctly decodes WINDOW_UPDATE and
/// DATA frames and enforces wire-format constraints as specified in §6.9.
///
/// Covered:
///   §6.9  : WINDOW_UPDATE on stream 0 (connection-level) — StreamId and Increment fields
///   §6.9  : WINDOW_UPDATE on stream N (stream-level) — StreamId and Increment fields
///   §6.9  : Multiple WINDOW_UPDATE frames in a single Decode call
///   §6.9  : Increment value edge cases — minimum (1), large values, maximum (0x7FFFFFFF)
///   §6.9  : Reserved bit in increment field stripped (high bit ignored)
///   §6.9  : Zero increment → PROTOCOL_ERROR (connection error)
///   §6.9  : Wrong payload size → FRAME_SIZE_ERROR (connection error)
///   §6.9  : DATA frame — StreamId, Data, EndStream decoded correctly
///   §6.9  : Round-trip: serialize then decode preserves all fields
/// </summary>
public sealed class Http2FlowControlTests
{
    // =========================================================================
    // FC-WU-001..006: WINDOW_UPDATE Decoding — Connection Level (Stream 0)
    // =========================================================================

    /// RFC 9113 §6.9 — WINDOW_UPDATE on stream 0 decoded with correct StreamId
    [Fact(DisplayName = "RFC-9113-§6.9-FC-WU-001: WINDOW_UPDATE on stream 0 decoded with correct StreamId")]
    public void WindowUpdate_Stream0_HasCorrectStreamId()
    {
        var bytes = new WindowUpdateFrame(0, 1000).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Single(frames);
        var frame = Assert.IsType<WindowUpdateFrame>(frames[0]);
        Assert.Equal(0, frame.StreamId);
    }

    /// RFC 9113 §6.9 — WINDOW_UPDATE on stream 0 decoded with correct Increment
    [Fact(DisplayName = "RFC-9113-§6.9-FC-WU-002: WINDOW_UPDATE on stream 0 decoded with correct Increment")]
    public void WindowUpdate_Stream0_HasCorrectIncrement()
    {
        var bytes = new WindowUpdateFrame(0, 32768).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Single(frames);
        var frame = Assert.IsType<WindowUpdateFrame>(frames[0]);
        Assert.Equal(32768, frame.Increment);
    }

    /// RFC 9113 §6.9 — WINDOW_UPDATE on stream 0 has correct FrameType
    [Fact(DisplayName = "RFC-9113-§6.9-FC-WU-003: WINDOW_UPDATE on stream 0 has correct FrameType")]
    public void WindowUpdate_Stream0_HasCorrectFrameType()
    {
        var bytes = new WindowUpdateFrame(0, 1).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Single(frames);
        Assert.IsType<WindowUpdateFrame>(frames[0]);
        Assert.Equal(FrameType.WindowUpdate, frames[0].Type);
    }

    /// RFC 9113 §6.9 — Multiple connection-level WINDOW_UPDATEs decoded as independent frames
    [Fact(DisplayName = "RFC-9113-§6.9-FC-WU-004: Multiple connection-level WINDOW_UPDATEs decoded independently")]
    public void WindowUpdate_MultipleStream0_DecodedAsIndependentFrames()
    {
        var wu1 = new WindowUpdateFrame(0, 1000).Serialize();
        var wu2 = new WindowUpdateFrame(0, 500).Serialize();
        var combined = wu1.Concat(wu2).ToArray();

        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(combined);

        Assert.Equal(2, frames.Count);
        var frame1 = Assert.IsType<WindowUpdateFrame>(frames[0]);
        var frame2 = Assert.IsType<WindowUpdateFrame>(frames[1]);
        Assert.Equal(0, frame1.StreamId);
        Assert.Equal(1000, frame1.Increment);
        Assert.Equal(0, frame2.StreamId);
        Assert.Equal(500, frame2.Increment);
    }

    /// RFC 9113 §6.9 — Connection WINDOW_UPDATE with minimum valid increment (1) accepted
    [Fact(DisplayName = "RFC-9113-§6.9-FC-WU-005: Connection WINDOW_UPDATE with increment=1 accepted")]
    public void WindowUpdate_Stream0_IncrementOne_Accepted()
    {
        var bytes = new WindowUpdateFrame(0, 1).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Single(frames);
        var frame = Assert.IsType<WindowUpdateFrame>(frames[0]);
        Assert.Equal(1, frame.Increment);
    }

    /// RFC 9113 §6.9 — Connection WINDOW_UPDATE with maximum valid increment (0x7FFFFFFF) accepted
    [Fact(DisplayName = "RFC-9113-§6.9-FC-WU-006: Connection WINDOW_UPDATE with max increment accepted")]
    public void WindowUpdate_Stream0_MaxIncrement_Accepted()
    {
        var bytes = new WindowUpdateFrame(0, 0x7FFFFFFF).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Single(frames);
        var frame = Assert.IsType<WindowUpdateFrame>(frames[0]);
        Assert.Equal(0x7FFFFFFF, frame.Increment);
    }

    // =========================================================================
    // FC-WU-007..012: WINDOW_UPDATE Decoding — Stream Level (Stream N)
    // =========================================================================

    /// RFC 9113 §6.9 — WINDOW_UPDATE on stream 1 decoded with correct StreamId
    [Fact(DisplayName = "RFC-9113-§6.9-FC-WU-007: WINDOW_UPDATE on stream 1 decoded with correct StreamId")]
    public void WindowUpdate_Stream1_HasCorrectStreamId()
    {
        var bytes = new WindowUpdateFrame(1, 2000).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Single(frames);
        var frame = Assert.IsType<WindowUpdateFrame>(frames[0]);
        Assert.Equal(1, frame.StreamId);
    }

    /// RFC 9113 §6.9 — WINDOW_UPDATE on stream 3 decoded with correct Increment
    [Fact(DisplayName = "RFC-9113-§6.9-FC-WU-008: WINDOW_UPDATE on stream 3 decoded with correct Increment")]
    public void WindowUpdate_Stream3_HasCorrectIncrement()
    {
        var bytes = new WindowUpdateFrame(3, 65535).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Single(frames);
        var frame = Assert.IsType<WindowUpdateFrame>(frames[0]);
        Assert.Equal(3, frame.StreamId);
        Assert.Equal(65535, frame.Increment);
    }

    /// RFC 9113 §6.9 — Mixed stream-0 and stream-N WINDOW_UPDATEs decoded independently
    [Fact(DisplayName = "RFC-9113-§6.9-FC-WU-009: Mixed stream-0 and stream-N WINDOW_UPDATEs decoded independently")]
    public void WindowUpdate_Mixed_Stream0AndStreamN_DecodedIndependently()
    {
        var wu0 = new WindowUpdateFrame(0, 100).Serialize();
        var wu1 = new WindowUpdateFrame(1, 200).Serialize();
        var wu3 = new WindowUpdateFrame(3, 300).Serialize();
        var combined = wu0.Concat(wu1).Concat(wu3).ToArray();

        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(combined);

        Assert.Equal(3, frames.Count);
        var f0 = Assert.IsType<WindowUpdateFrame>(frames[0]);
        var f1 = Assert.IsType<WindowUpdateFrame>(frames[1]);
        var f3 = Assert.IsType<WindowUpdateFrame>(frames[2]);
        Assert.Equal(0, f0.StreamId);
        Assert.Equal(100, f0.Increment);
        Assert.Equal(1, f1.StreamId);
        Assert.Equal(200, f1.Increment);
        Assert.Equal(3, f3.StreamId);
        Assert.Equal(300, f3.Increment);
    }

    /// RFC 9113 §6.9 — Stream WINDOW_UPDATE with large stream ID decoded correctly
    [Fact(DisplayName = "RFC-9113-§6.9-FC-WU-010: Stream WINDOW_UPDATE with large stream ID decoded correctly")]
    public void WindowUpdate_LargeStreamId_DecodedCorrectly()
    {
        var bytes = new WindowUpdateFrame(0x7FFFFFFE, 1024).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Single(frames);
        var frame = Assert.IsType<WindowUpdateFrame>(frames[0]);
        Assert.Equal(0x7FFFFFFE, frame.StreamId);
        Assert.Equal(1024, frame.Increment);
    }

    /// RFC 9113 §6.9 — Stream WINDOW_UPDATE with minimum increment (1) accepted
    [Fact(DisplayName = "RFC-9113-§6.9-FC-WU-011: Stream WINDOW_UPDATE with increment=1 accepted")]
    public void WindowUpdate_StreamN_IncrementOne_Accepted()
    {
        var bytes = new WindowUpdateFrame(5, 1).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Single(frames);
        var frame = Assert.IsType<WindowUpdateFrame>(frames[0]);
        Assert.Equal(5, frame.StreamId);
        Assert.Equal(1, frame.Increment);
    }

    /// RFC 9113 §6.9 — Stream WINDOW_UPDATE with maximum increment (0x7FFFFFFF) accepted
    [Fact(DisplayName = "RFC-9113-§6.9-FC-WU-012: Stream WINDOW_UPDATE with max increment accepted")]
    public void WindowUpdate_StreamN_MaxIncrement_Accepted()
    {
        var bytes = new WindowUpdateFrame(7, 0x7FFFFFFF).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Single(frames);
        var frame = Assert.IsType<WindowUpdateFrame>(frames[0]);
        Assert.Equal(7, frame.StreamId);
        Assert.Equal(0x7FFFFFFF, frame.Increment);
    }

    // =========================================================================
    // FC-WU-013..016: Reserved bit handling and increment values
    // =========================================================================

    /// RFC 9113 §6.9 — Reserved high bit of increment field is stripped on decode
    [Fact(DisplayName = "RFC-9113-§6.9-FC-WU-013: Reserved high bit of increment field stripped on decode")]
    public void WindowUpdate_ReservedHighBit_StrippedOnDecode()
    {
        // Build raw WINDOW_UPDATE with high bit set: 0x80000001 → increment should be 1
        var rawFrame = new byte[]
        {
            0x00, 0x00, 0x04, // length = 4
            0x08,             // WINDOW_UPDATE
            0x00,             // flags
            0x00, 0x00, 0x00, 0x01, // stream = 1
            0x80, 0x00, 0x00, 0x01, // increment with high bit set → stripped to 1
        };
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(rawFrame);

        Assert.Single(frames);
        var frame = Assert.IsType<WindowUpdateFrame>(frames[0]);
        Assert.Equal(1, frame.Increment); // high bit stripped
    }

    /// RFC 9113 §6.9 — WINDOW_UPDATE round-trip on stream 0 preserves all fields
    [Fact(DisplayName = "RFC-9113-§6.9-FC-WU-014: WINDOW_UPDATE round-trip on stream 0 preserves fields")]
    public void WindowUpdate_RoundTrip_Stream0_PreservesFields()
    {
        var original = new WindowUpdateFrame(0, 131072);
        var bytes = original.Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Single(frames);
        var decoded = Assert.IsType<WindowUpdateFrame>(frames[0]);
        Assert.Equal(0, decoded.StreamId);
        Assert.Equal(131072, decoded.Increment);
    }

    /// RFC 9113 §6.9 — WINDOW_UPDATE round-trip on stream N preserves all fields
    [Fact(DisplayName = "RFC-9113-§6.9-FC-WU-015: WINDOW_UPDATE round-trip on stream N preserves fields")]
    public void WindowUpdate_RoundTrip_StreamN_PreservesFields()
    {
        var original = new WindowUpdateFrame(9, 4096);
        var bytes = original.Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Single(frames);
        var decoded = Assert.IsType<WindowUpdateFrame>(frames[0]);
        Assert.Equal(9, decoded.StreamId);
        Assert.Equal(4096, decoded.Increment);
    }

    /// RFC 9113 §6.9 — TCP-fragmented WINDOW_UPDATE decoded correctly across two calls
    [Fact(DisplayName = "RFC-9113-§6.9-FC-WU-016: TCP-fragmented WINDOW_UPDATE decoded across two calls")]
    public void WindowUpdate_TcpFragmented_DecodedAcrossTwoCalls()
    {
        var bytes = new WindowUpdateFrame(0, 8192).Serialize(); // 13 bytes total
        var part1 = bytes[..7];
        var part2 = bytes[7..];

        var decoder = new Http2FrameDecoder();
        var frames1 = decoder.Decode(part1);
        var frames2 = decoder.Decode(part2);

        Assert.Empty(frames1); // incomplete
        Assert.Single(frames2);
        var frame = Assert.IsType<WindowUpdateFrame>(frames2[0]);
        Assert.Equal(0, frame.StreamId);
        Assert.Equal(8192, frame.Increment);
    }

    // =========================================================================
    // FC-WU-017..019: Error cases — PROTOCOL_ERROR and FRAME_SIZE_ERROR
    // =========================================================================

    /// RFC 9113 §6.9 — Zero increment on stream 0 is connection PROTOCOL_ERROR
    [Fact(DisplayName = "RFC-9113-§6.9-FC-WU-017: Zero increment on stream 0 is connection PROTOCOL_ERROR")]
    public void WindowUpdate_ZeroIncrement_Stream0_IsProtocolError()
    {
        var rawFrame = new byte[]
        {
            0x00, 0x00, 0x04, // length = 4
            0x08,             // WINDOW_UPDATE
            0x00,             // flags
            0x00, 0x00, 0x00, 0x00, // stream = 0
            0x00, 0x00, 0x00, 0x00, // increment = 0 — MUST be > 0
        };
        var decoder = new Http2FrameDecoder();
        var ex = Assert.Throws<Http2Exception>(() => decoder.Decode(rawFrame));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    /// RFC 9113 §6.9 — Zero increment on stream N is connection PROTOCOL_ERROR
    [Fact(DisplayName = "RFC-9113-§6.9-FC-WU-018: Zero increment on stream N is connection PROTOCOL_ERROR")]
    public void WindowUpdate_ZeroIncrement_StreamN_IsProtocolError()
    {
        var rawFrame = new byte[]
        {
            0x00, 0x00, 0x04, // length = 4
            0x08,             // WINDOW_UPDATE
            0x00,             // flags
            0x00, 0x00, 0x00, 0x01, // stream = 1
            0x00, 0x00, 0x00, 0x00, // increment = 0 — MUST be > 0
        };
        var decoder = new Http2FrameDecoder();
        var ex = Assert.Throws<Http2Exception>(() => decoder.Decode(rawFrame));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    /// RFC 9113 §6.9 — WINDOW_UPDATE with wrong payload size is FRAME_SIZE_ERROR
    [Fact(DisplayName = "RFC-9113-§6.9-FC-WU-019: WINDOW_UPDATE with wrong payload size is FRAME_SIZE_ERROR")]
    public void WindowUpdate_WrongPayloadSize_IsFrameSizeError()
    {
        var rawFrame = new byte[]
        {
            0x00, 0x00, 0x03, // length = 3 (must be 4)
            0x08,             // WINDOW_UPDATE
            0x00,             // flags
            0x00, 0x00, 0x00, 0x00, // stream = 0
            0x00, 0x00, 0x01, // only 3 payload bytes
        };
        var decoder = new Http2FrameDecoder();
        var ex = Assert.Throws<Http2Exception>(() => decoder.Decode(rawFrame));
        Assert.Equal(Http2ErrorCode.FrameSizeError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    // =========================================================================
    // FC-DF-001..007: DATA Frame Decoding
    // =========================================================================

    /// RFC 9113 §6.9 — DATA frame decoded with correct stream ID and payload
    [Fact(DisplayName = "RFC-9113-§6.9-FC-DF-001: DATA frame decoded with correct StreamId and data")]
    public void DataFrame_DecodedWithCorrectStreamIdAndData()
    {
        var data = new byte[] { 1, 2, 3, 4, 5 };
        var bytes = new DataFrame(1, data).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Single(frames);
        var frame = Assert.IsType<DataFrame>(frames[0]);
        Assert.Equal(1, frame.StreamId);
        Assert.Equal(data, frame.Data.ToArray());
    }

    /// RFC 9113 §6.9 — DATA frame with END_STREAM decoded with EndStream=true
    [Fact(DisplayName = "RFC-9113-§6.9-FC-DF-002: DATA frame with END_STREAM decoded with EndStream=true")]
    public void DataFrame_WithEndStream_DecodedAsEndStream()
    {
        var data = new byte[10];
        var bytes = new DataFrame(3, data, endStream: true).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Single(frames);
        var frame = Assert.IsType<DataFrame>(frames[0]);
        Assert.True(frame.EndStream);
        Assert.Equal(3, frame.StreamId);
    }

    /// RFC 9113 §6.9 — DATA frame without END_STREAM decoded with EndStream=false
    [Fact(DisplayName = "RFC-9113-§6.9-FC-DF-003: DATA frame without END_STREAM decoded with EndStream=false")]
    public void DataFrame_WithoutEndStream_DecodedAsNotEndStream()
    {
        var data = new byte[10];
        var bytes = new DataFrame(5, data, endStream: false).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Single(frames);
        var frame = Assert.IsType<DataFrame>(frames[0]);
        Assert.False(frame.EndStream);
    }

    /// RFC 9113 §6.9 — Zero-length DATA frame decoded correctly
    [Fact(DisplayName = "RFC-9113-§6.9-FC-DF-004: Zero-length DATA frame decoded correctly")]
    public void DataFrame_ZeroLength_DecodedCorrectly()
    {
        var bytes = new DataFrame(1, ReadOnlyMemory<byte>.Empty, endStream: true).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Single(frames);
        var frame = Assert.IsType<DataFrame>(frames[0]);
        Assert.Equal(0, frame.Data.Length);
        Assert.True(frame.EndStream);
    }

    /// RFC 9113 §6.9 — DATA frame round-trip preserves StreamId, Data, and EndStream
    [Fact(DisplayName = "RFC-9113-§6.9-FC-DF-005: DATA frame round-trip preserves all fields")]
    public void DataFrame_RoundTrip_PreservesAllFields()
    {
        var data = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        var original = new DataFrame(7, data, endStream: true);
        var bytes = original.Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Single(frames);
        var decoded = Assert.IsType<DataFrame>(frames[0]);
        Assert.Equal(7, decoded.StreamId);
        Assert.Equal(data, decoded.Data.ToArray());
        Assert.True(decoded.EndStream);
    }

    /// RFC 9113 §6.9 — WINDOW_UPDATE followed by DATA decoded as two frames in order
    [Fact(DisplayName = "RFC-9113-§6.9-FC-DF-006: WINDOW_UPDATE followed by DATA decoded as two frames in order")]
    public void WindowUpdateThenDataFrame_DecodedInOrder()
    {
        var wu = new WindowUpdateFrame(1, 65535).Serialize();
        var df = new DataFrame(1, new byte[] { 0x42 }, endStream: true).Serialize();
        var combined = wu.Concat(df).ToArray();

        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(combined);

        Assert.Equal(2, frames.Count);
        Assert.IsType<WindowUpdateFrame>(frames[0]);
        Assert.IsType<DataFrame>(frames[1]);
    }

    /// RFC 9113 §6.9 — Large DATA payload decoded with correct length
    [Fact(DisplayName = "RFC-9113-§6.9-FC-DF-007: Large DATA payload decoded with correct length")]
    public void DataFrame_LargePayload_DecodedCorrectly()
    {
        var data = new byte[16384]; // 16 KB
        for (var i = 0; i < data.Length; i++) { data[i] = (byte)(i & 0xFF); }

        var bytes = new DataFrame(1, data).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Single(frames);
        var frame = Assert.IsType<DataFrame>(frames[0]);
        Assert.Equal(16384, frame.Data.Length);
        Assert.Equal(data, frame.Data.ToArray());
    }
}
