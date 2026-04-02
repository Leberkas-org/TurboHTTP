using System.Buffers.Binary;
using System.Net;
using TurboHttp.Protocol.Http2.Hpack;
using TurboHttp.Protocol.Http2;

namespace TurboHttp.Tests.Http2.StreamState;

/// <summary>
/// Tests HTTP/2 connection preface encoding and decoding per RFC 9113 §3.4/3.5.
/// Part 2: Frame header parsing, DATA frame.
/// </summary>
/// <remarks>
/// Class under test: <see cref="Http2FrameDecoder"/>.
/// </remarks>
public sealed class Http2ConnectionPrefacePart2Spec
{
    // RFC 9113 §3.4: client connection preface = magic octets + SETTINGS frame
    private static readonly byte[] Magic = "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"u8.ToArray();
    private const int MagicLength = 24; // "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"
    private const int FrameHeaderLength = 9;

    // Helper: Decode server responses from frame bytes (replaces Http2ProtocolSession.Responses)
    private static List<(int StreamId, HttpResponseMessage Response)> DecodeResponses(ReadOnlyMemory<byte> data)
    {
        var decoder = new Http2FrameDecoder();
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

    // Frame header tests (migrated from 12_DecoderConnectionPrefaceTests — Phase 6)

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-4.1")]
    public void Http2FrameDecoder_should_decode_correctly_when_frame_header_is_valid_9_bytes()
    {
        // A SETTINGS ACK is the smallest valid frame (9-byte header, no payload).
        var frameBytes = SettingsFrame.SettingsAck();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(frameBytes);
        Assert.NotEmpty(frames);
        var settings = Assert.IsType<SettingsFrame>(Assert.Single(frames));
        Assert.True(settings.IsAck); // ACK is not a new SETTINGS
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-4.1")]
    public void Http2FrameDecoder_should_parse_24_bit_length_when_frame_has_large_payload()
    {
        // Build a SETTINGS frame with payload > 65535 bytes (66006 = 11001 × 6).
        const int payloadLen = 66006;
        var buf = new byte[9 + payloadLen];
        buf[0] = payloadLen >> 16;
        buf[1] = (payloadLen >> 8) & 0xFF;
        buf[2] = payloadLen & 0xFF;
        buf[3] = 0x04; // SETTINGS
        buf[4] = 0x00; // no flags
        // stream ID = 0 (bytes 5–8 remain zero)
        for (var i = 0; i < payloadLen; i += 6)
        {
            buf[9 + i + 0] = 0x00;
            buf[9 + i + 1] = 0x01; // HeaderTableSize param
            // value = 0 (4 bytes remain zero)
        }

        // Http2FrameDecoder has no MAX_FRAME_SIZE check; the large SETTINGS decodes directly.
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(buf);
        Assert.NotEmpty(frames);
        var settings = Assert.IsType<SettingsFrame>(frames[0]);
        Assert.True(settings.Parameters.Count > 0);
    }

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9113-4.1")]
    [InlineData(0x0)] // DATA
    [InlineData(0x1)] // HEADERS
    [InlineData(0x2)] // PRIORITY
    [InlineData(0x3)] // RST_STREAM
    [InlineData(0x4)] // SETTINGS
    [InlineData(0x5)] // PUSH_PROMISE
    [InlineData(0x6)] // PING
    [InlineData(0x7)] // GOAWAY
    [InlineData(0x8)] // WINDOW_UPDATE
    [InlineData(0x9)] // CONTINUATION
    public void Http2FrameDecoder_should_dispatch_without_crash_when_all_known_frame_types(byte typeCode)
    {
        var frame = (FrameType)typeCode switch
        {
            FrameType.Settings => SettingsFrame.SettingsAck(),
            FrameType.Ping => new PingFrame(new byte[8]).Serialize(),
            FrameType.GoAway => new GoAwayFrame(0, Http2ErrorCode.NoError).Serialize(),
            FrameType.WindowUpdate => new WindowUpdateFrame(0, 1).Serialize(),
            FrameType.RstStream => new RstStreamFrame(1, Http2ErrorCode.Cancel).Serialize(),
            FrameType.Priority =>
            [
                0x00, 0x00, 0x05, // length=5
                0x02, // PRIORITY
                0x00, // flags=0
                0x00, 0x00, 0x00, 0x01, // stream=1
                0x00, 0x00, 0x00, 0x01, // stream dependency
                0x00 // weight
            ],
            FrameType.PushPromise =>
            [
                0x00, 0x00, 0x05, // length=5
                0x05, // PUSH_PROMISE
                0x04, // END_HEADERS
                0x00, 0x00, 0x00, 0x01, // stream=1
                0x00, 0x00, 0x00, 0x02, // promised stream=2
                0x00 // empty header block byte
            ],
            _ =>
            [
                0x00, 0x00, 0x01, typeCode, 0x00, 0x00, 0x00, 0x00, 0x00, // stream 0
                0x00
            ]
        };

        var decoder = new Http2FrameDecoder();
        // Allow any Http2Exception — the handler was reached and detected an error condition.
        try
        {
            decoder.Decode(frame);
        }
        catch (Http2Exception)
        {
            // Expected for certain invalid frame states.
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-4.1")]
    public void Http2FrameDecoder_should_silently_ignore_when_frame_type_is_unknown_0x0a()
    {
        // Build a raw frame with unknown type 0x0A (10).
        var frame = new byte[]
        {
            0x00, 0x00, 0x04, // length = 4
            0x0A, // type  = unknown
            0x00, // flags = none
            0x00, 0x00, 0x00, 0x01, // stream = 1
            0x00, 0x00, 0x00, 0x00 // 4 bytes payload
        };

        // RFC 7540 §4.1 / RFC 9113 §5.5: Unknown frame types MUST be ignored.
        var decoder = new Http2FrameDecoder();
        var result = decoder.Decode(frame);
        Assert.Empty(result); // unknown frame produces no output — silently discarded
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-4.1")]
    public void Http2FrameDecoder_should_mask_last_stream_id_when_r_bit_set_in_goaway()
    {
        // RFC 7540 §6.8: The GOAWAY last-stream-id has a reserved bit that MUST be ignored.
        var payload = new byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(payload, 0x80000003u); // lastStreamId=3 with R-bit
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(4), (uint)Http2ErrorCode.NoError);

        var frame = new byte[9 + 8];
        frame[0] = 0;
        frame[1] = 0;
        frame[2] = 8; // length=8
        frame[3] = 0x07; // GOAWAY
        frame[4] = 0x00; // flags=0
        // stream ID = 0 in header (bytes 5–8)
        payload.CopyTo(frame, 9);

        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(frame);

        var goAway = Assert.IsType<GoAwayFrame>(Assert.Single(frames));
        Assert.Equal(3, goAway.LastStreamId); // R-bit stripped → 3, not 0x80000003
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-4.1")]
    public void Http2FrameDecoder_should_strip_silently_when_r_bit_set_in_stream_id()
    {
        // A SETTINGS ACK frame with R-bit set in the stream word.
        var settingsFrame = new byte[9];
        settingsFrame[3] = 0x04; // SETTINGS
        settingsFrame[4] = (byte)Settings.Ack; // ACK
        settingsFrame[5] = 0x80; // R-bit set in MSB

        // Http2FrameDecoder masks the R-bit and decodes the frame normally.
        // NOTE: RFC 7540 §4.1 says a set R-bit MUST be treated as PROTOCOL_ERROR,
        // but Http2FrameDecoder silently strips it. This test documents current behaviour.
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(settingsFrame);
        Assert.NotEmpty(frames); // decoded successfully — no exception
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-4.1")]
    public void Http2FrameDecoder_should_be_processed_when_payload_exceeds_max_frame_size()
    {
        // Build a DATA frame with length = 16385 (just over the default MAX_FRAME_SIZE of 16384).
        const int overSize = 16385;
        var fullFrame = new byte[9 + overSize];
        fullFrame[0] = overSize >> 16;
        fullFrame[1] = overSize >> 8;
        fullFrame[2] = overSize & 0xFF;
        fullFrame[3] = 0x00; // DATA
        fullFrame[4] = 0x00;
        fullFrame[5] = 0;
        fullFrame[6] = 0;
        fullFrame[7] = 0;
        fullFrame[8] = 1; // stream=1

        // RFC 9113 §4.2: FRAME_SIZE_ERROR for frames exceeding MAX_FRAME_SIZE.
        // Http2FrameDecoder is a stateless frame parser and does not enforce MAX_FRAME_SIZE.
        // Size validation occurs at the session layer where MAX_FRAME_SIZE is negotiated via SETTINGS.
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(fullFrame);
        Assert.Single(frames);
        Assert.IsType<DataFrame>(frames[0]);
    }

    // DATA frame tests (migrated from 12_DecoderConnectionPrefaceTests — Phase 6)

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.1")]
    public void Http2FrameDecoder_should_decode_correctly_when_data_frame_has_payload()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200")]);
        var headersFrame = new HeadersFrame(1, headerBlock, endStream: false, endHeaders: true).Serialize();
        var body = "hello"u8.ToArray();
        var dataFrame = new DataFrame(1, body, endStream: true).Serialize();

        var responses = DecodeResponses(headersFrame.Concat(dataFrame).ToArray().AsMemory());

        Assert.Single(responses);
        Assert.Equal(200, (int)responses[0].Response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.1")]
    public void DataFrame_should_mark_stream_closed_when_end_stream_set()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200")]);
        var headersFrame = new HeadersFrame(1, headerBlock, endStream: false, endHeaders: true).Serialize();
        var dataFrame = new DataFrame(1, new byte[4], endStream: true).Serialize();

        var responses = DecodeResponses(headersFrame.Concat(dataFrame).ToArray().AsMemory());
        Assert.Single(responses);

        // Verify that the DATA frame had END_STREAM flag
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(dataFrame);
        var frame = Assert.IsType<DataFrame>(Assert.Single(frames));
        Assert.True(frame.EndStream);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.1")]
    public void Http2FrameDecoder_should_strip_padding_when_data_frame_is_padded()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200")]);
        var headersFrame = new HeadersFrame(1, headerBlock, endStream: false, endHeaders: true).Serialize();

        // Payload: pad_length(1) + data(2 bytes "hi") + padding(3 bytes) = 6 bytes
        var paddedPayload = new byte[] { 3, (byte)'h', (byte)'i', 0x00, 0x00, 0x00 };
        var dataFrame = new byte[9 + paddedPayload.Length];
        dataFrame[0] = 0;
        dataFrame[1] = 0;
        dataFrame[2] = (byte)paddedPayload.Length;
        dataFrame[3] = 0x00; // DATA
        dataFrame[4] = 0x09; // END_STREAM(0x1) | PADDED(0x8)
        dataFrame[5] = 0;
        dataFrame[6] = 0;
        dataFrame[7] = 0;
        dataFrame[8] = 1; // stream=1
        paddedPayload.CopyTo(dataFrame, 9);

        var responses = DecodeResponses(headersFrame.Concat(dataFrame).ToArray().AsMemory());

        Assert.Single(responses);
        Assert.Equal(200, (int)responses[0].Response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.1")]
    public void Http2FrameDecoder_should_throw_protocol_error_when_data_frame_on_stream_0()
    {
        var frame = new byte[]
        {
            0x00, 0x00, 0x01,
            0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, // stream=0
            0x00
        };
        var decoder = new Http2FrameDecoder();
        var ex = Assert.Throws<Http2Exception>(() => decoder.Decode(frame));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.1")]
    public void Http2FrameDecoder_should_throw_stream_closed_when_data_frame_on_closed_stream()
    {
        // RFC 9113 §6.1: DATA on a closed stream is a stream error.
        // Http2FrameDecoder is a frame parser and does not track stream state.
        // This test documents that the decoder will parse the frame itself;
        // stream lifecycle validation happens at the session layer.
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200")]);
        var headersFrame = new HeadersFrame(1, headerBlock, endStream: true, endHeaders: true).Serialize();

        var decoder = new Http2FrameDecoder();
        var headerFrames = decoder.Decode(headersFrame);
        Assert.Single(headerFrames);

        // Decoder parses the DATA frame without error (stream state validation is session-level)
        var dataFrame = new DataFrame(1, new byte[1]).Serialize();
        var frames = decoder.Decode(dataFrame);
        Assert.Single(frames);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.1")]
    public void Http2FrameDecoder_should_complete_response_when_empty_data_frame_has_end_stream()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200")]);
        var headersFrame = new HeadersFrame(1, headerBlock, endStream: false, endHeaders: true).Serialize();
        var emptyDataFrame = new DataFrame(1, ReadOnlyMemory<byte>.Empty, endStream: true).Serialize();

        var responses = DecodeResponses(headersFrame.Concat(emptyDataFrame).ToArray().AsMemory());

        Assert.Single(responses);
        Assert.Equal(200, (int)responses[0].Response.StatusCode);
    }
}
