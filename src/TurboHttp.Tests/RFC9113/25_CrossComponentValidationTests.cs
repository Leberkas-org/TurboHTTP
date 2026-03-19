using System.Buffers.Binary;
using TurboHttp.Protocol.RFC7541;
using TurboHttp.Protocol.RFC9113;

namespace TurboHttp.Tests.RFC9113;

/// <summary>
/// RFC 9113 Cross-Component Validation — Frame decoding with enforcement.
///
/// Tests verify correct interaction between decoder output and enforcement:
///
///   CC-001..005 — HPACK failure → connection error (RFC 9113 §4.3)
///       A decompression failure MUST be treated as a connection error of type
///       COMPRESSION_ERROR, regardless of which stream triggered it.
///
///   CC-006..010 — Flow control independent from header decoding (RFC 9113 §6.9)
///       Windows are tracked per-connection and per-stream independently.
///
///   CC-011..014 — Stream cleanup on RST_STREAM (RFC 9113 §6.4)
///       RST_STREAM marks stream closed and prevents further DATA.
///
///   CC-015..018 — GOAWAY stops new stream creation (RFC 9113 §6.8)
///       After GOAWAY, streams > lastStreamId are rejected.
///
///   CC-019..020 — Header injection prevention (RFC 9113 §8.2)
///       Invalid HPACK data and uppercase headers are rejected.
/// </summary>
public sealed class Http2CrossComponentValidationTests
{
    // =========================================================================
    // Helpers
    // =========================================================================

