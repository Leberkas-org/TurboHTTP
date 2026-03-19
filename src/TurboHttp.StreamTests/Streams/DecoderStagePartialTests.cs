using System.Net;
using System.Text;
using Akka.Streams.Dsl;
using TurboHttp.Internal;
using TurboHttp.IO.Stages;
using TurboHttp.Protocol.RFC9113;
using TurboHttp.Streams.Stages;

namespace TurboHttp.StreamTests.Streams;

/// <summary>
/// Tests decoder stage partial-frame handling.
/// RFC 9112 §6 (HTTP/1.x) and RFC 9113 §4.1 (HTTP/2):
/// Decoders must buffer incomplete data across TCP boundaries and
/// emit only when a complete message or frame has been received.
/// </summary>
public sealed class DecoderStagePartialTests : StreamTestBase
{
    // ── Helpers ──────────────────────────────────────────────────────────────────
    private static IInputItem H2Chunk(byte[] data)
        => new DataItem(new SimpleMemoryOwner(data), data.Length) { Key = RequestEndpoint.Default };

    private static IInputItem Chunk(string ascii)
    {
        var bytes = Encoding.Latin1.GetBytes(ascii);
        return new DataItem(new SimpleMemoryOwner(bytes), bytes.Length) { Key = RequestEndpoint.Default };
    }

    private async Task<HttpResponseMessage> Decode11Async(
        IEnumerable<IInputItem> fragments)
    {
        return await Source.From(fragments)
            .Via(Flow.FromGraph(new Http11DecoderStage()))
            .RunWith(Sink.First<HttpResponseMessage>(), Materializer);
    }

    private async Task<IReadOnlyList<Http2Frame>> Decode20Async(
        IEnumerable<IInputItem> fragments)
    {
        return await Source.From(fragments)
            .Via(Flow.FromGraph(new Http20DecoderStage()))
            .RunWith(Sink.Seq<Http2Frame>(), Materializer);
    }

    // ── PART-001: HTTP/1.x — incomplete header → decoder waits for next chunk ──

