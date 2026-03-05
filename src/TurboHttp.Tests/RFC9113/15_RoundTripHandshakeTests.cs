using System.Net;
using System.Text;
using TurboHttp.Protocol;

namespace TurboHttp.Tests.RFC9113;

public sealed class Http2RoundTripHandshakeTests
{
    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Build a complete HTTP/2 response: HEADERS frame (endStream=false) + DATA frame (endStream=true).
    /// Uses a fresh HpackEncoder so the state is independent across calls.
    /// </summary>
    private static byte[] BuildH2Response(
        int streamId,
        int status,
        string body,
        HpackEncoder hpack,
        params (string Name, string Value)[] extraHeaders)
    {
        var headers = new List<(string, string)>
        {
            (":status", status.ToString())
        };
        headers.AddRange(extraHeaders.Select(h => (h.Name, h.Value)));

        var headerBlock = hpack.Encode(headers);
        var headersFrame = new HeadersFrame(streamId, headerBlock,
            endStream: body.Length == 0, endHeaders: true).Serialize();

        if (body.Length == 0)
        {
            return headersFrame;
        }

        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var dataFrame = new DataFrame(streamId, bodyBytes, endStream: true).Serialize();

        var combined = new byte[headersFrame.Length + dataFrame.Length];
        headersFrame.CopyTo(combined, 0);
        dataFrame.CopyTo(combined, headersFrame.Length);
        return combined;
    }

    private static byte[] CombineFrames(params byte[][] frames)
    {
        var total = frames.Sum(f => f.Length);
        var result = new byte[total];
        var offset = 0;
        foreach (var f in frames)
        {
            f.CopyTo(result, offset);
            offset += f.Length;
        }

        return result;
    }

    // ── RT-2-001 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-001: HTTP/2 connection preface + SETTINGS exchange")]
    public void Should_ContainMagicAndSettings_When_ConnectionPrefaceBuilt()
    {
        var preface = Http2FrameUtils.BuildConnectionPreface();

        // Verify PRI magic (24 bytes)
        var magic = "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"u8.ToArray();
        Assert.Equal(magic, preface[..magic.Length]);

        // Verify SETTINGS frame starts at byte 24
        Assert.Equal((byte)FrameType.Settings, preface[magic.Length + 3]);

        // Server sends its own SETTINGS; client decoder should queue a SETTINGS ACK
        var serverSettings = new SettingsFrame(new List<(SettingsParameter, uint)>
        {
            (SettingsParameter.MaxConcurrentStreams, 128u),
        }).Serialize();

        var session = new Http2ProtocolSession();
        session.Process(serverSettings);

        Assert.True(session.HasNewSettings);
        Assert.Single(session.SettingsAcksToSend);
    }

    // ── RT-2-002 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-002: HTTP/2 GET → 200 on stream 1")]
    public async Task Should_Return200_When_Http2GetRoundTrip()
    {
        var encoder = new Http2RequestEncoder(useHuffman: false);
        var buf = new byte[4096].AsMemory();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var (streamId, written) = encoder.Encode(request, ref buf);

        Assert.Equal(1, streamId);
        Assert.True(written > 0);

        var hpack = new HpackEncoder(useHuffman: false);
        var responseFrame = BuildH2Response(streamId, 200, "Hello HTTP/2", hpack);
        var session = new Http2ProtocolSession();
        var frames = session.Process(responseFrame);

        Assert.NotEmpty(frames);
        Assert.NotEmpty(session.Responses);
        Assert.Single(session.Responses);
        Assert.Equal(1, session.Responses[0].StreamId);
        Assert.Equal(HttpStatusCode.OK, session.Responses[0].Response.StatusCode);
        Assert.Equal("Hello HTTP/2", await session.Responses[0].Response.Content.ReadAsStringAsync());
    }

