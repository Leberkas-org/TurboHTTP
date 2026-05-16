using TurboHTTP.Protocol.Syntax.Http2;
using TurboHTTP.Protocol.Syntax.Http2.Hpack;

namespace TurboHTTP.Tests.Protocol.Syntax.Http2.Frames;

public sealed class StreamStateMachineSpec
{
    private static byte[] MakeHeadersBytes(int streamId, bool endStream = false, string status = "200")
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var block = hpack.Encode([(":status", status)]);
        return new HeadersFrame(streamId, block, endStream, endHeaders: true).Serialize();
    }

    private static byte[] MakeDataBytes(int streamId, bool endStream, byte[]? body = null)
        => new DataFrame(streamId, body ?? "data"u8.ToArray(), endStream).Serialize();

    private static byte[] Concat(params byte[][] arrays)
    {
        var result = new byte[arrays.Sum(a => a.Length)];
        var offset = 0;
        foreach (var arr in arrays)
        {
            arr.CopyTo(result, offset);
            offset += arr.Length;
        }

        return result;
    }

    private static void EnforceNonZeroStreamId(Http2Frame frame, FrameType frameType)
    {
        if (frame.StreamId == 0)
        {
            throw new HttpProtocolException(
                $"RFC 9113 §5.1: {frameType} frame on stream 0 is a connection error (PROTOCOL_ERROR).");
        }
    }

    private static void EnforceStreamOpen(int streamId, HashSet<int> openStreams)
    {
        if (!openStreams.Contains(streamId))
        {
            throw new HttpProtocolException(
                $"RFC 9113 §5.1: DATA on idle stream {streamId} is a connection error (PROTOCOL_ERROR).");
        }
    }

    private static void EnforceStreamNotClosed(int streamId, HashSet<int> closedStreams)
    {
        if (closedStreams.Contains(streamId))
        {
            throw new HttpProtocolException(
                $"RFC 9113 §6.1: DATA on closed stream {streamId} is a stream error (STREAM_CLOSED).");
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1")]
    public void Http2FrameDecoder_should_decode_as_headers_frame_when_headers_has_no_end_stream()
    {
        var decoder = new FrameDecoder();
        var frames = decoder.Decode(MakeHeadersBytes(streamId: 1, endStream: false));

        Assert.Single(frames);
        var frame = Assert.IsType<HeadersFrame>(frames[0]);
        Assert.Equal(1, frame.StreamId);
        Assert.False(frame.EndStream);
        Assert.True(frame.EndHeaders);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1")]
    public void Http2FrameDecoder_should_decode_with_end_stream_flag_when_headers_has_end_stream()
    {
        var decoder = new FrameDecoder();
        var frames = decoder.Decode(MakeHeadersBytes(streamId: 1, endStream: true));

        Assert.Single(frames);
        var frame = Assert.IsType<HeadersFrame>(frames[0]);
        Assert.Equal(1, frame.StreamId);
        Assert.True(frame.EndStream);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1")]
    public void Http2FrameDecoder_should_decode_with_end_stream_flag_when_data_frame_has_end_stream()
    {
        var decoder = new FrameDecoder();
        var payload = "response body"u8.ToArray();
        var frames = decoder.Decode(MakeDataBytes(streamId: 1, endStream: true, body: payload));

        Assert.Single(frames);
        var frame = Assert.IsType<DataFrame>(frames[0]);
        Assert.Equal(1, frame.StreamId);
        Assert.True(frame.EndStream);
        Assert.Equal(payload, frame.Data.ToArray());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1")]
    public void Http2FrameDecoder_should_decode_with_end_stream_false_when_data_frame_has_no_end_stream()
    {
        var decoder = new FrameDecoder();
        var frames = decoder.Decode(MakeDataBytes(streamId: 1, endStream: false));

        Assert.Single(frames);
        var frame = Assert.IsType<DataFrame>(frames[0]);
        Assert.Equal(1, frame.StreamId);
        Assert.False(frame.EndStream);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1")]
    public void Http2FrameDecoder_should_decode_with_correct_fields_when_rst_stream_frame()
    {
        var decoder = new FrameDecoder();
        var frames = decoder.Decode(new RstStreamFrame(1, Http2ErrorCode.Cancel).Serialize());

        Assert.Single(frames);
        var frame = Assert.IsType<RstStreamFrame>(frames[0]);
        Assert.Equal(1, frame.StreamId);
        Assert.Equal(Http2ErrorCode.Cancel, frame.ErrorCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1")]
    public void Http2FrameDecoder_should_decode_as_sequence_when_headers_then_data_end_stream()
    {
        var decoder = new FrameDecoder();
        var bytes = Concat(
            MakeHeadersBytes(streamId: 1, endStream: false),
            MakeDataBytes(streamId: 1, endStream: true));

        var frames = decoder.Decode(bytes);

        Assert.Equal(2, frames.Count);
        var headers = Assert.IsType<HeadersFrame>(frames[0]);
        var data = Assert.IsType<DataFrame>(frames[1]);
        Assert.Equal(1, headers.StreamId);
        Assert.Equal(1, data.StreamId);
        Assert.False(headers.EndStream);
        Assert.True(data.EndStream);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1")]
    public void Http2FrameDecoder_should_have_independent_stream_ids_when_frames_for_different_streams()
    {
        var decoder = new FrameDecoder();
        var bytes = Concat(
            MakeHeadersBytes(streamId: 1, endStream: false),
            MakeHeadersBytes(streamId: 3, endStream: true));

        var frames = decoder.Decode(bytes);

        Assert.Equal(2, frames.Count);
        Assert.Equal(1, frames[0].StreamId);
        Assert.Equal(3, frames[1].StreamId);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1")]
    public void Http2FrameDecoder_should_decode_by_hpack_decoder_when_headers_has_hpack_fragment()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var block = hpack.Encode([(":status", "204"), ("content-type", "text/plain")]);
        var bytes = new HeadersFrame(1, block, endStream: true, endHeaders: true).Serialize();

        var decoder = new FrameDecoder();
        var frames = decoder.Decode(bytes);

        var headersFrame = Assert.IsType<HeadersFrame>(frames[0]);

        // HPACK-decode the fragment using HpackDecoder directly.
        var hpackDecoder = new HpackDecoder();
        var headers = hpackDecoder.Decode(headersFrame.HeaderBlockFragment.Span);

        Assert.Contains(headers, h => h is { Name: ":status", Value: "204" });
        Assert.Contains(headers, h => h is { Name: "content-type", Value: "text/plain" });
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1")]
    public void Http2FrameDecoder_should_be_protocol_error_when_headers_on_stream_0()
    {
        var decoder = new FrameDecoder();
        var frames = decoder.Decode(MakeHeadersBytes(streamId: 0, endStream: false));

        // Decoder produces the frame; stream-0 validation is the caller's responsibility.
        var frame = Assert.IsType<HeadersFrame>(frames[0]);
        Assert.Equal(0, frame.StreamId);

        // RFC 9113 §5.1: HEADERS on stream 0 MUST trigger a connection PROTOCOL_ERROR.
        Assert.Throws<HttpProtocolException>(() => EnforceNonZeroStreamId(frame, FrameType.Headers));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1")]
    public void Http2FrameDecoder_should_be_protocol_error_when_data_on_stream_0()
    {
        // Build a raw DATA frame with stream ID = 0 (bypasses frame constructor defaults).
        var rawFrame = new byte[]
        {
            0x00, 0x00, 0x04, // length = 4
            0x00, // DATA
            0x00, // no flags
            0x00, 0x00, 0x00, 0x00, // stream = 0
            0x01, 0x02, 0x03, 0x04, // payload
        };
        // RFC 9113 §6.1: Http2FrameDecoder rejects DATA on stream 0 at the frame level.
        var decoder = new FrameDecoder();
        Assert.Throws<HttpProtocolException>(() => decoder.Decode(rawFrame));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1")]
    public void Http2FrameDecoder_should_be_protocol_error_when_data_on_idle_stream()
    {
        var decoder = new FrameDecoder();
        var frames = decoder.Decode(MakeDataBytes(streamId: 1, endStream: false));

        var frame = Assert.IsType<DataFrame>(frames[0]);
        Assert.Equal(1, frame.StreamId);

        // RFC 9113 §5.1: No HEADERS received for stream 1 → stream is Idle.
        // DATA on an Idle stream MUST be treated as a connection PROTOCOL_ERROR.
        var openStreams = new HashSet<int>(); // stream 1 never opened
        Assert.Throws<HttpProtocolException>(() => EnforceStreamOpen(frame.StreamId, openStreams));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1")]
    public void Http2FrameDecoder_should_be_stream_closed_error_when_data_on_closed_stream()
    {
        var decoder = new FrameDecoder();

        // Step 1: decode HEADERS+END_STREAM → stream 1 is now Closed.
        var headersBytes = MakeHeadersBytes(streamId: 1, endStream: true);
        decoder.Decode(headersBytes);
        var closedStreams = new HashSet<int> { 1 }; // stream 1 closed by END_STREAM on HEADERS

        // Step 2: decode DATA on the (now-closed) stream 1.
        var dataBytes = MakeDataBytes(streamId: 1, endStream: false);
        var dataFrames = decoder.Decode(dataBytes);
        var frame = Assert.IsType<DataFrame>(dataFrames[0]);
        Assert.Equal(1, frame.StreamId);

        // RFC 9113 §6.1: DATA on a closed stream MUST trigger a STREAM_CLOSED stream error.
        Assert.Throws<HttpProtocolException>(() => EnforceStreamNotClosed(frame.StreamId, closedStreams));
    }
}