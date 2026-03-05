using System.Net;
using System.Text;
using TurboHttp.Protocol;

namespace TurboHttp.Tests.Integration;

/// <summary>
/// Phase 10: TCP Fragmentation — Systematic Matrix
///
/// Pattern: feed bytes in two slices data[..splitPoint] and data[splitPoint..].
/// For HTTP/1.0 and HTTP/1.1: first call must return false (NeedMoreData).
/// For HTTP/2: first call may return true (frame decoded) but without a complete response,
/// OR false when split mid-frame-header or mid-payload.
/// Second call must yield a complete response.
/// </summary>
public sealed class TcpFragmentationTests
{
    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Minimal HTTP/1.0 200 response with a 5-byte body "hello".
    /// Byte layout:
    ///   [0-16]  "HTTP/1.0 200 OK\r\n"         (17 bytes)
    ///   [17-35] "Content-Length: 5\r\n"        (19 bytes)
    ///   [36-37] "\r\n"                          (2 bytes — empty line)
    ///   [38-42] "hello"                         (5 bytes — body)
    ///   Total: 43 bytes
    /// </summary>
    private static byte[] Raw10() =>
        "HTTP/1.0 200 OK\r\nContent-Length: 5\r\n\r\nhello"u8.ToArray();

    /// <summary>
    /// Minimal HTTP/1.1 200 response with a 5-byte body "hello".
    /// Byte layout identical to Raw10 except version string.
    ///   [0-16]  "HTTP/1.1 200 OK\r\n"         (17 bytes)
    ///   [17-35] "Content-Length: 5\r\n"        (19 bytes)
    ///   [36-37] "\r\n"                          (2 bytes — empty line)
    ///   [38-42] "hello"                         (5 bytes — body)
    ///   Total: 43 bytes
    /// </summary>
    private static byte[] Raw11() =>
        "HTTP/1.1 200 OK\r\nContent-Length: 5\r\n\r\nhello"u8.ToArray();

    /// <summary>
    /// HTTP/1.1 chunked response with a single 10-byte chunk.
    /// Byte layout:
    ///   [0-16]  "HTTP/1.1 200 OK\r\n"              (17 bytes)
    ///   [17-44] "Transfer-Encoding: chunked\r\n"   (28 bytes)
    ///   [45-46] "\r\n"                               (2 bytes — empty line)
    ///   [47-50] "0a\r\n"                             (4 bytes — chunk-size "10")
    ///   [51-62] "0123456789\r\n"                     (12 bytes — chunk data + CRLF)
    ///   [63-67] "0\r\n\r\n"                          (5 bytes — final chunk)
    ///   Total: 68 bytes
    /// </summary>
    private static byte[] Raw11Chunked() =>
        Encoding.ASCII.GetBytes(
            "HTTP/1.1 200 OK\r\n" +
            "Transfer-Encoding: chunked\r\n" +
            "\r\n" +
            "0a\r\n" +
            "0123456789\r\n" +
            "0\r\n\r\n");

    /// <summary>
    /// An empty SETTINGS frame (9-byte header, no parameters).
    /// Byte layout: [length:3=0][type=0x04][flags=0x00][streamId:4=0]
    /// </summary>
    private static byte[] SettingsFrame9() =>
        new Protocol.SettingsFrame(new List<(SettingsParameter, uint)>()).Serialize();

