using System.Net;
using TurboHTTP.Protocol.Http2;
using TurboHTTP.Protocol.Http2.Hpack;

namespace TurboHTTP.Tests.Http2.StreamState;

/// <summary>
/// Tests HTTP/2 connection preface encoding and decoding per RFC 9113 §3.4/3.5.
/// Part 3: HEADERS frame, CONTINUATION frame.
/// </summary>
/// <remarks>
/// Class under test: <see cref="FrameDecoder"/>.
/// </remarks>
public sealed class Http2ConnectionPrefacePart3Spec
{
    // RFC 9113 §3.4: client connection preface = magic octets + SETTINGS frame
    private static readonly byte[] Magic = "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"u8.ToArray();
    private const int MagicLength = 24; // "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"
    private const int FrameHeaderLength = 9;

    // Helper: Decode server responses from frame bytes (replaces Http2ProtocolSession.Responses)
    private static List<(int StreamId, HttpResponseMessage Response)> DecodeResponses(ReadOnlyMemory<byte> data)
    {
        var decoder = new FrameDecoder();
        var hpack = new HpackDecoder();
        var frames = decoder.Decode(data);
        var responses = new List<(int, HttpResponseMessage)>();
        var pending = new Dictionary<int, (HttpResponseMessage Response, List<byte> Body)>();

        foreach (var frame in frames)
        {
            switch (frame)
            {
                case HeadersFrame { EndHeaders: true } h:
                {
                    var hdrs = hpack.Decode(h.HeaderBlockFragment.Span);
                    var resp = BuildResponseFromHpack(hdrs);
                    if (resp == null) break;
                    if (h.EndStream) responses.Add((h.StreamId, resp));
                    else pending[h.StreamId] = (resp, []);
                    break;
                }
                case DataFrame d:
                    if (pending.TryGetValue(d.StreamId, out var p))
                        p.Body.AddRange(d.Data.ToArray());
                    if (d.EndStream && pending.TryGetValue(d.StreamId, out var completed))
                    {
                        completed.Response.Content = new ByteArrayContent(completed.Body.ToArray());
                        responses.Add((d.StreamId, completed.Response));
                        pending.Remove(d.StreamId);
                    }

                    break;
            }
        }

        return responses;
    }

    private static HttpResponseMessage? BuildResponseFromHpack(IReadOnlyList<HpackHeader> headers)
    {
        var statusHeader = headers.FirstOrDefault(h => h.Name == ":status");
        if (statusHeader == default || !int.TryParse(statusHeader.Value, out var code)) return null;
        var response = new HttpResponseMessage((HttpStatusCode)code);
        foreach (var h in headers.Where(h => !h.Name.StartsWith(':')))
        {
            if (!response.Headers.TryAddWithoutValidation(h.Name, h.Value))
            {
                response.Content.Headers.TryAddWithoutValidation(h.Name, h.Value);
            }
        }

        return response;
    }

    // HEADERS frame tests (migrated from 12_DecoderConnectionPrefaceTests — Phase 6)

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.2")]
    public void Http2FrameDecoder_should_decode_response_headers_when_headers_frame_received()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200"), ("x-custom", "value")]);
        var frame = new HeadersFrame(1, headerBlock, endStream: true, endHeaders: true).Serialize();

        var responses = DecodeResponses(frame.AsMemory());