    [Fact(Timeout = 10_000,
        DisplayName =
            "RFC-9112-§6-PART-001: Incomplete HTTP/1.x header → decoder waits, emits only after full header arrives")]
    public async Task Should_WaitForNextChunk_When_Http1xHeaderIsIncomplete()
    {
        // The response header is sent in two chunks:
        //   Chunk 1: "HTTP/1.1 200 OK\r\nContent-Length: 5\r\n"  ← no \r\n\r\n yet
        //   Chunk 2: "\r\nHello"                                  ← completes the header + body
        // The decoder must NOT emit after chunk 1 (headers incomplete).
        // It MUST emit exactly one response after chunk 2 completes the frame.

        const string partialHeaders = "HTTP/1.1 200 OK\r\nContent-Length: 5\r\n";
        const string headerTerminatorAndBody = "\r\nHello";

        var fragments = new List<IInputItem>
        {
            Chunk(partialHeaders),
            Chunk(headerTerminatorAndBody)
        };

        var response = await Decode11Async(fragments);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpVersion.Version11, response.Version);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("Hello", body);
    }

    [Fact(Timeout = 10_000,
        DisplayName =
            "RFC-9112-§6-PART-001b: Header split mid-field → decoder accumulates and correctly parses full header")]
    public async Task Should_DecodeHeaderCorrectly_When_Http1xHeaderSplitMidField()
    {
        // Split inside the status line, before \r\n
        //   Chunk 1: "HTTP/1.1 20"
        //   Chunk 2: "0 OK\r\nContent-Length: 3\r\n\r\nABC"
        var fragments = new List<IInputItem>
        {
            Chunk("HTTP/1.1 20"),
            Chunk("0 OK\r\nContent-Length: 3\r\n\r\nABC")
        };

        var response = await Decode11Async(fragments);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("ABC", body);
    }

    // ── PART-002: HTTP/1.x — body fragment → accumulates until Content-Length ──

    [Fact(Timeout = 10_000,
        DisplayName = "RFC-9112-§6-PART-002: HTTP/1.x body fragment → accumulates until Content-Length reached")]
    public async Task Should_AccumulateBodyFragments_When_Http1xBodyIncomplete()
    {
        // Full body is 15 bytes "AAAAABBBBBCCCCC", sent as 3 separate body fragments.
        // The decoder must NOT emit after partial body fragments; it emits only when
        // all 15 bytes have been received.
        const string bodyText = "AAAAABBBBBCCCCC"; // 15 bytes

        var fragments = new List<IInputItem>
        {
            Chunk($"HTTP/1.1 200 OK\r\nContent-Length: {bodyText.Length}\r\n\r\n"),
            Chunk("AAAAA"), // first 5 bytes — decoder must not emit yet
            Chunk("BBBBB"), // next 5 bytes — still incomplete
            Chunk("CCCCC") // final 5 bytes — now Content-Length satisfied → emit
        };

        var response = await Decode11Async(fragments);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpVersion.Version11, response.Version);
        Assert.Equal(bodyText.Length, response.Content.Headers.ContentLength);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(bodyText, body);
    }

    [Fact(Timeout = 10_000,
        DisplayName = "RFC-9112-§6-PART-002b: HTTP/1.x body arrives byte-by-byte → decoder accumulates all bytes")]
    public async Task Should_AccumulateFullBody_When_Http1xBodyArrivesOneByteAtATime()
    {
        // Send a 4-byte body one byte per TCP chunk to stress the accumulation logic.
        const string bodyText = "Test";

        var fragments = new List<IInputItem>
        {
            Chunk($"HTTP/1.1 200 OK\r\nContent-Length: {bodyText.Length}\r\n\r\n")
        };

        foreach (var ch in bodyText)
        {
            fragments.Add(Chunk(ch.ToString()));
        }

        var response = await Decode11Async(fragments);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(bodyText, body);
    }

    // ── PART-003: HTTP/2 — 5 of 9 header bytes → frame waits for remainder ──────

    [Fact(Timeout = 10_000,
        DisplayName =
            "RFC-9113-§4.1-PART-003: HTTP/2 frame header split at byte 5 → decoder waits for remaining 4 bytes")]
    public async Task Should_WaitForRemainingHeaderBytes_When_Http2FrameHeaderSplitAtByte5()
    {
        // An HTTP/2 frame header is exactly 9 bytes.
        // Sending only bytes 0..4 (5 bytes) must NOT cause the decoder to emit a frame.
        // Sending bytes 5..8 (the remaining 4 header bytes) + payload completes the frame.

        var payload = new byte[] { 0x82, 0x84, 0x86 };
        var rawBytes = new HeadersFrame(streamId: 1, headerBlock: payload, endHeaders: true)
            .Serialize();

        // Frame header: bytes 0–8 (9 bytes), payload: bytes 9+
        Assert.True(rawBytes.Length > 9, "Test prerequisite: frame must be larger than 9 bytes");

        // Send exactly 5 bytes of the 9-byte frame header in chunk 1
        var chunk1 = rawBytes[..5];
        // Send the remaining header bytes + full payload in chunk 2
        var chunk2 = rawBytes[5..];

        var fragments = new List<IInputItem>
        {
            H2Chunk(chunk1),
            H2Chunk(chunk2)
        };

        var frames = await Decode20Async(fragments);

        Assert.Single(frames);
        var headersFrame = Assert.IsType<HeadersFrame>(frames[0]);
        Assert.Equal(1, headersFrame.StreamId);
        Assert.True(headersFrame.EndHeaders);
        Assert.Equal(payload, headersFrame.HeaderBlockFragment.ToArray());
    }

    [Fact(Timeout = 10_000,
        DisplayName =
            "RFC-9113-§4.1-PART-003b: HTTP/2 DATA frame header split at byte 1 → decoder waits for 8 more header bytes")]
    public async Task Should_WaitForRemainingHeaderBytes_When_Http2FrameHeaderHasOneByte()
    {
        // Send only the very first byte of the 9-byte frame header, then the rest.
        var body = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        var rawBytes = new DataFrame(streamId: 3, data: body, endStream: true).Serialize();

        var chunk1 = rawBytes[..1]; // just 1 byte of 9-byte header
        var chunk2 = rawBytes[1..]; // the rest (8 header bytes + payload)

        var fragments = new List<IInputItem>
        {
            H2Chunk(chunk1),
            H2Chunk(chunk2)
        };

        var frames = await Decode20Async(fragments);

        Assert.Single(frames);
        var dataFrame = Assert.IsType<DataFrame>(frames[0]);
        Assert.Equal(3, dataFrame.StreamId);
        Assert.True(dataFrame.EndStream);
        Assert.Equal(body, dataFrame.Data.ToArray());
    }

    // ── PART-004: HTTP/2 — frame payload spread across 3 chunks ─────────────────

    [Fact(Timeout = 10_000,
        DisplayName = "RFC-9113-§4.1-PART-004: HTTP/2 frame payload spread across 3 chunks → correctly reassembled")]
    public async Task Should_ReassembleFrame_When_Http2PayloadSpreadAcrossThreeChunks()
    {
        // A DATA frame with a 12-byte payload is split as:
        //   Chunk 1: 9-byte frame header (complete)
        //   Chunk 2: first 4 bytes of payload
        //   Chunk 3: remaining 8 bytes of payload
        // The decoder must hold the partial payload until all bytes arrive.

        var payload = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C }; // 12 bytes
        var rawBytes = new DataFrame(streamId: 5, data: payload, endStream: true).Serialize();

        // rawBytes layout: [0..8] = 9-byte header, [9..] = payload
        Assert.Equal(9 + payload.Length, rawBytes.Length);

        var chunk1 = rawBytes[..9]; // complete frame header
        var chunk2 = rawBytes[9..13]; // first 4 bytes of payload
        var chunk3 = rawBytes[13..]; // remaining 8 bytes of payload

        var fragments = new List<IInputItem>
        {
            H2Chunk(chunk1),
            H2Chunk(chunk2),
            H2Chunk(chunk3)
        };

        var frames = await Decode20Async(fragments);

        Assert.Single(frames);
        var dataFrame = Assert.IsType<DataFrame>(frames[0]);
        Assert.Equal(5, dataFrame.StreamId);
        Assert.True(dataFrame.EndStream);
        Assert.Equal(payload, dataFrame.Data.ToArray());
    }

    [Fact(Timeout = 10_000,
        DisplayName =
            "RFC-9113-§4.1-PART-004b: HTTP/2 HEADERS frame payload in 3 single-byte chunks + header → reassembled")]
    public async Task Should_ReassembleHeadersFrame_When_Http2PayloadArrivesBytewise()
    {
        // HEADERS frame with 3-byte HPACK payload sent as:
        //   Chunk 1: full 9-byte frame header
        //   Chunk 2: 1st payload byte
        //   Chunk 3: 2nd payload byte
        //   Chunk 4: 3rd payload byte
        var hpackBlock = new byte[] { 0x82, 0x84, 0x86 };
        var rawBytes = new HeadersFrame(streamId: 7, headerBlock: hpackBlock, endHeaders: true)
            .Serialize();

        Assert.Equal(9 + hpackBlock.Length, rawBytes.Length);

        var fragments = new List<IInputItem>
        {
            H2Chunk(rawBytes[..9]), // 9-byte frame header
            H2Chunk(rawBytes[9..10]), // 1st payload byte
            H2Chunk(rawBytes[10..11]), // 2nd payload byte
            H2Chunk(rawBytes[11..]) // 3rd payload byte
        };

        var frames = await Decode20Async(fragments);

        Assert.Single(frames);
        var headersFrame = Assert.IsType<HeadersFrame>(frames[0]);
        Assert.Equal(7, headersFrame.StreamId);
        Assert.True(headersFrame.EndHeaders);
        Assert.Equal(hpackBlock, headersFrame.HeaderBlockFragment.ToArray());
    }
}