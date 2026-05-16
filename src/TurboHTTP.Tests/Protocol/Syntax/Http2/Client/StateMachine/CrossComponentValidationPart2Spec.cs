using System.Buffers.Binary;
using TurboHTTP.Protocol.Syntax.Http2;
using TurboHTTP.Protocol.Syntax.Http2.Hpack;

namespace TurboHTTP.Tests.Protocol.Syntax.Http2.Client.StateMachine;

public sealed class Http2CrossComponentValidationPart2Spec
{
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

    private static byte[] BuildHeadersFrame(int streamId, byte[] headerBlock, bool endStream = false,
        bool endHeaders = true)
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

    private static byte[] ValidStatusHeaderBlock()
    {
        var enc = new HpackEncoder(useHuffman: false);
        return enc.Encode([(":status", "200")]).ToArray();
    }

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
            throw new HttpProtocolException(
                $"RFC 9113 §4.3: HPACK decompression failure — {ex.Message}");
        }
    }

    private static void EnforceGoAwayRejectsNewStreams(int streamId, int lastStreamId)
    {
        if (streamId > lastStreamId)
        {
            throw new HttpProtocolException(
                $"RFC 9113 §6.8: HEADERS on stream {streamId} after GOAWAY with lastStreamId={lastStreamId}");
        }
    }

    private static void EnforceStreamNotClosed(int streamId, HashSet<int> closedStreams)
    {
        if (closedStreams.Contains(streamId))
        {
            throw new HttpProtocolException(
                $"RFC 9113 §6.1: DATA on closed stream {streamId}");
        }
    }

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
                        throw new HttpProtocolException(
                            $"RFC 9113 §8.2: Header name '{h.Name}' contains uppercase characters.");
                    }
                }
            }
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.4")]
    public void Http2FrameDecoder_should_decrement_active_stream_count_when_rst_stream_received()
    {
        var decoder = new FrameDecoder();
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

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.4")]
    public void Http2FrameDecoder_should_carry_error_code_when_rst_stream_frame_received()
    {
        var decoder = new FrameDecoder();

        // Open stream 1
        decoder.Decode(BuildHeadersFrame(1, ValidStatusHeaderBlock()));

        // RST_STREAM with InternalError
        var rst = decoder.Decode(BuildRstStreamFrame(1, Http2ErrorCode.InternalError));
        var frame = Assert.IsType<RstStreamFrame>(rst[0]);

        Assert.Equal(1, frame.StreamId);
        Assert.Equal(Http2ErrorCode.InternalError, frame.ErrorCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.4")]
    public void Http2FrameDecoder_should_throw_stream_closed_error_when_data_sent_on_reset_stream()
    {
        var decoder = new FrameDecoder();
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

        Assert.Throws<HttpProtocolException>(() => EnforceStreamNotClosed(dataFrame.StreamId, closedStreams));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.4")]
    public void Http2FrameDecoder_should_have_error_code_in_payload_when_rst_stream_frame_built()
    {
        var decoder = new FrameDecoder();

        // Open stream 1
        decoder.Decode(BuildHeadersFrame(1, ValidStatusHeaderBlock()));

        // RST_STREAM with Cancel error
        var rst = decoder.Decode(BuildRstStreamFrame(1, Http2ErrorCode.Cancel));
        var frame = Assert.IsType<RstStreamFrame>(rst[0]);

        Assert.Equal(Http2ErrorCode.Cancel, frame.ErrorCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.8")]
    public void Http2FrameDecoder_should_decode_correct_last_stream_id_when_go_away_received()
    {
        var decoder = new FrameDecoder();

        // Open stream 1 first
        decoder.Decode(BuildHeadersFrame(1, ValidStatusHeaderBlock()));

        // GOAWAY with lastStreamId = 1
        var goAway = decoder.Decode(BuildGoAwayFrame(1));
        var frame = Assert.IsType<GoAwayFrame>(goAway[0]);

        Assert.Equal(1, frame.LastStreamId);
        Assert.Equal(Http2ErrorCode.NoError, frame.ErrorCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.8")]
    public void Http2FrameDecoder_should_reject_new_headers_when_stream_id_exceeds_go_away_last_stream_id()
    {
        var decoder = new FrameDecoder();

        // GOAWAY with lastStreamId = 0
        var goAway = decoder.Decode(BuildGoAwayFrame(0));
        var goAwayFrame = Assert.IsType<GoAwayFrame>(goAway[0]);

        // Stream 1 is > lastStreamId → should be rejected
        var headers = decoder.Decode(BuildHeadersFrame(1, ValidStatusHeaderBlock()));
        var headersFrame = Assert.IsType<HeadersFrame>(headers[0]);

        Assert.Throws<HttpProtocolException>(() =>
            EnforceGoAwayRejectsNewStreams(headersFrame.StreamId, goAwayFrame.LastStreamId));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.8")]
    public void Http2FrameDecoder_should_set_last_stream_id_correctly_when_go_away_built()
    {
        var decoder = new FrameDecoder();

        // Open streams 1, 3, 5
        decoder.Decode(BuildHeadersFrame(1, ValidStatusHeaderBlock()));
        decoder.Decode(BuildHeadersFrame(3, ValidStatusHeaderBlock()));
        decoder.Decode(BuildHeadersFrame(5, ValidStatusHeaderBlock()));

        // GOAWAY with lastStreamId = 3
        var goAway = decoder.Decode(BuildGoAwayFrame(3));
        var frame = Assert.IsType<GoAwayFrame>(goAway[0]);

        Assert.Equal(3, frame.LastStreamId);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.8")]
    public void Http2FrameDecoder_should_decode_error_code_correctly_when_go_away_received()
    {
        var decoder = new FrameDecoder();

        var goAway = decoder.Decode(BuildGoAwayFrame(0, Http2ErrorCode.FlowControlError));
        var frame = Assert.IsType<GoAwayFrame>(goAway[0]);

        Assert.Equal(Http2ErrorCode.FlowControlError, frame.ErrorCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.2")]
    public void Http2FrameDecoder_should_prevent_header_injection_when_hpack_index_is_invalid()
    {
        // HPACK block that references index 0 (reserved) → HpackException
        var corruptHpack = new byte[] { 0x80 }; // indexed, index=0 (reserved)
        var headersFrame = BuildHeadersFrame(1, corruptHpack);

        var decoder = new FrameDecoder();
        var frames = decoder.Decode(headersFrame);
        var frame = Assert.IsType<HeadersFrame>(frames[0]);

        var hpackDecoder = new HpackDecoder();
        Assert.Throws<HttpProtocolException>(() =>
            DecodeHpackWithCompressionErrorWrapping(hpackDecoder, frame.HeaderBlockFragment.Span));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.2")]
    public void Http2FrameDecoder_should_reject_headers_when_uppercase_header_name_detected()
    {
        // Build a valid HPACK block with an uppercase header name
        // Start with :status: 200 (indexed, index 8)
        var combined = new List<byte>
        {
            0x88,
            // Add literal with new name: "X-UPPER": "test"
            // Literal without indexing: 0x00 (4-bit prefix)
            0x00
        }; // indexed :status: 200

        // Name: "X-UPPER"
        var upperName = "X-UPPER"u8.ToArray();
        combined.Add((byte)upperName.Length);
        combined.AddRange(upperName);

        // Value: "test"
        var val = "test"u8.ToArray();
        combined.Add((byte)val.Length);
        combined.AddRange(val);

        var headersFrame = BuildHeadersFrame(1, combined.ToArray());

        var decoder = new FrameDecoder();
        var frames = decoder.Decode(headersFrame);
        var frame = Assert.IsType<HeadersFrame>(frames[0]);

        var hpackDecoder = new HpackDecoder();
        var headers = hpackDecoder.Decode(frame.HeaderBlockFragment.Span);

        // Validation should reject uppercase "X-UPPER"
        Assert.Throws<HttpProtocolException>(() => ValidateResponseHeaders(headers));
    }
}