        Assert.Single(responses);
        var response = responses[0].Response;
        Assert.Equal(200, (int)response.StatusCode);
        Assert.True(response.Headers.Contains("x-custom"));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.2")]
    public void HeadersFrame_should_close_stream_immediately_when_has_end_stream()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "204")]);
        var frame = new HeadersFrame(1, headerBlock, endStream: true, endHeaders: true).Serialize();

        var responses = DecodeResponses(frame.AsMemory());

        Assert.Single(responses);
        Assert.Equal(204, (int)responses[0].Response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.2")]
    public void HeadersFrame_should_complete_header_block_when_has_end_headers()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200")]);
        var frame = new HeadersFrame(1, headerBlock, endStream: false, endHeaders: true).Serialize();

        var decoder = new FrameDecoder();
        decoder.Decode(frame);

        // If END_HEADERS was respected, a subsequent non-CONTINUATION frame must not throw.
        var pingFrame = new PingFrame(new byte[8]).Serialize();
        var frames = decoder.Decode(pingFrame);
        Assert.Single(frames); // no exception — END_HEADERS was recognised
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.2")]
    public void Http2FrameDecoder_should_strip_padding_when_headers_frame_is_padded()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200")]);

        // Build PADDED HEADERS: PADDED flag=0x08, pad_length=2, header block, 2 bytes padding.
        const int padLength = 2;
        var payload = new byte[1 + headerBlock.Length + padLength];
        payload[0] = padLength; // Pad Length
        headerBlock.CopyTo(payload.AsMemory(1));
        // last 2 bytes remain zero (padding)

        var frame = new byte[9 + payload.Length];
        frame[0] = 0;
        frame[1] = 0;
        frame[2] = (byte)payload.Length;
        frame[3] = 0x01; // HEADERS
        frame[4] = 0x0D; // END_STREAM(0x1) | END_HEADERS(0x4) | PADDED(0x8)
        frame[5] = 0;
        frame[6] = 0;
        frame[7] = 0;
        frame[8] = 1; // stream=1
        payload.CopyTo(frame, 9);

        var responses = DecodeResponses(frame.AsMemory());

        Assert.Single(responses);
        Assert.Equal(200, (int)responses[0].Response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.2")]
    public void HeadersFrame_should_consume_priority_flag_correctly_when_has_priority_flag()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200")]);

        // Build HEADERS with PRIORITY flag: 5 extra bytes (4 stream dep + 1 weight).
        var priorityBytes = new byte[] { 0x00, 0x00, 0x00, 0x03, 0x0F }; // dep=3, weight=15
        var payload = priorityBytes.Concat(headerBlock.ToArray()).ToArray();

        var frame = new byte[9 + payload.Length];
        frame[0] = 0;
        frame[1] = 0;
        frame[2] = (byte)payload.Length;
        frame[3] = 0x01; // HEADERS
        frame[4] = 0x25; // END_STREAM(0x1) | END_HEADERS(0x4) | PRIORITY(0x20)
        frame[5] = 0;
        frame[6] = 0;
        frame[7] = 0;
        frame[8] = 1; // stream=1
        payload.CopyTo(frame, 9);

        var responses = DecodeResponses(frame.AsMemory());

        Assert.Single(responses);
        Assert.Equal(200, (int)responses[0].Response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.2")]
    public void Http2FrameDecoder_should_wait_for_continuation_when_headers_frame_lacks_end_headers()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200")]);
        var split1 = headerBlock[..(headerBlock.Length / 2)];
        var split2 = headerBlock[(headerBlock.Length / 2)..];

        var headersFrame = new HeadersFrame(1, split1, endStream: true, endHeaders: false).Serialize();
        var contFrame = new ContinuationFrame(1, split2, endHeaders: true).Serialize();

        // Use same decoder instance to maintain state
        var decoder = new FrameDecoder();
        var frames1 = decoder.Decode(headersFrame);
        Assert.Single(frames1); // HEADERS frame is decoded

        // Decoder is now awaiting CONTINUATION (continuation state maintained by decoder)
        var frames2 = decoder.Decode(contFrame);
        Assert.Single(frames2); // CONTINUATION frame is decoded
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.2")]
    public void Http2FrameDecoder_should_parse_when_headers_frame_on_stream_0()
    {
        // RFC 9113 §6.2: HEADERS on stream 0 is a connection error.
        // Http2FrameDecoder parses the frame; stream=0 validation happens at session layer.
        var frame = new byte[]
        {
            0x00, 0x00, 0x01,
            0x01, 0x05,
            0x00, 0x00, 0x00, 0x00, // stream=0
            0x88
        };
        var decoder = new FrameDecoder();
        // Decoder parses the frame itself without validation of stream ID constraints
        var frames = decoder.Decode(frame);
        var headersFrame = Assert.IsType<HeadersFrame>(Assert.Single(frames));
        Assert.Equal(0, headersFrame.StreamId);
    }

    // CONTINUATION frame tests (migrated from 12_DecoderConnectionPrefaceTests — Phase 6)

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void Http2FrameDecoder_should_merge_header_block_when_continuation_frame_appended_to_headers()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200"), ("x-test", "cont")]);
        var split = headerBlock.Length / 2;

        var headersFrame = new HeadersFrame(1, headerBlock[..split], endStream: true, endHeaders: false).Serialize();
        var contFrame = new ContinuationFrame(1, headerBlock[split..], endHeaders: true).Serialize();

        // Use same decoder instance to maintain continuation state
        var decoder = new FrameDecoder();
        decoder.Decode(headersFrame);

        var frames = decoder.Decode(contFrame);
        Assert.Single(frames);
        var cont = Assert.IsType<ContinuationFrame>(frames[0]);
        Assert.True(cont.EndHeaders);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void ContinuationFrame_should_complete_block_when_has_end_headers()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200")]);

        var headersFrame = new HeadersFrame(1, headerBlock[..1], endStream: true, endHeaders: false).Serialize();
        var contFrame = new ContinuationFrame(1, headerBlock[1..], endHeaders: true).Serialize();

        var decoder = new FrameDecoder();
        decoder.Decode(headersFrame);
        var frames = decoder.Decode(contFrame);

        Assert.Single(frames);
        var cont = Assert.IsType<ContinuationFrame>(frames[0]);
        Assert.True(cont.EndHeaders);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void Http2FrameDecoder_should_merge_all_when_multiple_continuation_frames()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200"), ("a", "1"), ("b", "2"), ("c", "3")]);
        var third = headerBlock.Length / 3;

        var headersFrame = new HeadersFrame(1, headerBlock[..third], endStream: true, endHeaders: false).Serialize();
        var cont1 = new ContinuationFrame(1, headerBlock[third..(2 * third)], endHeaders: false).Serialize();
        var cont2 = new ContinuationFrame(1, headerBlock[(2 * third)..], endHeaders: true).Serialize();

        var decoder = new FrameDecoder();
        decoder.Decode(headersFrame);
        decoder.Decode(cont1);
        var frames = decoder.Decode(cont2);

        Assert.Single(frames);
        var cont = Assert.IsType<ContinuationFrame>(frames[0]);
        Assert.True(cont.EndHeaders);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void Http2FrameDecoder_should_throw_protocol_error_when_continuation_frame_on_wrong_stream()
    {
        var headersFrame = new byte[]
        {
            0x00, 0x00, 0x01,
            0x01, 0x00,
            0x00, 0x00, 0x00, 0x01, // stream=1
            0x82
        };
        var contFrame = new byte[]
        {
            0x00, 0x00, 0x01,
            0x09, 0x04,
            0x00, 0x00, 0x00, 0x03, // stream=3 (wrong)
            0x84
        };

        var combined = headersFrame.Concat(contFrame).ToArray();
        var decoder = new FrameDecoder();
        var ex = Assert.Throws<Http2Exception>(() => decoder.Decode(combined));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void Http2FrameDecoder_should_throw_protocol_error_when_non_continuation_follows_headers_without_end_headers()
    {
        // RFC 9113 §6.9: After HEADERS without END_HEADERS, next frame MUST be CONTINUATION.
        // Http2FrameDecoder enforces this and throws PROTOCOL_ERROR if violated.
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200")]);
        var headersFrame = new HeadersFrame(1, headerBlock, endStream: false, endHeaders: false).Serialize();
        var pingFrame = new PingFrame(new byte[8]).Serialize();

        var decoder = new FrameDecoder();
        decoder.Decode(headersFrame);

        // PING while awaiting CONTINUATION must be PROTOCOL_ERROR.
        var ex = Assert.Throws<Http2Exception>(() => decoder.Decode(pingFrame));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void Http2FrameDecoder_should_throw_protocol_error_when_continuation_frame_on_stream_0()
    {
        var headersOnStream1 = new byte[]
        {
            0x00, 0x00, 0x01,
            0x01, 0x00,
            0x00, 0x00, 0x00, 0x01, // stream=1
            0x82
        };
        var contOnStream0 = new byte[]
        {
            0x00, 0x00, 0x01,
            0x09, 0x04,
            0x00, 0x00, 0x00, 0x00, // stream=0
            0x84
        };

        var combined = headersOnStream1.Concat(contOnStream0).ToArray();
        var decoder = new FrameDecoder();
        var ex = Assert.Throws<Http2Exception>(() => decoder.Decode(combined));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.10")]
    public void Http2FrameDecoder_should_throw_protocol_error_when_continuation_frame_has_no_preceding_headers()
    {
        var contFrame = new ContinuationFrame(1, new byte[] { 0x88 }, endHeaders: true).Serialize();
        var decoder = new FrameDecoder();
        var ex = Assert.Throws<Http2Exception>(() => decoder.Decode(contFrame));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }
}
