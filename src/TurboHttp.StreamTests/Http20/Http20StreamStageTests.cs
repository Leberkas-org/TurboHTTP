using System.IO.Compression;
using System.Net;
using Akka.Streams.Dsl;
using TurboHttp.Protocol.RFC7541;
using TurboHttp.Protocol.RFC9113;
using TurboHttp.Streams.Stages;

namespace TurboHttp.StreamTests.Http20;

public sealed class Http20StreamStageTests : StreamTestBase
{
    private readonly HpackEncoder _hpack = new(useHuffman: false);

    private async Task<IReadOnlyList<HttpResponseMessage>> RunAsync(params Http2Frame[] frames)
    {
        return await Source.From(frames)
            .Via(Flow.FromGraph(new Http20StreamStage()))
            .RunWith(Sink.Seq<HttpResponseMessage>(), Materializer);
    }

    private ReadOnlyMemory<byte> EncodeHeaders(params (string Name, string Value)[] headers)
    {
        return _hpack.Encode(headers);
    }

    // ─── 20S-001: HEADERS with END_STREAM → response without body ────────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-8.1-20S-001: HEADERS with END_STREAM produces response without body")]
    public async Task Headers_With_EndStream_Produces_Response_Without_Body()
    {
        var headerBlock = EncodeHeaders(
            (":status", "200")
        );

        var frames = new Http2Frame[]
        {
            new HeadersFrame(streamId: 1, headerBlock: headerBlock, endStream: true, endHeaders: true)
        };

        var responses = await RunAsync(frames);

        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
        // HEADERS-only (END_STREAM on HEADERS) — no DATA frames, so no body content set by stage
        if (responses[0].Content is not null)
        {
            var body = await responses[0].Content.ReadAsByteArrayAsync();
            Assert.Empty(body);
        }
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-8.1-20S-001: HEADERS-only response with 204 has no body")]
    public async Task Headers_Only_204_No_Body()
    {
        var headerBlock = EncodeHeaders(
            (":status", "204")
        );

        var frames = new Http2Frame[]
        {
            new HeadersFrame(streamId: 1, headerBlock: headerBlock, endStream: true, endHeaders: true)
        };

        var responses = await RunAsync(frames);

        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.NoContent, responses[0].StatusCode);
        // HEADERS-only (END_STREAM on HEADERS) — no DATA frames, so no body content set by stage
        if (responses[0].Content is not null)
        {
            var body = await responses[0].Content.ReadAsByteArrayAsync();
            Assert.Empty(body);
        }
    }