    private static byte[] Combine(params byte[][] parts)
    {
        var total = parts.Sum(p => p.Length);
        var result = new byte[total];
        var offset = 0;
        foreach (var p in parts)
        {
            p.CopyTo(result, offset);
            offset += p.Length;
        }

        return result;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // HTTP/1.0 Fragmentation
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "FRAG-10-001: HTTP/1.0 status-line split at byte 1")]
    public void Should_BufferAndComplete_When_Http10StatusLineSplitAtByte1()
    {
        var data = Raw10();
        var decoder = new Http10Decoder();

        // Feed just 'H' — can't find header end → NeedMoreData
        Assert.False(decoder.TryDecode(data.AsMemory(0, 1), out _));

        // Feed the rest — completes header+body
        Assert.True(decoder.TryDecode(data.AsMemory(1), out var response));
        Assert.Equal(HttpStatusCode.OK, response!.StatusCode);
    }

    [Fact(DisplayName = "FRAG-10-002: HTTP/1.0 status-line split mid-version")]
    public void Should_BufferAndComplete_When_Http10StatusLineSplitMidVersion()
    {
        var data = Raw10();
        // Split at byte 8: "HTTP/1.0" = 8 bytes; first slice ends after the version
        const int split = 8;
        var decoder = new Http10Decoder();

        Assert.False(decoder.TryDecode(data.AsMemory(0, split), out _));
        Assert.True(decoder.TryDecode(data.AsMemory(split), out var response));
        Assert.Equal(HttpStatusCode.OK, response!.StatusCode);
    }

    [Fact(DisplayName = "FRAG-10-003: HTTP/1.0 header name split mid-word")]
    public void Should_BufferAndComplete_When_Http10HeaderNameSplitMidWord()
    {
        var data = Raw10();
        // Status line ends at byte 17. "Content" starts at 17; split after "Cont" (4 chars) → byte 21.
        const int split = 17 + 4; // = 21
        var decoder = new Http10Decoder();

        Assert.False(decoder.TryDecode(data.AsMemory(0, split), out _));
        Assert.True(decoder.TryDecode(data.AsMemory(split), out var response));
        Assert.Equal(HttpStatusCode.OK, response!.StatusCode);
    }

    [Fact(DisplayName = "FRAG-10-004: HTTP/1.0 body split at first byte")]
    public void Should_BufferAndComplete_When_Http10BodySplitAtFirstByte()
    {
        var data = Raw10();
        // Headers = 38 bytes (17 + 19 + 2). Body starts at 38.
        // Split at 39 = headers + first body byte; Content-Length=5 so body incomplete (1 < 5).
        const int split = 39;
        var decoder = new Http10Decoder();

        Assert.False(decoder.TryDecode(data.AsMemory(0, split), out _));
        Assert.True(decoder.TryDecode(data.AsMemory(split), out var response));
        Assert.Equal(HttpStatusCode.OK, response!.StatusCode);
    }

    [Fact(DisplayName = "FRAG-10-005: HTTP/1.0 body split at midpoint")]
    public void Should_BufferAndComplete_When_Http10BodySplitAtMidpoint()
    {
        var data = Raw10();
        // Headers = 38 bytes; body = 5 bytes. Midpoint split = 38+2 = 40 (2 of 5 body bytes).
        const int split = 40;
        var decoder = new Http10Decoder();

        Assert.False(decoder.TryDecode(data.AsMemory(0, split), out _));
        Assert.True(decoder.TryDecode(data.AsMemory(split), out var response));
        Assert.Equal(HttpStatusCode.OK, response!.StatusCode);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // HTTP/1.1 Fragmentation
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "FRAG-11-001: HTTP/1.1 status-line split at byte 1")]
    public void Should_BufferAndComplete_When_Http11StatusLineSplitAtByte1()
    {
        var data = Raw11();
        var decoder = new Http11Decoder();

        Assert.False(decoder.TryDecode(data.AsMemory(0, 1), out _));
        Assert.True(decoder.TryDecode(data.AsMemory(1), out var responses));
        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
    }

    [Fact(DisplayName = "FRAG-11-002: HTTP/1.1 status-line split inside version")]
    public void Should_BufferAndComplete_When_Http11StatusLineSplitMidVersion()
    {
        var data = Raw11();
        // Split at byte 5: "HTTP/" = 5 bytes; version "1.1" not yet received
        const int split = 5;
        var decoder = new Http11Decoder();

        Assert.False(decoder.TryDecode(data.AsMemory(0, split), out _));
        Assert.True(decoder.TryDecode(data.AsMemory(split), out var responses));
        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
    }

    [Fact(DisplayName = "FRAG-11-003: HTTP/1.1 header split at colon")]
    public void Should_BufferAndComplete_When_Http11HeaderSplitAtColon()
    {
        var data = Raw11();
        // Status line = 17 bytes. "Content-Length" = 14 chars. Colon is at byte 17+14 = 31.
        // Sending bytes 0..30 (31 bytes) — header name present but no colon or CRLFCRLF.
        const int split = 17 + 14; // = 31
        var decoder = new Http11Decoder();

        Assert.False(decoder.TryDecode(data.AsMemory(0, split), out _));
        Assert.True(decoder.TryDecode(data.AsMemory(split), out var responses));
        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
    }

    [Fact(DisplayName = "FRAG-11-004: HTTP/1.1 split at first byte of CRLFCRLF")]
    public void Should_BufferAndComplete_When_Http11SplitAtFirstByteOfCrlfCrlf()
    {
        var data = Raw11();
        // Layout: [0-16] status [17-35] Content-Length header [36-37] empty CRLF [38-42] body
        // CRLFCRLF sequence: bytes 34-37. First byte = 34 ('\r' at end of Content-Length header).
        // "Content-Length: 5\r\n" goes from byte 17 to 35; '\r' at 34, '\n' at 35.
        // Sending bytes 0..33 (34 bytes) — no CRLFCRLF terminator present yet.
        const int split = 34;
        var decoder = new Http11Decoder();

        Assert.False(decoder.TryDecode(data.AsMemory(0, split), out _));
        Assert.True(decoder.TryDecode(data.AsMemory(split), out var responses));
        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
    }

    [Fact(DisplayName = "FRAG-11-005: HTTP/1.1 chunk-size line split mid-hex")]
    public void Should_BufferAndComplete_When_Http11ChunkSizeLineSplitMidHex()
    {
        var data = Raw11Chunked();
        // Headers end at byte 46 (17+28+2-1 = 46, i.e. bytes 0..46 is 47 bytes of header).
        // Chunk-size line "0a\r\n" starts at byte 47.
        // Split at 48: first slice includes complete headers + first hex digit '0'.
        // No '\r\n' terminator seen for chunk-size → body incomplete → NeedMoreData.
        const int split = 48;
        var decoder = new Http11Decoder();

        Assert.False(decoder.TryDecode(data.AsMemory(0, split), out _));
        Assert.True(decoder.TryDecode(data.AsMemory(split), out var responses));
        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
    }

    [Fact(DisplayName = "FRAG-11-006: HTTP/1.1 chunk data split mid-content")]
    public void Should_BufferAndComplete_When_Http11ChunkDataSplitMidContent()
    {
        var data = Raw11Chunked();
        // Chunk data starts at byte 51 (17+28+2+4). 10 data bytes. Split after 5 data bytes.
        const int split = 51 + 5; // = 56
        var decoder = new Http11Decoder();

        Assert.False(decoder.TryDecode(data.AsMemory(0, split), out _));
        Assert.True(decoder.TryDecode(data.AsMemory(split), out var responses));
        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
    }

    [Fact(DisplayName = "FRAG-11-007: HTTP/1.1 final 0-chunk split")]
    public void Should_BufferAndComplete_When_Http11FinalChunkSplit()
    {
        var data = Raw11Chunked();
        // Final chunk "0\r\n\r\n" starts at byte 63 (17+28+2+4+12). Split after "0\r" (2 bytes).
        const int split = 63 + 2; // = 65
        var decoder = new Http11Decoder();

        Assert.False(decoder.TryDecode(data.AsMemory(0, split), out _));
        Assert.True(decoder.TryDecode(data.AsMemory(split), out var responses));
        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
    }

    [Fact(DisplayName = "FRAG-11-008: HTTP/1.1 response delivered 1 byte at a time")]
    public async System.Threading.Tasks.Task Should_AssembleComplete_When_Http11DeliveredOneByteAtATime()
    {
        // Use a short response: "HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nhi" = 41 bytes
        var data = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nhi"u8.ToArray();
        var decoder = new Http11Decoder();

        // Feed every byte except the last; each call must return false
        for (var i = 0; i < data.Length - 1; i++)
        {
            var got = decoder.TryDecode(data.AsMemory(i, 1), out var partial);
            Assert.False(got, $"Expected NeedMoreData after byte {i} but got decoded response");
            Assert.Empty(partial);
        }

        // Final byte completes the response
        Assert.True(decoder.TryDecode(data.AsMemory(data.Length - 1, 1), out var responses));
        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
        Assert.Equal("hi", await responses[0].Content.ReadAsStringAsync());
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // HTTP/2 Fragmentation
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "FRAG-2-001: HTTP/2 frame header split at byte 1")]
    public void Should_ReturnFalse_When_Http2FrameHeaderSplitAtByte1()
    {
        var frame = SettingsFrame9(); // 9 bytes total
        var session = new Http2ProtocolSession();

        // 1 byte < 9-byte frame header → NeedMoreData
        Assert.Empty(session.Process(frame.AsMemory(0, 1)));

        // Feed remaining 8 bytes → complete SETTINGS frame decoded
        var frames = session.Process(frame.AsMemory(1));
        Assert.NotEmpty(frames);
        Assert.Contains(frames, f => f is SettingsFrame sf && !sf.IsAck);
    }

    [Fact(DisplayName = "FRAG-2-002: HTTP/2 frame header split at byte 3 (end of length)")]
    public void Should_ReturnFalse_When_Http2FrameHeaderSplitAtByte3()
    {
        var frame = SettingsFrame9();
        var session = new Http2ProtocolSession();

        // 3 bytes (length field only) < 9 → NeedMoreData
        Assert.Empty(session.Process(frame.AsMemory(0, 3)));

        var frames = session.Process(frame.AsMemory(3));
        Assert.NotEmpty(frames);
        Assert.Contains(frames, f => f is SettingsFrame sf && !sf.IsAck);
    }

    [Fact(DisplayName = "FRAG-2-003: HTTP/2 frame header split at byte 5 (flags)")]
    public void Should_ReturnFalse_When_Http2FrameHeaderSplitAtByte5()
    {
        var frame = SettingsFrame9();
        var session = new Http2ProtocolSession();

        // 5 bytes (length + type + flags) < 9 → NeedMoreData
        Assert.Empty(session.Process(frame.AsMemory(0, 5)));

        var frames = session.Process(frame.AsMemory(5));
        Assert.NotEmpty(frames);
        Assert.Contains(frames, f => f is SettingsFrame sf && !sf.IsAck);
    }

    [Fact(DisplayName = "FRAG-2-004: HTTP/2 frame header split at byte 8 (last stream byte)")]
    public void Should_ReturnFalse_When_Http2FrameHeaderSplitAtByte8()
    {
        var frame = SettingsFrame9();
        var session = new Http2ProtocolSession();

        // 8 bytes < 9 (missing final stream-id byte) → NeedMoreData
        Assert.Empty(session.Process(frame.AsMemory(0, 8)));

        var frames = session.Process(frame.AsMemory(8));
        Assert.NotEmpty(frames);
        Assert.Contains(frames, f => f is SettingsFrame sf && !sf.IsAck);
    }

    [Fact(DisplayName = "FRAG-2-005: HTTP/2 DATA payload split mid-content")]
    public void Should_BufferAndComplete_When_Http2DataPayloadSplitMidContent()
    {
        // Build HEADERS (stream 1, endStream=false, endHeaders=true, :status=200)
        // followed by DATA (stream 1, endStream=true, body="hello world").
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode(new List<(string, string)> { (":status", "200") });
        var headersFrame = new Protocol.HeadersFrame(1, headerBlock, endStream: false, endHeaders: true).Serialize();
        var bodyBytes = "hello world"u8.ToArray();
        var dataFrame = new Protocol.DataFrame(1, bodyBytes, endStream: true).Serialize();

        var combined = Combine(headersFrame, dataFrame);
        var session = new Http2ProtocolSession();

        // Split: complete HEADERS + complete DATA header (9 bytes) + 5 of 11 body bytes.
        // After HEADERS is processed, working = 14 bytes of DATA; 14 < 9+11 → DATA payload incomplete.
        var split = headersFrame.Length + 9 + 5;
        var frames1 = session.Process(combined.AsMemory(0, split));

        // HEADERS frame was decoded (frames returned) but no complete response yet
        Assert.NotEmpty(frames1);
        Assert.Empty(session.Responses);

        // Feed remaining DATA bytes → completes stream
        Assert.NotEmpty(session.Process(combined.AsMemory(split)));
        Assert.NotEmpty(session.Responses);
        Assert.Equal(1, session.Responses[0].StreamId);
        Assert.Equal(HttpStatusCode.OK, session.Responses[0].Response.StatusCode);
    }

    [Fact(DisplayName = "FRAG-2-006: HTTP/2 HEADERS HPACK block split mid-stream")]
    public void Should_ReturnFalse_When_Http2HpackBlockSplitMidStream()
    {
        // Build HEADERS with a multi-byte HPACK block (endStream=true so decoder closes stream on decode).
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode(new List<(string, string)>
        {
            (":status", "200"),
            ("content-type", "text/plain"),
            ("x-trace-id", "frag-006-test"),
        });
        var headersFrame = new Protocol.HeadersFrame(1, headerBlock, endStream: true, endHeaders: true).Serialize();

        // Ensure the HPACK payload is large enough to split
        Assert.True(headerBlock.Length >= 2, "Need multi-byte HPACK block for mid-stream split");

        // Split within the payload: frame header (9) + half the HPACK block
        var split = 9 + headerBlock.Length / 2;
        var session = new Http2ProtocolSession();

        // First slice: 9-byte frame header + partial payload → payloadLength > bytes-available → NeedMoreData
        Assert.Empty(session.Process(headersFrame.AsMemory(0, split)));

        // Second slice: rest of payload → HPACK decoding completes, response emitted
        Assert.NotEmpty(session.Process(headersFrame.AsMemory(split)));
        Assert.NotEmpty(session.Responses);
        Assert.Equal(HttpStatusCode.OK, session.Responses[0].Response.StatusCode);
    }

    [Fact(DisplayName = "FRAG-2-007: HTTP/2 split between HEADERS and CONTINUATION frames")]
    public void Should_AccumulateAndComplete_When_Http2SplitBetweenHeadersAndContinuation()
    {
        // Build a full HPACK block across two frames: HEADERS(endHeaders=false) + CONTINUATION(endHeaders=true).
        var hpack = new HpackEncoder(useHuffman: false);
        var fullBlock = hpack.Encode(new List<(string, string)>
        {
            (":status", "200"),
            ("x-custom-a", "alpha"),
            ("x-custom-b", "beta"),
        });

        // Put first byte in HEADERS, remaining bytes in CONTINUATION
        var firstPart = fullBlock[..1];
        var secondPart = fullBlock[1..];

        var headersFrame = new Protocol.HeadersFrame(1, firstPart, endStream: true, endHeaders: false).Serialize();
        var contFrame = new Protocol.ContinuationFrame(1, secondPart, endHeaders: true).Serialize();

        var session = new Http2ProtocolSession();

        // First call: deliver HEADERS frame only → frame is processed but response is not yet complete
        // (awaiting CONTINUATION to finalise the header block)
        var frames1 = session.Process(headersFrame);
        Assert.NotEmpty(frames1);         // HEADERS frame decoded
        Assert.Empty(session.Responses);  // no complete response yet

        // Second call: deliver CONTINUATION → header block assembled, response emitted
        Assert.NotEmpty(session.Process(contFrame));
        Assert.NotEmpty(session.Responses);
        Assert.Equal(HttpStatusCode.OK, session.Responses[0].Response.StatusCode);
    }

    [Fact(DisplayName = "FRAG-2-008: Two complete HTTP/2 frames in one read both processed")]
    public void Should_ProcessBothFrames_When_TwoCompleteFramesInOneBuffer()
    {
        var settings1 = new Protocol.SettingsFrame(new List<(SettingsParameter, uint)>
        {
            (SettingsParameter.MaxConcurrentStreams, 100u),
        }).Serialize();

        var settings2 = new Protocol.SettingsFrame(new List<(SettingsParameter, uint)>
        {
            (SettingsParameter.InitialWindowSize, 32768u),
        }).Serialize();

        var combined = Combine(settings1, settings2);
        var session = new Http2ProtocolSession();

        // Single call must process both SETTINGS frames
        var frames = session.Process(combined);
        Assert.NotEmpty(frames);
        Assert.Equal(2, session.ReceivedSettings.Count);
        Assert.Equal(2, frames.Count(f => f is SettingsFrame sf && !sf.IsAck));
    }

    [Fact(DisplayName = "FRAG-2-009: Second stream's HEADERS split across reads while first stream active")]
    public void Should_BufferAndComplete_When_SecondStreamHeadersSplitWhileFirstActive()
    {
        // Stream 1: complete HEADERS (endStream=true, endHeaders=true) — 10 bytes
        // Stream 3: HEADERS (endStream=true, endHeaders=true, :status=200) — 10 bytes; split after frame header
        var hpack1 = new HpackEncoder(useHuffman: false);
        var block1 = hpack1.Encode(new List<(string, string)> { (":status", "200") }); // 1 byte: 0x88

        // Each HEADERS frame = 9-byte header + 1-byte HPACK payload = 10 bytes
        var stream1Frame = new Protocol.HeadersFrame(1, block1, endStream: true, endHeaders: true).Serialize();
        var stream3Frame = new Protocol.HeadersFrame(3, block1, endStream: true, endHeaders: true).Serialize();

        var session = new Http2ProtocolSession();

        // First call: stream-1 HEADERS complete (10 bytes) + stream-3 frame header only (9 bytes).
        // After stream-1: stream-3 working = 9 bytes; payloadLength=1; 9 < 9+1=10 → stored in _remainder.
        var firstSlice = Combine(stream1Frame, stream3Frame[..9]);
        var frames1 = session.Process(firstSlice);
        Assert.NotEmpty(frames1);
        Assert.Single(session.Responses);
        Assert.Equal(1, session.Responses[0].StreamId);
        Assert.Equal(HttpStatusCode.OK, session.Responses[0].Response.StatusCode);

        // Second call: remaining 1 byte of stream-3 HPACK payload
        Assert.NotEmpty(session.Process(stream3Frame.AsMemory(9)));
        Assert.Equal(2, session.Responses.Count);
        Assert.Equal(3, session.Responses[1].StreamId);
        Assert.Equal(HttpStatusCode.OK, session.Responses[1].Response.StatusCode);
    }
}
