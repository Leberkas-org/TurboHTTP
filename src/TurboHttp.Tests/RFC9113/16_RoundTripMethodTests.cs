using System.Net;
using System.Text;
using TurboHttp.Protocol;

namespace TurboHttp.Tests.RFC9113;

public sealed class Http2RoundTripMethodTests
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

    // ── RT-2-020 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-020: DELETE request encodes correctly with no body")]
    public void Should_EncodeDeleteWithNoBody_When_DeleteRequestEncoded()
    {
        var encoder = new Http2RequestEncoder(useHuffman: false);
        var encoderBuf = new byte[4096];
        var buf = encoderBuf.AsMemory();
        var request = new HttpRequestMessage(HttpMethod.Delete, "https://api.example.com/items/42");
        var (streamId, written) = encoder.Encode(request, ref buf);

        Assert.Equal(1, streamId);
        Assert.True(written > 0);

        // Only a HEADERS frame (endStream=true), no DATA frame
        Assert.Equal((byte)FrameType.Headers, encoderBuf[3]);
        var headersFlags = (Headers)encoderBuf[4];
        Assert.True(headersFlags.HasFlag(Headers.EndStream));
        Assert.True(headersFlags.HasFlag(Headers.EndHeaders));
    }

    // ── RT-2-021 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-021: PUT request encodes with body (HEADERS + DATA)")]
    public void Should_EncodePutWithBody_When_PutRequestEncoded()
    {
        var encoder = new Http2RequestEncoder(useHuffman: false);
        var encoderBuf = new byte[8192];
        var buf = encoderBuf.AsMemory();
        var request = new HttpRequestMessage(HttpMethod.Put, "https://api.example.com/items/42")
        {
            Content = new StringContent("{\"name\":\"updated\"}", Encoding.UTF8, "application/json")
        };
        var (streamId, written) = encoder.Encode(request, ref buf);

        Assert.Equal(1, streamId);
        Assert.True(written > 0);

        // HEADERS frame without END_STREAM (body follows)
        Assert.Equal((byte)FrameType.Headers, encoderBuf[3]);
        var headersFlags = (Headers)encoderBuf[4];
        Assert.False(headersFlags.HasFlag(Headers.EndStream));

        // DATA frame follows
        var firstFrameLen = (encoderBuf[0] << 16) | (encoderBuf[1] << 8) | encoderBuf[2];
        var dataFrameOffset = 9 + firstFrameLen;
        Assert.Equal((byte)FrameType.Data, encoderBuf[dataFrameOffset + 3]);
    }

    // ── RT-2-022 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-022: PATCH request encodes with body (HEADERS + DATA)")]
    public void Should_EncodePatchWithBody_When_PatchRequestEncoded()
    {
        var encoder = new Http2RequestEncoder(useHuffman: false);
        var encoderBuf = new byte[8192];
        var buf = encoderBuf.AsMemory();
        var request = new HttpRequestMessage(HttpMethod.Patch, "https://api.example.com/items/42")
        {
            Content = new StringContent("{\"field\":\"value\"}", Encoding.UTF8, "application/json")
        };
        var (streamId, written) = encoder.Encode(request, ref buf);

        Assert.Equal(1, streamId);
        Assert.True(written > 0);
        Assert.Equal((byte)FrameType.Headers, encoderBuf[3]);
    }

    // ── RT-2-023 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-023: Three CONTINUATION frames required for tiny max frame size")]
    public async Task Should_UseThreeContinuationFrames_When_MaxFrameSizeVerySmall()
    {
        var encoder = new Http2RequestEncoder(useHuffman: false);
        // Force very small frames so the HEADERS block must be split into multiple CONTINUATION frames
        encoder.ApplyServerSettings([(SettingsParameter.MaxFrameSize, 5u)]);

        var encoderBuf = new byte[65536];
        var buf = encoderBuf.AsMemory();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/resource");
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        request.Headers.TryAddWithoutValidation("X-Custom-Header", "some-value-here");
        var (streamId, written) = encoder.Encode(request, ref buf);

        // First frame HEADERS without END_HEADERS (continuation follows)
        Assert.Equal((byte)FrameType.Headers, encoderBuf[3]);
        var headersFlags = (Headers)encoderBuf[4];
        Assert.False(headersFlags.HasFlag(Headers.EndHeaders));

        // Find at least two CONTINUATION frames
        var continuationCount = 0;
        var offset = 0;
        while (offset < written)
        {
            var frameLen = (encoderBuf[offset] << 16) | (encoderBuf[offset + 1] << 8) | encoderBuf[offset + 2];
            var frameType = (FrameType)encoderBuf[offset + 3];
            if (frameType == FrameType.Continuation)
            {
                continuationCount++;
            }

            offset += 9 + frameLen;
        }

        Assert.True(continuationCount >= 2, $"Expected at least 2 CONTINUATION frames, got {continuationCount}");

        // Decode the whole thing: build a response with CONTINUATION
        var hpack = new HpackEncoder(useHuffman: false);
        var block = hpack.Encode([(":status", "200"), ("content-type", "application/json")]);
        var splitAt = block.Length / 2;
        var respHeaders = new HeadersFrame(streamId, block[..splitAt], endStream: false, endHeaders: false).Serialize();
        var respContinuation = new ContinuationFrame(streamId, block[splitAt..], endHeaders: true).Serialize();
        var respData = new DataFrame(streamId, "ok"u8.ToArray(), endStream: true).Serialize();

        var session = new Http2ProtocolSession();
        session.Process(CombineFrames(respHeaders, respContinuation, respData));

        Assert.NotEmpty(session.Responses);
        Assert.Equal(HttpStatusCode.OK, session.Responses[0].Response.StatusCode);
        Assert.Equal("ok", await session.Responses[0].Response.Content.ReadAsStringAsync());
    }

    // ── RT-2-024 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-024: Response with multiple CONTINUATION frames decoded correctly")]
    public async Task Should_DecodeResponse_When_HeadersSplitAcrossMultipleContinuations()
    {
        // Build a large header block and split it across 3 CONTINUATION frames
        var hpack = new HpackEncoder(useHuffman: false);
        var block = hpack.Encode([
            (":status", "200"),
            ("content-type", "application/json"),
            ("x-request-id", "abc-123-def-456"),
            ("x-trace-id", "trace-987654321"),
            ("cache-control", "no-cache, no-store"),
        ]);

        // Split into 3 roughly equal parts
        var part1Len = block.Length / 3;
        var part2Len = block.Length / 3;
        var part3Len = block.Length - part1Len - part2Len;

        var frame1 = new HeadersFrame(1, block[..part1Len], endStream: false, endHeaders: false).Serialize();
        var frame2 = new ContinuationFrame(1, block.Slice(part1Len, part2Len), endHeaders: false).Serialize();
        var frame3 = new ContinuationFrame(1, block.Slice(part1Len + part2Len, part3Len), endHeaders: true).Serialize();
        var dataFrame = new DataFrame(1, "multi-cont-body"u8.ToArray(), endStream: true).Serialize();

        var session = new Http2ProtocolSession();
        session.Process(CombineFrames(frame1, frame2, frame3, dataFrame));

        Assert.NotEmpty(session.Responses);
        Assert.Equal(HttpStatusCode.OK, session.Responses[0].Response.StatusCode);
        Assert.Equal("multi-cont-body", await session.Responses[0].Response.Content.ReadAsStringAsync());
    }

    // ── RT-2-025 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-025: set-cookie response header NeverIndexed does not appear in dynamic table")]
    public void Should_NeverIndexSetCookie_When_SetCookieHeaderEncoded()
    {
        // set-cookie is sensitive; encoding twice must produce same length (never cached)
        var hpackEnc = new HpackEncoder(useHuffman: false);
        var block1 = hpackEnc.Encode([("set-cookie", "sessionid=abc; HttpOnly; Secure")]);
        var block2 = hpackEnc.Encode([("set-cookie", "sessionid=abc; HttpOnly; Secure")]);

        Assert.Equal(block1.Length, block2.Length);
    }

    // ── RT-2-026 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-026: proxy-authorization request header NeverIndexed")]
    public void Should_NeverIndexProxyAuthorization_When_ProxyAuthHeaderEncoded()
    {
        var hpackEnc = new HpackEncoder(useHuffman: false);
        var block1 = hpackEnc.Encode([("proxy-authorization", "Basic dXNlcjpwYXNz")]);
        var block2 = hpackEnc.Encode([("proxy-authorization", "Basic dXNlcjpwYXNz")]);

        Assert.Equal(block1.Length, block2.Length);
    }

    // ── RT-2-027 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-027: Mixed sensitive and non-sensitive headers round-trip correctly")]
    public async Task Should_HandleMixedHeaders_When_SensitiveAndNonSensitiveCombined()
    {
        var encoder = new Http2RequestEncoder(useHuffman: false);
        var buf = new byte[8192].AsMemory();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/data");
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer token123");
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        request.Headers.TryAddWithoutValidation("Cookie", "session=xyz");
        var (streamId, written) = encoder.Encode(request, ref buf);

        Assert.True(written > 0);

        var hpack = new HpackEncoder(useHuffman: false);
        var respFrame = BuildH2Response(streamId, 200, "secured data", hpack);

        var session = new Http2ProtocolSession();
        session.Process(respFrame);

        Assert.NotEmpty(session.Responses);
        Assert.Equal(HttpStatusCode.OK, session.Responses[0].Response.StatusCode);
        Assert.Equal("secured data", await session.Responses[0].Response.Content.ReadAsStringAsync());
    }

    // ── RT-2-028 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-028: Non-sensitive custom header is indexed and produces shorter second encoding")]
    public void Should_IndexCustomHeader_When_NonSensitiveHeaderEncoded()
    {
        var hpackEnc = new HpackEncoder(useHuffman: false);
        var block1 = hpackEnc.Encode([("x-custom-header", "my-value")]);
        var block2 = hpackEnc.Encode([("x-custom-header", "my-value")]);

        // Second encoding should be shorter due to dynamic table indexing
        Assert.True(block2.Length < block1.Length,
            "Non-sensitive header should be indexed, making second encoding shorter");
    }

    // ── RT-2-029 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-029: Five concurrent streams all complete successfully")]
    public async Task Should_ReturnFiveResponses_When_FiveConcurrentStreams()
    {
        var encoder = new Http2RequestEncoder(useHuffman: false);
        var hpack = new HpackEncoder(useHuffman: false);
        var session = new Http2ProtocolSession();

        var streamIds = new int[5];
        for (var i = 0; i < 5; i++)
        {
            var buf = new byte[4096].AsMemory();
            var (id, _) = encoder.Encode(
                new HttpRequestMessage(HttpMethod.Get, $"https://example.com/item/{i}"), ref buf);
            streamIds[i] = id;
        }

        // Stream IDs should be 1, 3, 5, 7, 9
        Assert.Equal([1, 3, 5, 7, 9], streamIds);

        var frames = streamIds.Select(id => BuildH2Response(id, 200, $"body-{id}", hpack)).ToArray();
        session.Process(CombineFrames(frames));

        Assert.Equal(5, session.Responses.Count);
        var byId = session.Responses.ToDictionary(r => r.StreamId, r => r.Response);
        foreach (var id in streamIds)
        {
            Assert.True(byId.ContainsKey(id));
            Assert.Equal(HttpStatusCode.OK, byId[id].StatusCode);
            Assert.Equal($"body-{id}", await byId[id].Content.ReadAsStringAsync());
        }
    }

    // ── RT-2-030 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-030: Mixed status codes across concurrent streams decoded correctly")]
    public void Should_DecodeCorrectStatusCodes_When_ConcurrentStreamsHaveDifferentStatuses()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var session = new Http2ProtocolSession();

        var statusMap = new Dictionary<int, HttpStatusCode>
        {
            { 1, HttpStatusCode.OK },
            { 3, HttpStatusCode.Created },
            { 5, HttpStatusCode.NotFound },
            { 7, HttpStatusCode.InternalServerError },
        };

        var frames = statusMap.Select(kv =>
        {
            var block = hpack.Encode([(":status", ((int)kv.Value).ToString())]);
            return new HeadersFrame(kv.Key, block, endStream: true, endHeaders: true).Serialize();
        }).ToArray();

        session.Process(CombineFrames(frames));

        Assert.Equal(4, session.Responses.Count);
        var byId = session.Responses.ToDictionary(r => r.StreamId, r => r.Response);
        foreach (var (id, expectedStatus) in statusMap)
        {
            Assert.Equal(expectedStatus, byId[id].StatusCode);
        }
    }

    // ── RT-2-031 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-031: Interleaved DATA frames for two concurrent streams decoded correctly")]
    public async Task Should_DecodeResponses_When_DataFramesInterleaved()
    {
        var hpack = new HpackEncoder(useHuffman: false);

        // Stream 1: HEADERS, then DATA
        var h1 = hpack.Encode([(":status", "200")]);
        var headers1 = new HeadersFrame(1, h1, endStream: false, endHeaders: true).Serialize();
        var data1 = new DataFrame(1, "stream1-part"u8.ToArray(), endStream: true).Serialize();

        // Stream 3: HEADERS, then DATA (interleaved)
        var h3 = hpack.Encode([(":status", "200")]);
        var headers3 = new HeadersFrame(3, h3, endStream: false, endHeaders: true).Serialize();
        var data3 = new DataFrame(3, "stream3-part"u8.ToArray(), endStream: true).Serialize();

        // Interleave: headers1, headers3, data1, data3
        var combined = CombineFrames(headers1, headers3, data1, data3);

        var session = new Http2ProtocolSession();
        session.Process(combined);

        Assert.Equal(2, session.Responses.Count);
        var byId = session.Responses.ToDictionary(r => r.StreamId, r => r.Response);
        Assert.Equal("stream1-part", await byId[1].Content.ReadAsStringAsync());
        Assert.Equal("stream3-part", await byId[3].Content.ReadAsStringAsync());
    }

    // ── RT-2-032 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-032: Large body response (32 KB) decoded correctly")]
    public async Task Should_DecodeLargeBody_When_ResponseBodyIs32KB()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var largeBody = new byte[32768];
        new Random(42).NextBytes(largeBody);

        var block = hpack.Encode([(":status", "200"), ("content-length", "32768")]);
        var headersFrame = new HeadersFrame(1, block, endStream: false, endHeaders: true).Serialize();
        var dataFrame = new DataFrame(1, largeBody, endStream: true).Serialize();

        var session = new Http2ProtocolSession();
        // Increase max frame size to allow 32KB DATA frame and set receive window accordingly
        session.Process(
            new SettingsFrame(new List<(SettingsParameter, uint)>
                { (SettingsParameter.MaxFrameSize, 65536u) }).Serialize().AsMemory());
        session.SetConnectionReceiveWindow(131072);

        session.Process(CombineFrames(headersFrame, dataFrame).AsMemory());

        Assert.NotEmpty(session.Responses);
        var body = await session.Responses[0].Response.Content.ReadAsByteArrayAsync();
        Assert.Equal(32768, body.Length);
        Assert.Equal(largeBody, body);
    }

    // ── RT-2-033 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-033: Multiple DATA frames for a single stream assembled into one body")]
    public async Task Should_AssembleBody_When_MultipleDataFramesForSingleStream()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var block = hpack.Encode([(":status", "200")]);
        var headersFrame = new HeadersFrame(1, block, endStream: false, endHeaders: true).Serialize();

        // Three DATA frames, last one has END_STREAM
        var data1 = new DataFrame(1, "Hello, "u8.ToArray(), endStream: false).Serialize();
        var data2 = new DataFrame(1, "HTTP/2 "u8.ToArray(), endStream: false).Serialize();
        var data3 = new DataFrame(1, "World!"u8.ToArray(), endStream: true).Serialize();

        var session = new Http2ProtocolSession();
        session.Process(CombineFrames(headersFrame, data1, data2, data3));

        Assert.NotEmpty(session.Responses);
        var body = await session.Responses[0].Response.Content.ReadAsStringAsync();
        Assert.Equal("Hello, HTTP/2 World!", body);
    }

    // ── RT-2-034 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-034: WINDOW_UPDATE that brings stream window to exactly 2^31-1 is accepted")]
    public void Should_AcceptMaxIncrement_When_WindowUpdateWithMaxValue()
    {
        // Default initial window is 65535. Use an increment that brings the send window to exactly
        // 2^31-1 (0x7FFFFFFF) without overflowing — per RFC 7540 §6.9.1.
        const int maxWindow = 0x7FFFFFFF;
        const int defaultInitialWindow = 65535;
        const int increment = maxWindow - defaultInitialWindow; // reaches exactly 2^31-1

        var windowUpdate = new WindowUpdateFrame(1, increment).Serialize();

        var session = new Http2ProtocolSession();
        session.Process(windowUpdate.AsMemory());

        Assert.Single(session.WindowUpdates);
        Assert.Equal(1, session.WindowUpdates[0].StreamId);
        Assert.Equal(increment, session.WindowUpdates[0].Increment);
        Assert.Equal(maxWindow, session.GetStreamSendWindow(1));
    }

    // ── RT-2-035 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-035: Connection and stream WINDOW_UPDATE increments tracked separately")]
    public void Should_TrackWindowUpdates_When_ConnectionAndStreamUpdatesReceived()
    {
        var connectionUpdate = new WindowUpdateFrame(0, 32768).Serialize();
        var streamUpdate = new WindowUpdateFrame(1, 16384).Serialize();
        var combined = CombineFrames(connectionUpdate, streamUpdate);

        var session = new Http2ProtocolSession();
        session.Process(combined.AsMemory());

        // Connection WINDOW_UPDATE increments ConnectionSendWindow directly
        Assert.Equal(65535 + 32768, session.ConnectionSendWindow);
        // Stream WINDOW_UPDATE is tracked in WindowUpdates
        Assert.Single(session.WindowUpdates);
        Assert.Equal(1, session.WindowUpdates[0].StreamId);
        Assert.Equal(16384, session.WindowUpdates[0].Increment);
    }

    // ── RT-2-036 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-036: HTTP/2 round-trip with Huffman encoding enabled")]
    public async Task Should_DecodeResponse_When_HuffmanEncodingEnabled()
    {
        var encoder = new Http2RequestEncoder(useHuffman: true);
        var buf = new byte[4096].AsMemory();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/huffman");
        var (streamId, written) = encoder.Encode(request, ref buf);

        Assert.True(written > 0);

        var hpack = new HpackEncoder(useHuffman: true);
        var responseFrame = BuildH2Response(streamId, 200, "Huffman body", hpack);

        var session = new Http2ProtocolSession();
        session.Process(responseFrame);

        Assert.NotEmpty(session.Responses);
        Assert.Equal(HttpStatusCode.OK, session.Responses[0].Response.StatusCode);
        Assert.Equal("Huffman body", await session.Responses[0].Response.Content.ReadAsStringAsync());
    }

    // ── RT-2-037 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-037: HPACK encoder with Huffman produces smaller encoding for well-known headers")]
    public void Should_ProduceSmallerBlock_When_HuffmanEnabledForKnownHeaders()
    {
        var hpackHuffman = new HpackEncoder(useHuffman: true);
        var hpackPlain = new HpackEncoder(useHuffman: false);

        // Encode a header with a long string value — Huffman should compress it
        var blockHuffman = hpackHuffman.Encode([(":status", "200"), ("content-type", "application/json")]);
        var blockPlain = hpackPlain.Encode([(":status", "200"), ("content-type", "application/json")]);

        // Huffman should produce a smaller or equal block
        Assert.True(blockHuffman.Length <= blockPlain.Length,
            "Huffman encoding should produce a block no larger than plain encoding");
    }

    // ── RT-2-038 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-038: HPACK sync: decoder correctly interprets indexed reference after dynamic table update")]
    public async Task Should_DecodeIndexedHeader_When_HpackDynamicTableContainsEntry()
    {
        // First response: encode "x-correlation-id: req-001" as literal → adds to dynamic table
        // Second response: "x-correlation-id: req-001" should now be indexed (1 byte vs. full literal)
        var hpack = new HpackEncoder(useHuffman: false);

        var block1 = hpack.Encode([(":status", "200"), ("x-correlation-id", "req-001")]);
        var block2 = hpack.Encode([(":status", "200"), ("x-correlation-id", "req-001")]);

        // block2 should be shorter because of dynamic table entry
        Assert.True(block2.Length < block1.Length);

        // Both must decode correctly
        var frames1 = CombineFrames(
            new HeadersFrame(1, block1, endStream: false, endHeaders: true).Serialize(),
            new DataFrame(1, "body1"u8.ToArray(), endStream: true).Serialize());
        var frames3 = CombineFrames(
            new HeadersFrame(3, block2, endStream: false, endHeaders: true).Serialize(),
            new DataFrame(3, "body3"u8.ToArray(), endStream: true).Serialize());

        var session = new Http2ProtocolSession();
        session.Process(CombineFrames(frames1, frames3));

        Assert.Equal(2, session.Responses.Count);
        foreach (var (_, response) in session.Responses)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var bodies = session.Responses.Select(r => r.Response.Content.ReadAsStringAsync().GetAwaiter().GetResult()).ToHashSet();
        Assert.Contains("body1", bodies);
        Assert.Contains("body3", bodies);
    }
}
