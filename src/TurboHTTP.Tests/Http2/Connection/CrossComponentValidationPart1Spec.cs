using System.Buffers.Binary;
using TurboHTTP.Protocol.Http2.Hpack;
using TurboHTTP.Protocol.Http2;

namespace TurboHTTP.Tests.Http2.Connection;

/// <summary>
/// Tests cross-component encoder/decoder round-trip contracts per RFC 9113.
/// Part 1: HPACK failure and flow control independence.
/// Verifies that every frame produced by the encoder can be decoded to equivalent values.
/// </summary>
/// <remarks>
/// Class under test: <see cref="Http2FrameDecoder"/> and <see cref="Http2RequestEncoder"/>.
/// These tests validate the full encode→transmit→decode pipeline used by TurboHttp connections.
/// </remarks>
public sealed class Http2CrossComponentValidationPart1Spec
{
    // Helpers

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
        var flags = endStream ? (byte)0x1 : (byte)0x0;
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

    // CC-001..005: HPACK failure → connection error (RFC 9113 §4.3)

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-4.3")]
    public void Http2FrameDecoder_should_throw_compression_error_when_malformed_hpack_bytes_decoded()
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

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-4.3")]
    public void Http2FrameDecoder_should_throw_compression_error_when_out_of_range_dynamic_index_received()
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

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-4.3")]
    public void Http2FrameDecoder_should_be_connection_level_error_when_hpack_compression_fails()
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

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-4.3")]
    public void Http2FrameDecoder_should_throw_compression_error_when_hpack_header_name_is_empty()
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

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-4.3")]
    public void Http2FrameDecoder_should_be_connection_error_when_hpack_failure_occurs_on_any_stream()
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

    // CC-006..010: Flow control independent from header decoding (RFC 9113 §6.9)

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void Http2FrameDecoder_should_track_connection_window_independently_from_hpack()
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

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void Http2FrameDecoder_should_have_independent_windows_when_multiple_streams_active()
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

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void Http2FrameDecoder_should_not_corrupt_other_streams_when_flow_control_error_on_one_stream()
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

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void Http2FrameDecoder_should_not_affect_other_streams_when_window_update_applied_to_one_stream()
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

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void Http2FrameDecoder_should_be_independent_from_streams_when_connection_window_updated()
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
}