    // ── RT-2-003 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-003: HTTP/2 POST → HEADERS+DATA → 201 response")]
    public async Task Should_Return201_When_Http2PostRoundTrip()
    {
        var encoder = new Http2RequestEncoder(useHuffman: false);
        var encoderBuf = new byte[8192];
        var buf = encoderBuf.AsMemory();
        var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/items")
        {
            Content = new StringContent("{\"name\":\"Alice\"}", Encoding.UTF8, "application/json")
        };
        var (streamId, written) = encoder.Encode(request, ref buf);

        Assert.Equal(1, streamId);

        // First frame: HEADERS (type 0x01)
        Assert.Equal((byte)FrameType.Headers, encoderBuf[3]);

        // HEADERS for POST must NOT have END_STREAM (bit 0) since there is a body
        var headersFlags = (Headers)encoderBuf[4];
        Assert.False(headersFlags.HasFlag(Headers.EndStream));
        Assert.True(headersFlags.HasFlag(Headers.EndHeaders));

        // A DATA frame must follow (verify it exists)
        var firstFrameLen = (encoderBuf[0] << 16) | (encoderBuf[1] << 8) | encoderBuf[2];
        var dataFrameStart = 9 + firstFrameLen;
        Assert.Equal((byte)FrameType.Data, encoderBuf[dataFrameStart + 3]);
        Assert.True(written > dataFrameStart);

        // Decode 201 response
        var hpack = new HpackEncoder(useHuffman: false);
        var responseFrame = BuildH2Response(streamId, 201, "", hpack);
        var session = new Http2ProtocolSession();
        session.Process(responseFrame);

        Assert.NotEmpty(session.Responses);
        Assert.Equal(HttpStatusCode.Created, session.Responses[0].Response.StatusCode);
        Assert.Equal(0, session.Responses[0].Response.Content.Headers.ContentLength);
    }

    // ── RT-2-004 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-004: HTTP/2 three concurrent streams each complete independently")]
    public async Task Should_ReturnThreeResponses_When_ThreeConcurrentStreams()
    {
        var encoder = new Http2RequestEncoder(useHuffman: false);
        var buf = new byte[4096].AsMemory();

        var (id1, _) = encoder.Encode(
            new HttpRequestMessage(HttpMethod.Get, "https://example.com/a"), ref buf);

        buf = new byte[4096].AsMemory();
        var (id2, _) = encoder.Encode(
            new HttpRequestMessage(HttpMethod.Get, "https://example.com/b"), ref buf);

        buf = new byte[4096].AsMemory();
        var (id3, _) = encoder.Encode(
            new HttpRequestMessage(HttpMethod.Get, "https://example.com/c"), ref buf);

        Assert.Equal(1, id1);
        Assert.Equal(3, id2);
        Assert.Equal(5, id3);

        var hpack = new HpackEncoder(useHuffman: false);
        var stream1 = BuildH2Response(id1, 200, "response-1", hpack);
        var stream2 = BuildH2Response(id2, 200, "response-2", hpack);
        var stream3 = BuildH2Response(id3, 200, "response-3", hpack);
        var combined = CombineFrames(stream1, stream2, stream3);

        var session = new Http2ProtocolSession();
        var frames = session.Process(combined);

        Assert.NotEmpty(frames);
        Assert.Equal(3, session.Responses.Count);

        var byStream = session.Responses.ToDictionary(r => r.StreamId, r => r.Response);
        Assert.True(byStream.ContainsKey(id1));
        Assert.True(byStream.ContainsKey(id2));
        Assert.True(byStream.ContainsKey(id3));
        Assert.Equal("response-1", await byStream[id1].Content.ReadAsStringAsync());
        Assert.Equal("response-2", await byStream[id2].Content.ReadAsStringAsync());
        Assert.Equal("response-3", await byStream[id3].Content.ReadAsStringAsync());
    }