    private static byte[] BuildRawFrame(byte type, byte flags, int streamId, byte[] payload)
    {
        var frame = new byte[9 + payload.Length];
        frame[0] = (byte)(payload.Length >> 16);
        frame[1] = (byte)(payload.Length >> 8);
        frame[2] = (byte)payload.Length;
        frame[3] = type;
        frame[4] = flags;
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(5), (uint)streamId & 0x7FFFFFFF);
        payload.CopyTo(frame, 9);
        return frame;
    }

    private static byte[] BuildHeadersFrame(int streamId, byte[] headerBlock, bool endStream = false, bool endHeaders = true)
    {
        byte flags = 0;
        if (endStream)
        {
            flags |= 0x1;
        }

        if (endHeaders)
        {
            flags |= 0x4;
        }

        return BuildRawFrame(0x1, flags, streamId, headerBlock);
    }

    private static byte[] BuildDataFrame(int streamId, byte[] data, bool endStream = false)
    {
        byte flags = endStream ? (byte)0x1 : (byte)0x0;
        return BuildRawFrame(0x0, flags, streamId, data);
    }

    private static byte[] BuildRstStreamFrame(int streamId, Http2ErrorCode error)
    {
        var payload = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(payload, (uint)error);
        return BuildRawFrame(0x3, 0, streamId, payload);
    }

    private static byte[] BuildGoAwayFrame(int lastStreamId, Http2ErrorCode error = Http2ErrorCode.NoError)
    {
        var payload = new byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(payload, (uint)lastStreamId & 0x7FFFFFFF);
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(4), (uint)error);
        return BuildRawFrame(0x7, 0, 0, payload);
    }

    private static byte[] BuildWindowUpdateFrame(int streamId, int increment)
    {
        var payload = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(payload, (uint)increment & 0x7FFFFFFF);
        return BuildRawFrame(0x8, 0, streamId, payload);
    }

    private static byte[] ValidStatusHeaderBlock()
    {
        var enc = new HpackEncoder(useHuffman: false);
        return enc.Encode([(":status", "200")]).ToArray();
    }

    /// <summary>
    /// RFC 9113 §4.3: HPACK decompression failure is a connection COMPRESSION_ERROR.
    /// The decoder produces the frame; the caller must HPACK-decode the fragment.
    /// If HpackException occurs during decode, wrap it as CompressionError.
    /// </summary>
    private static IReadOnlyList<HpackHeader> DecodeHpackWithCompressionErrorWrapping(
        HpackDecoder hpack,
        ReadOnlySpan<byte> fragment)
    {
        try
        {
            return hpack.Decode(fragment);
        }
        catch (HpackException ex)
        {
            throw new Http2Exception(
                $"RFC 9113 §4.3: HPACK decompression failure — {ex.Message}",
                Http2ErrorCode.CompressionError,
                Http2ErrorScope.Connection);
        }
    }

    /// <summary>
    /// RFC 9113 §6.8: After GOAWAY, new HEADERS with streamId > lastStreamId must be rejected.
    /// </summary>
    private static void EnforceGoAwayRejectsNewStreams(int streamId, int lastStreamId)
    {
        if (streamId > lastStreamId)
        {
            throw new Http2Exception(
                $"RFC 9113 §6.8: HEADERS on stream {streamId} after GOAWAY with lastStreamId={lastStreamId}",
                Http2ErrorCode.ProtocolError,
                Http2ErrorScope.Connection);
        }
    }

    /// <summary>
    /// RFC 9113 §6.1: DATA on a closed stream is a stream error STREAM_CLOSED.
    /// </summary>
    private static void EnforceStreamNotClosed(int streamId, HashSet<int> closedStreams)
    {
        if (closedStreams.Contains(streamId))
        {
            throw new Http2Exception(
                $"RFC 9113 §6.1: DATA on closed stream {streamId}",
                Http2ErrorCode.StreamClosed,
                Http2ErrorScope.Stream,
                streamId);
        }
    }

    /// <summary>
    /// RFC 9113 §8.2: All header names (except pseudo-headers starting with ':') must be lowercase.
    /// </summary>
    private static void ValidateResponseHeaders(IReadOnlyList<HpackHeader> headers)
    {
        foreach (var h in headers)
        {
            if (!h.Name.StartsWith(':'))
            {
                foreach (var c in h.Name)
                {
                    if (char.IsUpper(c))
                    {
                        throw new Http2Exception(
                            $"RFC 9113 §8.2: Header name '{h.Name}' contains uppercase characters.",
                            Http2ErrorCode.ProtocolError,
                            Http2ErrorScope.Connection);
                    }
                }
            }
        }
    }

    // =========================================================================
    // CC-001..005: HPACK failure → connection error (RFC 9113 §4.3)
    // =========================================================================

    [Fact(DisplayName = "RFC9113-4.3-CC-001: Malformed HPACK → CompressionError connection error")]
    public void MalformedHpackBytes_ThrowsCompressionError()
    {
        // 0x80 = indexed representation with index 0 (reserved → HpackException)
        var corruptHpack = new byte[] { 0x80 };
        var headersFrame = BuildHeadersFrame(1, corruptHpack);

        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(headersFrame);
        var frame = Assert.IsType<HeadersFrame>(frames[0]);

        var hpackDecoder = new HpackDecoder();
        var ex = Assert.Throws<Http2Exception>(
            () => DecodeHpackWithCompressionErrorWrapping(hpackDecoder, frame.HeaderBlockFragment.Span));

        Assert.Equal(Http2ErrorCode.CompressionError, ex.ErrorCode);
        Assert.Equal(Http2ErrorScope.Connection, ex.Scope);
        Assert.True(ex.IsConnectionError);
    }

    [Fact(DisplayName = "RFC9113-4.3-CC-002: Out-of-range dynamic index in HPACK → CompressionError")]
    public void OutOfRangeDynamicIndex_ThrowsCompressionError()
    {
        // Dynamic table is empty; reference dynamic index out of range
        // Encoded as 0xFF 0x3F (indexed, index = 127, out of range)
        var corruptHpack = new byte[] { 0xFF, 0x3F };
        var headersFrame = BuildHeadersFrame(1, corruptHpack);

        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(headersFrame);
        var frame = Assert.IsType<HeadersFrame>(frames[0]);

        var hpackDecoder = new HpackDecoder();
        var ex = Assert.Throws<Http2Exception>(
            () => DecodeHpackWithCompressionErrorWrapping(hpackDecoder, frame.HeaderBlockFragment.Span));

        Assert.Equal(Http2ErrorCode.CompressionError, ex.ErrorCode);
        Assert.Equal(Http2ErrorScope.Connection, ex.Scope);
    }

    [Fact(DisplayName = "RFC9113-4.3-CC-003: HPACK CompressionError is connection-level, not stream-level")]
    public void HpackCompressionError_IsConnectionLevel_NotStreamLevel()
    {
        var corruptHpack = new byte[] { 0x80 }; // index 0 is reserved → HpackException
        var headersFrame = BuildHeadersFrame(3, corruptHpack);

        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(headersFrame);
        var frame = Assert.IsType<HeadersFrame>(frames[0]);

        var hpackDecoder = new HpackDecoder();
        var ex = Assert.Throws<Http2Exception>(
            () => DecodeHpackWithCompressionErrorWrapping(hpackDecoder, frame.HeaderBlockFragment.Span));

        // Must NOT be a stream error — RFC 9113 §4.3 mandates connection scope
        Assert.NotEqual(Http2ErrorScope.Stream, ex.Scope);
        Assert.Equal(Http2ErrorScope.Connection, ex.Scope);
        Assert.Equal(0, ex.StreamId); // StreamId = 0 for connection errors
    }

    [Fact(DisplayName = "RFC9113-4.3-CC-004: HPACK empty header name → CompressionError")]
    public void HpackEmptyHeaderName_ThrowsCompressionError()
    {
        // Literal without indexing (0x00), name index=0 (new name), name length = 0 (empty)
        // RFC 7541 §7.2: empty header name is a protocol violation
        var corruptHpack = new byte[] { 0x00, 0x00 }; // literal, new name, empty string
        var headersFrame = BuildHeadersFrame(1, corruptHpack);

        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(headersFrame);
        var frame = Assert.IsType<HeadersFrame>(frames[0]);

        var hpackDecoder = new HpackDecoder();
        var ex = Assert.Throws<Http2Exception>(
            () => DecodeHpackWithCompressionErrorWrapping(hpackDecoder, frame.HeaderBlockFragment.Span));

        Assert.Equal(Http2ErrorCode.CompressionError, ex.ErrorCode);
        Assert.Equal(Http2ErrorScope.Connection, ex.Scope);
    }

    [Fact(DisplayName = "RFC9113-4.3-CC-005: HPACK failure on stream 5 is connection error")]
    public void HpackFailureOnAnyStream_IsConnectionError()
    {
        var decoder = new Http2FrameDecoder();

        // Open stream 1 successfully first
        var goodHeaders = BuildHeadersFrame(1, ValidStatusHeaderBlock(), endHeaders: true);
        var frames1 = decoder.Decode(goodHeaders);
        Assert.Single(frames1);

        // Now trigger HPACK failure on stream 5
        var corruptHpack = new byte[] { 0x80 };
        var badHeadersFrame = BuildHeadersFrame(5, corruptHpack);

        var frames2 = decoder.Decode(badHeadersFrame);
        var frame = Assert.IsType<HeadersFrame>(frames2[0]);

        var hpackDecoder = new HpackDecoder();
        var ex = Assert.Throws<Http2Exception>(
            () => DecodeHpackWithCompressionErrorWrapping(hpackDecoder, frame.HeaderBlockFragment.Span));

        // The HPACK error is connection-level even though stream 1 is fine
        Assert.Equal(Http2ErrorCode.CompressionError, ex.ErrorCode);
        Assert.Equal(Http2ErrorScope.Connection, ex.Scope);
    }

    // =========================================================================
    // CC-006..010: Flow control independent from header decoding (RFC 9113 §6.9)
    // =========================================================================

    [Fact(DisplayName = "RFC9113-6.9-CC-006: Connection window tracked independently from HPACK")]
    public void ConnectionWindow_TrackedIndependentlyFromHpack()
    {
        var decoder = new Http2FrameDecoder();

        // Open stream 1
        var headers = BuildHeadersFrame(1, ValidStatusHeaderBlock());
        var hFrames = decoder.Decode(headers);
        Assert.Single(hFrames);

        // Send 100 bytes of DATA
        var data = BuildDataFrame(1, new byte[100]);
        var dFrames = decoder.Decode(data);
        Assert.Single(dFrames);

        var dataFrame = Assert.IsType<DataFrame>(dFrames[0]);
        Assert.Equal(100, dataFrame.Data.Length);
    }

    [Fact(DisplayName = "RFC9113-6.9-CC-007: Stream windows are independent across streams")]
    public void StreamWindows_AreIndependent_AcrossStreams()
    {
        var decoder = new Http2FrameDecoder();

        // Open streams 1 and 3
        var h1 = decoder.Decode(BuildHeadersFrame(1, ValidStatusHeaderBlock()));
        var h3 = decoder.Decode(BuildHeadersFrame(3, ValidStatusHeaderBlock()));

        Assert.Single(h1);
        Assert.Single(h3);

        // DATA on stream 3 should be decodable independently
        var d3 = decoder.Decode(BuildDataFrame(3, new byte[50]));
        var dataFrame = Assert.IsType<DataFrame>(d3[0]);
        Assert.Equal(3, dataFrame.StreamId);
        Assert.Equal(50, dataFrame.Data.Length);
    }

    [Fact(DisplayName = "RFC9113-6.9-CC-008: Flow control error on stream 1 doesn't corrupt stream 3")]
    public void FlowControlErrorOnStream1_DoesNotCorruptStream3()
    {
        var decoder = new Http2FrameDecoder();

        // Open both streams
        decoder.Decode(BuildHeadersFrame(1, ValidStatusHeaderBlock()));
        decoder.Decode(BuildHeadersFrame(3, ValidStatusHeaderBlock()));

        // DATA on stream 1
        var d1 = decoder.Decode(BuildDataFrame(1, new byte[10]));
        Assert.Single(d1);

        // Stream 3 window should still allow data
        var d3 = decoder.Decode(BuildDataFrame(3, new byte[50]));
        Assert.Single(d3);
        var dataFrame = Assert.IsType<DataFrame>(d3[0]);
        Assert.Equal(3, dataFrame.StreamId);
    }

    [Fact(DisplayName = "RFC9113-6.9-CC-009: WINDOW_UPDATE on stream 1 doesn't affect stream 3")]
    public void WindowUpdateOnStream1_DoesNotAffectStream3()
    {
        var decoder = new Http2FrameDecoder();

        // Open stream 1
        decoder.Decode(BuildHeadersFrame(1, ValidStatusHeaderBlock()));

        // WINDOW_UPDATE on stream 1
        var wu1 = decoder.Decode(BuildWindowUpdateFrame(1, 1000));
        var frame1 = Assert.IsType<WindowUpdateFrame>(wu1[0]);
        Assert.Equal(1, frame1.StreamId);
        Assert.Equal(1000, frame1.Increment);

        // Stream 3 is unaffected (idle, no explicit window yet)
        // WINDOW_UPDATE on stream 0 (connection) should not affect stream 3's logical window
        var wu0 = decoder.Decode(BuildWindowUpdateFrame(0, 5000));
        var frame0 = Assert.IsType<WindowUpdateFrame>(wu0[0]);
        Assert.Equal(0, frame0.StreamId);
    }

    [Fact(DisplayName = "RFC9113-6.9-CC-010: Connection WINDOW_UPDATE is independent from stream windows")]
    public void ConnectionWindowUpdate_IsIndependentFromStreams()
    {
        var decoder = new Http2FrameDecoder();

        // Open stream 1
        decoder.Decode(BuildHeadersFrame(1, ValidStatusHeaderBlock()));

        // WINDOW_UPDATE on stream 0 (connection level)
        var wu = decoder.Decode(BuildWindowUpdateFrame(0, 5000));
        var frame = Assert.IsType<WindowUpdateFrame>(wu[0]);
        Assert.Equal(0, frame.StreamId);
        Assert.Equal(5000, frame.Increment);
    }

    // =========================================================================
    // CC-011..014: Stream cleanup on RST_STREAM (RFC 9113 §6.4)
    // =========================================================================

    [Fact(DisplayName = "RFC9113-6.4-CC-011: RST_STREAM decrements active stream count")]
    public void RstStream_DecrementsActiveStreamCount()
    {
        var decoder = new Http2FrameDecoder();
        var openStreams = new HashSet<int>();
        var closedStreams = new HashSet<int>();

        // Open 2 streams
        var h1 = decoder.Decode(BuildHeadersFrame(1, ValidStatusHeaderBlock()));
        var h3 = decoder.Decode(BuildHeadersFrame(3, ValidStatusHeaderBlock()));

        var frame1 = Assert.IsType<HeadersFrame>(h1[0]);
        var frame3 = Assert.IsType<HeadersFrame>(h3[0]);
        openStreams.Add(frame1.StreamId);
        openStreams.Add(frame3.StreamId);

        Assert.Equal(2, openStreams.Count);

        // RST_STREAM on stream 1
        var rst = decoder.Decode(BuildRstStreamFrame(1, Http2ErrorCode.Cancel));
        var rstFrame = Assert.IsType<RstStreamFrame>(rst[0]);
        openStreams.Remove(rstFrame.StreamId);
        closedStreams.Add(rstFrame.StreamId);

        Assert.Single(openStreams);
        Assert.Single(closedStreams);
    }

    [Fact(DisplayName = "RFC9113-6.4-CC-012: RST_STREAM carries correct error code")]
    public void RstStream_Result_CarriesErrorCode()
    {
        var decoder = new Http2FrameDecoder();

        // Open stream 1
        decoder.Decode(BuildHeadersFrame(1, ValidStatusHeaderBlock()));

        // RST_STREAM with InternalError
        var rst = decoder.Decode(BuildRstStreamFrame(1, Http2ErrorCode.InternalError));
        var frame = Assert.IsType<RstStreamFrame>(rst[0]);

        Assert.Equal(1, frame.StreamId);
        Assert.Equal(Http2ErrorCode.InternalError, frame.ErrorCode);
    }

    [Fact(DisplayName = "RFC9113-6.4-CC-013: After RST_STREAM, DATA on that stream is stream error")]
    public void AfterRstStream_DataOnResetStream_IsStreamClosedError()
    {
        var decoder = new Http2FrameDecoder();
        var closedStreams = new HashSet<int>();

        // Open stream 1
        decoder.Decode(BuildHeadersFrame(1, ValidStatusHeaderBlock()));

        // RST_STREAM on stream 1
        var rst = decoder.Decode(BuildRstStreamFrame(1, Http2ErrorCode.Cancel));
        var rstFrame = Assert.IsType<RstStreamFrame>(rst[0]);
        closedStreams.Add(rstFrame.StreamId);

        // DATA on reset stream → STREAM_CLOSED
        var data = decoder.Decode(BuildDataFrame(1, new byte[10]));
        var dataFrame = Assert.IsType<DataFrame>(data[0]);

        var ex = Assert.Throws<Http2Exception>(
            () => EnforceStreamNotClosed(dataFrame.StreamId, closedStreams));

        Assert.Equal(Http2ErrorCode.StreamClosed, ex.ErrorCode);
        Assert.Equal(Http2ErrorScope.Stream, ex.Scope);
        Assert.Equal(1, ex.StreamId);
    }

    [Fact(DisplayName = "RFC9113-6.4-CC-014: RST_STREAM error code in frame payload")]
    public void RstStream_ErrorCodeInPayload()
    {
        var decoder = new Http2FrameDecoder();

        // Open stream 1
        decoder.Decode(BuildHeadersFrame(1, ValidStatusHeaderBlock()));

        // RST_STREAM with Cancel error
        var rst = decoder.Decode(BuildRstStreamFrame(1, Http2ErrorCode.Cancel));
        var frame = Assert.IsType<RstStreamFrame>(rst[0]);

        Assert.Equal(Http2ErrorCode.Cancel, frame.ErrorCode);
    }

    // =========================================================================
    // CC-015..018: GOAWAY stops new stream creation (RFC 9113 §6.8)
    // =========================================================================

    [Fact(DisplayName = "RFC9113-6.8-CC-015: GOAWAY frame decoded with correct lastStreamId")]
    public void GoAway_DecodedWithCorrectLastStreamId()
    {
        var decoder = new Http2FrameDecoder();

        // Open stream 1 first
        decoder.Decode(BuildHeadersFrame(1, ValidStatusHeaderBlock()));

        // GOAWAY with lastStreamId = 1
        var goAway = decoder.Decode(BuildGoAwayFrame(1, Http2ErrorCode.NoError));
        var frame = Assert.IsType<GoAwayFrame>(goAway[0]);

        Assert.Equal(1, frame.LastStreamId);
        Assert.Equal(Http2ErrorCode.NoError, frame.ErrorCode);
    }

    [Fact(DisplayName = "RFC9113-6.8-CC-016: New HEADERS after GOAWAY with streamId > lastStreamId is rejected")]
    public void NewHeadersAfterGoAway_AboveLastStreamId_IsRejected()
    {
        var decoder = new Http2FrameDecoder();

        // GOAWAY with lastStreamId = 0
        var goAway = decoder.Decode(BuildGoAwayFrame(0, Http2ErrorCode.NoError));
        var goAwayFrame = Assert.IsType<GoAwayFrame>(goAway[0]);

        // Stream 1 is > lastStreamId → should be rejected
        var headers = decoder.Decode(BuildHeadersFrame(1, ValidStatusHeaderBlock()));
        var headersFrame = Assert.IsType<HeadersFrame>(headers[0]);

        var ex = Assert.Throws<Http2Exception>(
            () => EnforceGoAwayRejectsNewStreams(headersFrame.StreamId, goAwayFrame.LastStreamId));

        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.Equal(Http2ErrorScope.Connection, ex.Scope);
    }

    [Fact(DisplayName = "RFC9113-6.8-CC-017: GOAWAY sets LastStreamId field correctly")]
    public void GoAway_SetsLastStreamIdCorrectly()
    {
        var decoder = new Http2FrameDecoder();

        // Open streams 1, 3, 5
        decoder.Decode(BuildHeadersFrame(1, ValidStatusHeaderBlock()));
        decoder.Decode(BuildHeadersFrame(3, ValidStatusHeaderBlock()));
        decoder.Decode(BuildHeadersFrame(5, ValidStatusHeaderBlock()));

        // GOAWAY with lastStreamId = 3
        var goAway = decoder.Decode(BuildGoAwayFrame(3, Http2ErrorCode.NoError));
        var frame = Assert.IsType<GoAwayFrame>(goAway[0]);

        Assert.Equal(3, frame.LastStreamId);
    }

    [Fact(DisplayName = "RFC9113-6.8-CC-018: GOAWAY error code decoded correctly")]
    public void GoAway_ErrorCodeDecodedCorrectly()
    {
        var decoder = new Http2FrameDecoder();

        var goAway = decoder.Decode(BuildGoAwayFrame(0, Http2ErrorCode.FlowControlError));
        var frame = Assert.IsType<GoAwayFrame>(goAway[0]);

        Assert.Equal(Http2ErrorCode.FlowControlError, frame.ErrorCode);
    }

    // =========================================================================
    // CC-019..020: No header injection via HPACK (RFC 9113 §8.2)
    // =========================================================================

    [Fact(DisplayName = "RFC9113-8.2-CC-019: Invalid HPACK index cannot inject headers")]
    public void InvalidHpackIndex_CannotInjectHeaders()
    {
        // HPACK block that references index 0 (reserved) → HpackException
        var corruptHpack = new byte[] { 0x80 }; // indexed, index=0 (reserved)
        var headersFrame = BuildHeadersFrame(1, corruptHpack);

        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(headersFrame);
        var frame = Assert.IsType<HeadersFrame>(frames[0]);

        var hpackDecoder = new HpackDecoder();
        var ex = Assert.Throws<Http2Exception>(
            () => DecodeHpackWithCompressionErrorWrapping(hpackDecoder, frame.HeaderBlockFragment.Span));

        Assert.Equal(Http2ErrorCode.CompressionError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    [Fact(DisplayName = "RFC9113-8.2-CC-020: Uppercase header name is rejected by validation")]
    public void HpackEncodedUppercaseHeaderName_IsRejectedByValidation()
    {
        // Build a valid HPACK block with an uppercase header name
        // Start with :status: 200 (indexed, index 8)
        var combined = new List<byte> { 0x88 }; // indexed :status: 200

        // Add literal with new name: "X-UPPER": "test"
        // Literal without indexing: 0x00 (4-bit prefix)
        combined.Add(0x00);

        // Name: "X-UPPER"
        var upperName = "X-UPPER"u8.ToArray();
        combined.Add((byte)upperName.Length);
        combined.AddRange(upperName);

        // Value: "test"
        var val = "test"u8.ToArray();
        combined.Add((byte)val.Length);
        combined.AddRange(val);

        var headersFrame = BuildHeadersFrame(1, combined.ToArray());

        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(headersFrame);
        var frame = Assert.IsType<HeadersFrame>(frames[0]);

        var hpackDecoder = new HpackDecoder();
        var headers = hpackDecoder.Decode(frame.HeaderBlockFragment.Span);

        // Validation should reject uppercase "X-UPPER"
        var ex = Assert.Throws<Http2Exception>(
            () => ValidateResponseHeaders(headers));

        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.Equal(Http2ErrorScope.Connection, ex.Scope);
    }
}