    // ─── 20S-002: HEADERS + DATA with END_STREAM → response with body ────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-8.1-20S-002: HEADERS + DATA with END_STREAM produces response with body")]
    public async Task Headers_Plus_Data_EndStream_Produces_Response_With_Body()
    {
        var headerBlock = EncodeHeaders(
            (":status", "200")
        );
        var body = "Hello, HTTP/2!"u8.ToArray();

        var frames = new Http2Frame[]
        {
            new HeadersFrame(streamId: 1, headerBlock: headerBlock, endStream: false, endHeaders: true),
            new DataFrame(streamId: 1, data: body, endStream: true)
        };

        var responses = await RunAsync(frames);

        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
        var responseBody = await responses[0].Content!.ReadAsByteArrayAsync();
        Assert.Equal(body, responseBody);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-8.1-20S-002: Multiple DATA frames concatenated into single body")]
    public async Task Multiple_Data_Frames_Concatenated()
    {
        var headerBlock = EncodeHeaders(
            (":status", "200")
        );
        var part1 = "Hello, "u8.ToArray();
        var part2 = "World!"u8.ToArray();

        var frames = new Http2Frame[]
        {
            new HeadersFrame(streamId: 1, headerBlock: headerBlock, endStream: false, endHeaders: true),
            new DataFrame(streamId: 1, data: part1, endStream: false),
            new DataFrame(streamId: 1, data: part2, endStream: true)
        };

        var responses = await RunAsync(frames);

        Assert.Single(responses);
        var responseBody = await responses[0].Content!.ReadAsByteArrayAsync();
        Assert.Equal("Hello, World!"u8.ToArray(), responseBody);
    }

    // ─── 20S-003: HEADERS + CONTINUATION + DATA → header block reassembled ───────

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-8.1-20S-003: HEADERS + CONTINUATION reassembles header block before DATA")]
    public async Task Headers_Plus_Continuation_Plus_Data_Reassembled()
    {
        // Encode a header block, then split it across HEADERS and CONTINUATION
        var fullBlock = EncodeHeaders(
            (":status", "200"),
            ("x-custom", "test-value")
        );
        var blockBytes = fullBlock.ToArray();
        var splitAt = blockBytes.Length / 2;
        var part1 = blockBytes[..splitAt];
        var part2 = blockBytes[splitAt..];

        var frames = new Http2Frame[]
        {
            new HeadersFrame(streamId: 1, headerBlock: part1, endStream: false, endHeaders: false),
            new ContinuationFrame(streamId: 1, headerBlock: part2, endHeaders: true),
            new DataFrame(streamId: 1, data: "body"u8.ToArray(), endStream: true)
        };

        var responses = await RunAsync(frames);

        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
        Assert.Equal("test-value", responses[0].Headers.GetValues("x-custom").Single());
        var responseBody = await responses[0].Content!.ReadAsByteArrayAsync();
        Assert.Equal("body"u8.ToArray(), responseBody);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-8.1-20S-003: Multiple CONTINUATION frames all reassembled")]
    public async Task Multiple_Continuation_Frames_Reassembled()
    {
        var fullBlock = EncodeHeaders(
            (":status", "200"),
            ("x-header-a", "value-a"),
            ("x-header-b", "value-b")
        );
        var blockBytes = fullBlock.ToArray();
        var third = blockBytes.Length / 3;
        var part1 = blockBytes[..third];
        var part2 = blockBytes[third..(2 * third)];
        var part3 = blockBytes[(2 * third)..];

        var frames = new Http2Frame[]
        {
            new HeadersFrame(streamId: 1, headerBlock: part1, endStream: false, endHeaders: false),
            new ContinuationFrame(streamId: 1, headerBlock: part2, endHeaders: false),
            new ContinuationFrame(streamId: 1, headerBlock: part3, endHeaders: true),
            new DataFrame(streamId: 1, data: "ok"u8.ToArray(), endStream: true)
        };

        var responses = await RunAsync(frames);

        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
        Assert.Equal("value-a", responses[0].Headers.GetValues("x-header-a").Single());
        Assert.Equal("value-b", responses[0].Headers.GetValues("x-header-b").Single());
    }

    // ─── 20S-004: Multiple streams (ID 1, 3) → separate responses ────────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-8.1-20S-004: Two streams produce two separate responses")]
    public async Task Two_Streams_Produce_Two_Responses()
    {
        // Use a fresh encoder per stream to avoid HPACK dynamic table cross-contamination
        // between the two independent header blocks (the stage's decoder is shared, so
        // the blocks must be encoded with the same encoder in wire order).
        var headerBlock1 = EncodeHeaders(
            (":status", "200")
        );
        var headerBlock3 = EncodeHeaders(
            (":status", "404")
        );

        var frames = new Http2Frame[]
        {
            new HeadersFrame(streamId: 1, headerBlock: headerBlock1, endStream: false, endHeaders: true),
            new HeadersFrame(streamId: 3, headerBlock: headerBlock3, endStream: false, endHeaders: true),
            new DataFrame(streamId: 1, data: "body-1"u8.ToArray(), endStream: true),
            new DataFrame(streamId: 3, data: "body-3"u8.ToArray(), endStream: true)
        };

        var responses = await RunAsync(frames);

        Assert.Equal(2, responses.Count);

        // Responses arrive in completion order (stream 1 first, then 3)
        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
        var body1 = await responses[0].Content!.ReadAsByteArrayAsync();
        Assert.Equal("body-1"u8.ToArray(), body1);

        Assert.Equal(HttpStatusCode.NotFound, responses[1].StatusCode);
        var body3 = await responses[1].Content!.ReadAsByteArrayAsync();
        Assert.Equal("body-3"u8.ToArray(), body3);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-8.1-20S-004: Interleaved frames from two streams correctly separated")]
    public async Task Interleaved_Frames_Two_Streams_Separated()
    {
        var headerBlock1 = EncodeHeaders(
            (":status", "200")
        );
        var headerBlock3 = EncodeHeaders(
            (":status", "201")
        );

        var frames = new Http2Frame[]
        {
            new HeadersFrame(streamId: 1, headerBlock: headerBlock1, endStream: false, endHeaders: true),
            new HeadersFrame(streamId: 3, headerBlock: headerBlock3, endStream: false, endHeaders: true),
            new DataFrame(streamId: 3, data: "three"u8.ToArray(), endStream: true),
            new DataFrame(streamId: 1, data: "one"u8.ToArray(), endStream: true)
        };

        var responses = await RunAsync(frames);

        Assert.Equal(2, responses.Count);

        // Stream 3 completes first (DATA arrives before stream 1's DATA)
        Assert.Equal(HttpStatusCode.Created, responses[0].StatusCode);
        var body3 = await responses[0].Content!.ReadAsByteArrayAsync();
        Assert.Equal("three"u8.ToArray(), body3);

        Assert.Equal(HttpStatusCode.OK, responses[1].StatusCode);
        var body1 = await responses[1].Content!.ReadAsByteArrayAsync();
        Assert.Equal("one"u8.ToArray(), body1);
    }

    // ─── 20S-005: :status pseudo-header → correct HttpStatusCode ─────────────────

    [Theory(Timeout = 10_000, DisplayName = "RFC9113-8.1-20S-005: :status pseudo-header maps to correct HttpStatusCode")]
    [InlineData("200", HttpStatusCode.OK)]
    [InlineData("301", HttpStatusCode.MovedPermanently)]
    [InlineData("404", HttpStatusCode.NotFound)]
    [InlineData("500", HttpStatusCode.InternalServerError)]
    [InlineData("204", HttpStatusCode.NoContent)]
    public async Task Status_PseudoHeader_Maps_To_HttpStatusCode(string statusValue, HttpStatusCode expected)
    {
        // Each theory case needs its own encoder to keep HPACK tables independent
        var encoder = new HpackEncoder(useHuffman: false);
        var headerBlock = encoder.Encode([(":status", statusValue)]);

        var frames = new Http2Frame[]
        {
            new HeadersFrame(streamId: 1, headerBlock: headerBlock, endStream: true, endHeaders: true)
        };

        var responses = await Source.From(frames)
            .Via(Flow.FromGraph(new Http20StreamStage()))
            .RunWith(Sink.Seq<HttpResponseMessage>(), Materializer);

        Assert.Single(responses);
        Assert.Equal(expected, responses[0].StatusCode);
    }

    // ─── 20S-006: Content-Encoding header → decompression applied (gzip) ─────────

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-8.1-20S-006: Content-Encoding gzip triggers decompression")]
    public async Task Content_Encoding_Gzip_Triggers_Decompression()
    {
        var headerBlock = EncodeHeaders(
            (":status", "200"),
            ("content-encoding", "gzip")
        );

        // Gzip-compress the body
        var originalBody = "Hello, compressed world!"u8.ToArray();
        byte[] compressedBody;
        using (var ms = new MemoryStream())
        {
            using (var gzip = new GZipStream(ms, CompressionMode.Compress, leaveOpen: true))
            {
                gzip.Write(originalBody);
            }
            compressedBody = ms.ToArray();
        }

        var frames = new Http2Frame[]
        {
            new HeadersFrame(streamId: 1, headerBlock: headerBlock, endStream: false, endHeaders: true),
            new DataFrame(streamId: 1, data: compressedBody, endStream: true)
        };

        var responses = await RunAsync(frames);

        Assert.Single(responses);
        var responseBody = await responses[0].Content!.ReadAsByteArrayAsync();
        Assert.Equal(originalBody, responseBody);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-8.1-20S-006: No Content-Encoding leaves body unchanged")]
    public async Task No_Content_Encoding_Leaves_Body_Unchanged()
    {
        var headerBlock = EncodeHeaders(
            (":status", "200")
        );
        var body = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };

        var frames = new Http2Frame[]
        {
            new HeadersFrame(streamId: 1, headerBlock: headerBlock, endStream: false, endHeaders: true),
            new DataFrame(streamId: 1, data: body, endStream: true)
        };

        var responses = await RunAsync(frames);

        Assert.Single(responses);
        var responseBody = await responses[0].Content!.ReadAsByteArrayAsync();
        Assert.Equal(body, responseBody);
    }

    // ─── 20S-007: Regular headers (non-pseudo) → present in Response.Headers ─────

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-8.1-20S-007: Regular headers present in response headers")]
    public async Task Regular_Headers_Present_In_Response()
    {
        var headerBlock = EncodeHeaders(
            (":status", "200"),
            ("x-request-id", "abc-123"),
            ("x-custom", "custom-value"),
            ("server", "TurboHttp")
        );

        var frames = new Http2Frame[]
        {
            new HeadersFrame(streamId: 1, headerBlock: headerBlock, endStream: true, endHeaders: true)
        };

        var responses = await RunAsync(frames);

        Assert.Single(responses);
        Assert.Equal("abc-123", responses[0].Headers.GetValues("x-request-id").Single());
        Assert.Equal("custom-value", responses[0].Headers.GetValues("x-custom").Single());
        Assert.Equal("TurboHttp", responses[0].Headers.GetValues("server").Single());
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-8.1-20S-007: Pseudo-headers excluded from response headers collection")]
    public async Task Pseudo_Headers_Excluded_From_Response_Headers()
    {
        var headerBlock = EncodeHeaders(
            (":status", "200"),
            ("x-visible", "yes")
        );

        var frames = new Http2Frame[]
        {
            new HeadersFrame(streamId: 1, headerBlock: headerBlock, endStream: true, endHeaders: true)
        };

        var responses = await RunAsync(frames);

        Assert.Single(responses);
        // :status should NOT appear as a response header
        Assert.False(responses[0].Headers.Contains(":status"));
        // Regular header should be present
        Assert.Equal("yes", responses[0].Headers.GetValues("x-visible").Single());
    }
}