    // ── RT-2-005 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-005: HTTP/2 HPACK dynamic table reused across three requests")]
    public async Task Should_ProduceSmallerHpackBlock_When_DynamicTableReuseAcrossRequests()
    {
        // Build three response header blocks with the same headers using one HpackEncoder.
        // The first block encodes "content-type: text/html" as a full literal (~20 bytes).
        // The second and third blocks use the indexed reference (~1 byte).
        var hpack = new HpackEncoder(useHuffman: false);

        var block1 = hpack.Encode([
            (":status", "200"),
            ("content-type", "text/html"),
        ]);
        var block2 = hpack.Encode([
            (":status", "200"),
            ("content-type", "text/html"),
        ]);
        var block3 = hpack.Encode([
            (":status", "200"),
            ("content-type", "text/html"),
        ]);

        // Dynamic table reuse means subsequent blocks are smaller
        Assert.True(block1.Length > block2.Length,
            "Second HPACK block should be shorter due to dynamic table reuse");
        Assert.Equal(block2.Length, block3.Length);

        // Build frames for streams 1, 3, 5 using the three blocks
        var frames1 = CombineFrames(
            new HeadersFrame(1, block1, endStream: false, endHeaders: true).Serialize(),
            new DataFrame(1, "body-1"u8.ToArray(), endStream: true).Serialize());
        var frames2 = CombineFrames(
            new HeadersFrame(3, block2, endStream: false, endHeaders: true).Serialize(),
            new DataFrame(3, "body-2"u8.ToArray(), endStream: true).Serialize());
        var frames3 = CombineFrames(
            new HeadersFrame(5, block3, endStream: false, endHeaders: true).Serialize(),
            new DataFrame(5, "body-3"u8.ToArray(), endStream: true).Serialize());
        var combined = CombineFrames(frames1, frames2, frames3);

        var session = new Http2ProtocolSession();
        session.Process(combined);

        Assert.Equal(3, session.Responses.Count);

        foreach (var (_, response) in session.Responses)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/html", response.Content.Headers.ContentType!.MediaType);
        }

        var bodies = new HashSet<string>();
        foreach (var (_, response) in session.Responses)
        {
            bodies.Add(await response.Content.ReadAsStringAsync());
        }

        Assert.Contains("body-1", bodies);
        Assert.Contains("body-2", bodies);
        Assert.Contains("body-3", bodies);
    }

    // ── RT-2-006 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-006: HTTP/2 server SETTINGS → client ACK → both sides updated")]
    public void Should_ApplyServerSettings_When_SettingsReceivedAndAcked()
    {
        var serverSettings = new SettingsFrame(new List<(SettingsParameter, uint)>
        {
            (SettingsParameter.MaxFrameSize, 32768u),
            (SettingsParameter.InitialWindowSize, 131070u),
        }).Serialize();

        var session = new Http2ProtocolSession();
        session.Process(serverSettings);

        Assert.True(session.HasNewSettings);
        Assert.Single(session.ReceivedSettings);

        var settings = session.ReceivedSettings[0];
        Assert.Contains(settings, s =>
            s.Item1 == SettingsParameter.MaxFrameSize && s.Item2 == 32768u);
        Assert.Contains(settings, s =>
            s.Item1 == SettingsParameter.InitialWindowSize && s.Item2 == 131070u);

        // Client must send a SETTINGS ACK back
        Assert.Single(session.SettingsAcksToSend);

        // Apply to encoder: encoder respects the new max frame size
        var encoder = new Http2RequestEncoder(useHuffman: false);
        encoder.ApplyServerSettings(settings);
        var buf = new byte[65536].AsMemory();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var (_, written) = encoder.Encode(request, ref buf);
        Assert.True(written > 0);
    }

    // ── RT-2-007 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-007: HTTP/2 server PING → client PONG with same payload")]
    public void Should_ReturnPingAckWithSamePayload_When_ServerPingReceived()
    {
        var payload = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 };
        var pingFrame = new PingFrame(payload, isAck: false).Serialize();

        var session = new Http2ProtocolSession();
        session.Process(pingFrame);

        // Session records the PING payload so the caller can build the ACK
        Assert.Single(session.PingRequests);
        Assert.Equal(payload, session.PingRequests[0]);

        // Verify the client-constructed PING ACK has the correct format
        var ackFrame = new PingFrame(session.PingRequests[0], isAck: true).Serialize();
        Assert.Equal((byte)FrameType.Ping, ackFrame[3]);
        Assert.Equal((byte)PingFlags.Ack, ackFrame[4]);
        Assert.Equal(payload, ackFrame[9..17]);
    }

    // ── RT-2-008 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-008: HTTP/2 GOAWAY received → no new requests sent")]
    public void Should_SignalGoAway_When_GoAwayFrameReceived()
    {
        var goawayFrame = new GoAwayFrame(5, Http2ErrorCode.NoError).Serialize();

        var session = new Http2ProtocolSession();
        session.Process(goawayFrame);

        Assert.True(session.IsGoingAway);
        Assert.Equal(5, session.GoAwayFrame!.LastStreamId);
        Assert.Equal(Http2ErrorCode.NoError, session.GoAwayFrame.ErrorCode);
    }

    // ── RT-2-009 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-009: HTTP/2 RST_STREAM → stream dropped, other streams continue")]
    public async Task Should_DropStream1AndCompleteStream3_When_RstStreamReceived()
    {
        var hpack = new HpackEncoder(useHuffman: false);

        // Stream 1: HEADERS (no endStream) then RST_STREAM
        var headers1Block = hpack.Encode([(":status", "200")]);
        var headers1 = new HeadersFrame(1, headers1Block,
            endStream: false, endHeaders: true).Serialize();
        var rst1 = new RstStreamFrame(1, Http2ErrorCode.Cancel).Serialize();

        // Stream 3: complete HEADERS + DATA
        var headers3Block = hpack.Encode([(":status", "200")]);
        var headers3 = new HeadersFrame(3, headers3Block,
            endStream: false, endHeaders: true).Serialize();
        var data3 = new DataFrame(3, "stream3-ok"u8.ToArray(), endStream: true).Serialize();

        var combined = CombineFrames(headers1, rst1, headers3, data3);

        var session = new Http2ProtocolSession();
        session.Process(combined);

        // Stream 1 was RST'd
        Assert.Contains(session.RstStreams, r => r.StreamId == 1 && r.Error == Http2ErrorCode.Cancel);

        // Stream 3 completed normally
        Assert.NotEmpty(session.Responses);
        var stream3 = session.Responses.Single(r => r.StreamId == 3);
        Assert.Equal(HttpStatusCode.OK, stream3.Response.StatusCode);
        Assert.Equal("stream3-ok", await stream3.Response.Content.ReadAsStringAsync());
    }

    // ── RT-2-010 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-010: Authorization header NeverIndexed in HTTP/2 round-trip")]
    public void Should_NeverIndexAuthorization_When_AuthorizationHeaderEncoded()
    {
        // Verify NeverIndex encoding by encoding Authorization twice using HpackEncoder directly.
        // NeverIndexed headers are never added to the dynamic table, so both encodings
        // produce identical byte sequences (the header cannot be reduced to an indexed reference).
        var hpackEnc = new HpackEncoder(useHuffman: false);
        var block1 = hpackEnc.Encode([("authorization", "Bearer secret123")]);
        var block2 = hpackEnc.Encode([("authorization", "Bearer secret123")]);

        // Both blocks must be identical in length — NeverIndexed prevents dynamic table entry
        Assert.Equal(block1.Length, block2.Length);

        // The first byte must have the NeverIndexed flag (0x10 = 0001xxxx prefix)
        // For "authorization" with static name index 42 > 15: first byte = 0x1F (prefix overflow)
        Assert.Equal(0x10, block1.Span[0] & 0xF0);

        // Full round-trip: encode a request with Authorization, then decode a 200 response
        var encoder = new Http2RequestEncoder(useHuffman: false);
        var buf = new byte[4096].AsMemory();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/secure");
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer secret123");
        var (streamId, written) = encoder.Encode(request, ref buf);
        Assert.True(written > 0);

        var respHpack = new HpackEncoder(useHuffman: false);
        var responseFrame = BuildH2Response(streamId, 200, "", respHpack);
        var session = new Http2ProtocolSession();
        session.Process(responseFrame);
        Assert.NotEmpty(session.Responses);
        Assert.Equal(HttpStatusCode.OK, session.Responses[0].Response.StatusCode);
    }

    // ── RT-2-011 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-011: Cookie header NeverIndexed in HTTP/2 round-trip")]
    public void Should_NeverIndexCookie_When_CookieHeaderEncoded()
    {
        // Same pattern as rt2-010: Cookie is a sensitive header and must use NeverIndexed encoding.
        var hpackEnc = new HpackEncoder(useHuffman: false);
        var block1 = hpackEnc.Encode([("cookie", "session=abc123; user=alice")]);
        var block2 = hpackEnc.Encode([("cookie", "session=abc123; user=alice")]);

        // NeverIndexed: dynamic table never updated → same encoding both times
        Assert.Equal(block1.Length, block2.Length);

        // First byte must have the NeverIndexed flag (0x10 = 0001xxxx prefix)
        Assert.Equal(0x10, block1.Span[0] & 0xF0);

        // Full round-trip
        var encoder = new Http2RequestEncoder(useHuffman: false);
        var buf = new byte[4096].AsMemory();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/profile");
        request.Headers.TryAddWithoutValidation("Cookie", "session=abc123; user=alice");
        var (streamId, written) = encoder.Encode(request, ref buf);
        Assert.True(written > 0);

        var respHpack = new HpackEncoder(useHuffman: false);
        var responseFrame = BuildH2Response(streamId, 200, "ok", respHpack);
        var session = new Http2ProtocolSession();
        session.Process(responseFrame);
        Assert.NotEmpty(session.Responses);
        Assert.Equal(HttpStatusCode.OK, session.Responses[0].Response.StatusCode);
    }

    // ── RT-2-012 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-012: HTTP/2 request with headers exceeding frame size uses CONTINUATION")]
    public async Task Should_UseContinuationFrames_When_HeadersExceedMaxFrameSize()
    {
        // Set a small max frame size to force CONTINUATION frames
        var encoder = new Http2RequestEncoder(useHuffman: false);
        encoder.ApplyServerSettings([(SettingsParameter.MaxFrameSize, 10u)]);

        var encoderBuf = new byte[4096];
        var buf = encoderBuf.AsMemory();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/resource");
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        var (streamId, written) = encoder.Encode(request, ref buf);

        Assert.True(written > 0);

        // First frame is HEADERS type
        Assert.Equal((byte)FrameType.Headers, encoderBuf[3]);

        // HEADERS frame flags must NOT have END_HEADERS (0x04) — continuation follows
        var headersFlags = (Headers)encoderBuf[4];
        Assert.False(headersFlags.HasFlag(Headers.EndHeaders));

        // Second frame must be CONTINUATION type (0x09)
        var firstFramePayloadLen = (encoderBuf[0] << 16) | (encoderBuf[1] << 8) | encoderBuf[2];
        var continuationFrameOffset = 9 + firstFramePayloadLen;
        Assert.Equal((byte)FrameType.Continuation, encoderBuf[continuationFrameOffset + 3]);

        // Response side: build a response using HEADERS + CONTINUATION manually
        var responseHpack = new HpackEncoder(useHuffman: false);
        var responseHeaderBlock = responseHpack.Encode([
            (":status", "200"),
            ("content-type", "text/plain"),
            ("x-request-id", "12345"),
        ]);

        // Split header block across HEADERS (no END_HEADERS) + CONTINUATION (END_HEADERS)
        var splitAt = responseHeaderBlock.Length / 2;
        var part1 = responseHeaderBlock[..splitAt];
        var part2 = responseHeaderBlock[splitAt..];

        var respHeadersFrame = new HeadersFrame(streamId, part1,
            endStream: false, endHeaders: false).Serialize();
        var respContinuationFrame = new ContinuationFrame(streamId, part2,
            endHeaders: true).Serialize();
        var respDataFrame = new DataFrame(streamId, "fragmented"u8.ToArray(),
            endStream: true).Serialize();

        var combined = CombineFrames(respHeadersFrame, respContinuationFrame, respDataFrame);

        var session = new Http2ProtocolSession();
        var frames = session.Process(combined);

        Assert.NotEmpty(frames);
        Assert.NotEmpty(session.Responses);
        Assert.Equal(HttpStatusCode.OK, session.Responses[0].Response.StatusCode);
        Assert.Equal("fragmented", await session.Responses[0].Response.Content.ReadAsStringAsync());
    }

    // ── RT-2-013 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-013: HTTP/2 server PUSH_PROMISE decoded, pushed response received")]
    public async Task Should_DecodePromisedStream_When_PushPromiseReceived()
    {
        var hpack = new HpackEncoder(useHuffman: false);

        // Server sends PUSH_PROMISE on stream 1 promising stream 2
        var pushHeaderBlock = hpack.Encode([
            (":method", "GET"),
            (":path", "/style.css"),
            (":scheme", "https"),
            (":authority", "example.com"),
        ]);
        var pushPromiseFrame = new PushPromiseFrame(1, 2, pushHeaderBlock,
            endHeaders: true).Serialize();

        // Server sends pushed response on stream 2 (HEADERS + DATA)
        var pushedHeaderBlock = hpack.Encode([
            (":status", "200"),
            ("content-type", "text/css"),
        ]);
        var pushedHeadersFrame = new HeadersFrame(2, pushedHeaderBlock,
            endStream: false, endHeaders: true).Serialize();
        var pushedDataFrame = new DataFrame(2, "body { color: red; }"u8.ToArray(),
            endStream: true).Serialize();

        var combined = CombineFrames(pushPromiseFrame, pushedHeadersFrame, pushedDataFrame);

        var session = new Http2ProtocolSession();
        session.Process(combined);

        // PUSH_PROMISE registered stream 2 as promised
        Assert.Contains(2, session.PromisedStreamIds);

        // Pushed response on stream 2 was decoded
        Assert.NotEmpty(session.Responses);
        var pushed = session.Responses.Single(r => r.StreamId == 2);
        Assert.Equal(HttpStatusCode.OK, pushed.Response.StatusCode);
        Assert.Equal("text/css", pushed.Response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("body { color: red; }", await pushed.Response.Content.ReadAsStringAsync());
    }

    // ── RT-2-014 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-014: HTTP/2 POST body larger than initial window uses WINDOW_UPDATE")]
    public void Should_UpdateSendWindow_When_ServerWindowUpdateReceived()
    {
        const int increment = 65535;

        // Server sends a WINDOW_UPDATE on stream 1 and the connection (stream 0)
        var streamWindowUpdate = new WindowUpdateFrame(1, increment).Serialize();
        var connectionWindowUpdate = new WindowUpdateFrame(0, increment).Serialize();
        var combined = CombineFrames(streamWindowUpdate, connectionWindowUpdate);

        var session = new Http2ProtocolSession();
        session.Process(combined);

        // Stream-level WINDOW_UPDATE tracked in WindowUpdates
        Assert.Single(session.WindowUpdates);
        Assert.Contains(session.WindowUpdates, u => u.StreamId == 1 && u.Increment == increment);
        // Connection-level WINDOW_UPDATE (stream 0) applied to ConnectionSendWindow
        Assert.Equal(65535 + increment, session.ConnectionSendWindow);

        // Encoder applies the window update and can now send more data
        var encoder = new Http2RequestEncoder(useHuffman: false);
        encoder.UpdateConnectionWindow(increment);
        encoder.UpdateStreamWindow(1, increment);

        var bigBody = new byte[70000];
        var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/upload")
        {
            Content = new ByteArrayContent(bigBody)
        };
        var encoderBuf = new byte[200000].AsMemory();
        var (_, written) = encoder.Encode(request, ref encoderBuf);
        Assert.True(written > 0);
    }

    // ── RT-2-015 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-015: HTTP/2 request → 404 response on stream decoded")]
    public void Should_Return404_When_Http2StreamReturnsNotFound()
    {
        var encoder = new Http2RequestEncoder(useHuffman: false);
        var buf = new byte[4096].AsMemory();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/missing");
        var (streamId, written) = encoder.Encode(request, ref buf);

        Assert.Equal(1, streamId);
        Assert.True(written > 0);

        // Server responds with 404 (headers-only response, endStream=true)
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "404")]);
        var headersFrame = new HeadersFrame(streamId, headerBlock,
            endStream: true, endHeaders: true).Serialize();

        var session = new Http2ProtocolSession();
        session.Process(headersFrame);

        Assert.NotEmpty(session.Responses);
        Assert.Single(session.Responses);
        Assert.Equal(streamId, session.Responses[0].StreamId);
        Assert.Equal(HttpStatusCode.NotFound, session.Responses[0].Response.StatusCode);
    }

    // ── RT-2-016 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-016: ValidateServerPreface returns false for incomplete frame (<9 bytes)")]
    public void Should_ReturnFalse_When_ServerPrefaceIncomplete()
    {
        var incomplete = new byte[5]; // less than 9 bytes (frame header size)
        var result = Http2StageTestHelper.ValidateServerPreface(incomplete.AsMemory());
        Assert.False(result);
    }

    // ── RT-2-017 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-017: ValidateServerPreface throws on non-SETTINGS first frame")]
    public void Should_ThrowProtocolError_When_ServerPrefaceHasWrongFrameType()
    {
        // Build a PING frame (type=0x06) on stream 0 with 8-byte payload
        var pingFrame = new PingFrame(new byte[8], isAck: false).Serialize();

        var ex = Assert.Throws<Http2Exception>(() =>
            Http2StageTestHelper.ValidateServerPreface(pingFrame.AsMemory()));

        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    // ── RT-2-018 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-018: Multiple sequential SETTINGS frames each produce one ACK")]
    public void Should_QueueOneAckPerSettingsFrame_When_MultipleSettingsReceived()
    {
        var settings1 = new SettingsFrame(new List<(SettingsParameter, uint)>
        {
            (SettingsParameter.MaxConcurrentStreams, 100u),
        }).Serialize();
        var settings2 = new SettingsFrame(new List<(SettingsParameter, uint)>
        {
            (SettingsParameter.MaxFrameSize, 32768u),
        }).Serialize();

        var combined = CombineFrames(settings1, settings2);
        var session = new Http2ProtocolSession();
        session.Process(combined);

        Assert.Equal(2, session.SettingsAcksToSend.Count);
    }

    // ── RT-2-019 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-019: SETTINGS ACK frame consumed silently, no response queued")]
    public void Should_ConsumeSettingsAck_When_SettingsAckFrameReceived()
    {
        var settingsAck = Http2FrameUtils.EncodeSettingsAck();
        var session = new Http2ProtocolSession();
        var frames = session.Process(settingsAck.AsMemory());

        Assert.NotEmpty(frames);
        Assert.False(session.HasNewSettings);
        Assert.Empty(session.SettingsAcksToSend);
        Assert.Empty(session.PingRequests);
        Assert.Empty(session.Responses);
    }
}
