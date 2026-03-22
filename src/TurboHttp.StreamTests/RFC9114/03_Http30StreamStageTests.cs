using System.IO.Compression;
using System.Net;
using Akka.Streams.Dsl;
using TurboHttp.Protocol.RFC9114;
using TurboHttp.Protocol.RFC9204;
using TurboHttp.Streams.Stages.Decoding;

namespace TurboHttp.StreamTests.RFC9114;

/// <summary>
/// Tests the HTTP/3 stream stage per RFC 9114 §4.1.
/// Verifies that HEADERS and DATA frames are assembled into complete HttpResponseMessage objects
/// with QPACK decoding and content-encoding decompression.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="Http30StreamStage"/>.
/// RFC 9114 §4.1: HTTP/3 request streams carry a sequence of HEADERS and DATA frames.
/// Unlike HTTP/2, there are no CONTINUATION frames and no EndStream flags —
/// stream completion is signaled by QUIC FIN (upstream completion).
/// </remarks>
public sealed class Http30StreamStageTests : StreamTestBase
{
    private readonly QpackEncoder _qpack = new(maxTableCapacity: 0);

    private async Task<IReadOnlyList<(HttpResponseMessage Response, long StreamId)>> RunAsync(params Http3Frame[] frames)
    {
        return await Source.From(frames)
            .Via(Flow.FromGraph(new Http30StreamStage()))
            .RunWith(Sink.Seq<(HttpResponseMessage Response, long StreamId)>(), Materializer);
    }

    private ReadOnlyMemory<byte> EncodeHeaders(params (string Name, string Value)[] headers)
    {
        return _qpack.Encode(headers);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-4.1-30S-001: HEADERS-only stream produces response without body")]
    public async Task Should_ProduceResponseWithoutBody_When_OnlyHeadersFramePresent()
    {
        var headerBlock = EncodeHeaders(
            (":status", "200")
        );

        var responses = await RunAsync(
            new Http3HeadersFrame(headerBlock)
        );

        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.OK, responses[0].Response.StatusCode);
        Assert.Equal(0L, responses[0].StreamId);
        // HEADERS-only (no DATA frames) — no body content set by stage
        if (responses[0].Response.Content is not null)
        {
            var body = await responses[0].Response.Content.ReadAsByteArrayAsync();
            Assert.Empty(body);
        }
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-4.1-30S-002: HEADERS + DATA produces response with body")]
    public async Task Should_ProduceResponseWithBody_When_HeadersPlusDataPresent()
    {
        var headerBlock = EncodeHeaders(
            (":status", "200")
        );
        var body = "Hello, HTTP/3!"u8.ToArray();

        var responses = await RunAsync(
            new Http3HeadersFrame(headerBlock),
            new Http3DataFrame(body)
        );

        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.OK, responses[0].Response.StatusCode);
        var responseBody = await responses[0].Response.Content!.ReadAsByteArrayAsync();
        Assert.Equal(body, responseBody);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-4.1-30S-003: Multiple DATA frames concatenated into single body")]
    public async Task Should_ConcatenateDataFrames_When_MultipleDataFramesArrive()
    {
        var headerBlock = EncodeHeaders(
            (":status", "200")
        );
        var part1 = "Hello, "u8.ToArray();
        var part2 = "World!"u8.ToArray();

        var responses = await RunAsync(
            new Http3HeadersFrame(headerBlock),
            new Http3DataFrame(part1),
            new Http3DataFrame(part2)
        );

        Assert.Single(responses);
        var responseBody = await responses[0].Response.Content!.ReadAsByteArrayAsync();
        Assert.Equal("Hello, World!"u8.ToArray(), responseBody);
    }

    [Theory(Timeout = 10_000, DisplayName = "RFC9114-4.1-30S-004: :status pseudo-header maps to correct HttpStatusCode")]
    [InlineData("200", HttpStatusCode.OK)]
    [InlineData("204", HttpStatusCode.NoContent)]
    [InlineData("301", HttpStatusCode.MovedPermanently)]
    [InlineData("404", HttpStatusCode.NotFound)]
    [InlineData("500", HttpStatusCode.InternalServerError)]
    public async Task Should_MapToCorrectHttpStatusCode_When_StatusPseudoHeaderPresent(string statusValue, HttpStatusCode expected)
    {
        var encoder = new QpackEncoder(maxTableCapacity: 0);
        var headerBlock = encoder.Encode([(":status", statusValue)]);

        var responses = await Source.From<Http3Frame>([new Http3HeadersFrame(headerBlock)])
            .Via(Flow.FromGraph(new Http30StreamStage()))
            .RunWith(Sink.Seq<(HttpResponseMessage Response, long StreamId)>(), Materializer);

        Assert.Single(responses);
        Assert.Equal(expected, responses[0].Response.StatusCode);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-4.1-30S-005: Regular headers present in response headers")]
    public async Task Should_IncludeRegularHeadersInResponse_When_ResponseHeadersPresent()
    {
        var headerBlock = EncodeHeaders(
            (":status", "200"),
            ("x-request-id", "abc-123"),
            ("x-custom", "custom-value"),
            ("server", "TurboHttp")
        );

        var responses = await RunAsync(
            new Http3HeadersFrame(headerBlock)
        );

        Assert.Single(responses);
        Assert.Equal("abc-123", responses[0].Response.Headers.GetValues("x-request-id").Single());
        Assert.Equal("custom-value", responses[0].Response.Headers.GetValues("x-custom").Single());
        Assert.Equal("TurboHttp", responses[0].Response.Headers.GetValues("server").Single());
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-4.1-30S-006: Pseudo-headers excluded from response headers collection")]
    public async Task Should_ExcludePseudoHeadersFromResponseHeaders_When_StatusPseudoHeaderPresent()
    {
        var headerBlock = EncodeHeaders(
            (":status", "200"),
            ("x-visible", "yes")
        );

        var responses = await RunAsync(
            new Http3HeadersFrame(headerBlock)
        );

        Assert.Single(responses);
        Assert.False(responses[0].Response.Headers.Contains(":status"));
        Assert.Equal("yes", responses[0].Response.Headers.GetValues("x-visible").Single());
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-4.1-30S-007: Content-Encoding gzip triggers decompression")]
    public async Task Should_DecompressBody_When_ContentEncodingIsGzip()
    {
        var headerBlock = EncodeHeaders(
            (":status", "200"),
            ("content-encoding", "gzip")
        );

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

        var responses = await RunAsync(
            new Http3HeadersFrame(headerBlock),
            new Http3DataFrame(compressedBody)
        );

        Assert.Single(responses);
        var responseBody = await responses[0].Response.Content!.ReadAsByteArrayAsync();
        Assert.Equal(originalBody, responseBody);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-4.1-30S-008: No Content-Encoding leaves body unchanged")]
    public async Task Should_LeaveBodyUnchanged_When_NoContentEncodingHeader()
    {
        var headerBlock = EncodeHeaders(
            (":status", "200")
        );
        var body = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };

        var responses = await RunAsync(
            new Http3HeadersFrame(headerBlock),
            new Http3DataFrame(body)
        );

        Assert.Single(responses);
        var responseBody = await responses[0].Response.Content!.ReadAsByteArrayAsync();
        Assert.Equal(body, responseBody);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-4.1-30S-009: DATA frame before HEADERS is dropped gracefully")]
    public async Task Should_DropDataFrame_When_ArrivesBeforeHeaders()
    {
        var headerBlock = EncodeHeaders(
            (":status", "200")
        );

        var responses = await RunAsync(
            new Http3DataFrame("orphan"u8.ToArray()),
            new Http3HeadersFrame(headerBlock),
            new Http3DataFrame("ok"u8.ToArray())
        );

        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.OK, responses[0].Response.StatusCode);
        var body = await responses[0].Response.Content!.ReadAsByteArrayAsync();
        Assert.Equal("ok"u8.ToArray(), body);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-4.1-30S-010: Empty DATA frame does not affect body")]
    public async Task Should_IgnoreEmptyDataFrame_When_EmptyDataFrameArrives()
    {
        var headerBlock = EncodeHeaders(
            (":status", "200")
        );

        var responses = await RunAsync(
            new Http3HeadersFrame(headerBlock),
            new Http3DataFrame(ReadOnlyMemory<byte>.Empty),
            new Http3DataFrame("data"u8.ToArray())
        );

        Assert.Single(responses);
        var body = await responses[0].Response.Content!.ReadAsByteArrayAsync();
        Assert.Equal("data"u8.ToArray(), body);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-4.1-30S-011: 204 No Content response has no body")]
    public async Task Should_ProduceNoContentResponse_When_StatusIs204()
    {
        var headerBlock = EncodeHeaders(
            (":status", "204")
        );

        var responses = await RunAsync(
            new Http3HeadersFrame(headerBlock)
        );

        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.NoContent, responses[0].Response.StatusCode);
        // HEADERS-only (no DATA frames) — no body content set by stage
        if (responses[0].Response.Content is not null)
        {
            var body = await responses[0].Response.Content.ReadAsByteArrayAsync();
            Assert.Empty(body);
        }
    }

    // --- RFC 9114 §4.2 Field Validation Integration Tests ---

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-4.2-30S-001: Uppercase header name rejected")]
    public async Task Should_FailStage_When_UppercaseHeaderNamePresent()
    {
        var headerBlock = EncodeHeaders(
            (":status", "200"),
            ("Content-Type", "text/plain")
        );

        var ex = await Assert.ThrowsAsync<Http3Exception>(() => RunAsync(
            new Http3HeadersFrame(headerBlock)
        ));

        Assert.Contains("uppercase", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory(Timeout = 10_000, DisplayName = "RFC9114-4.2-30S-002: Connection-specific headers rejected")]
    [InlineData("connection", "close")]
    [InlineData("transfer-encoding", "chunked")]
    [InlineData("upgrade", "websocket")]
    [InlineData("keep-alive", "timeout=5")]
    public async Task Should_FailStage_When_ConnectionSpecificHeaderPresent(string name, string value)
    {
        var headerBlock = EncodeHeaders(
            (":status", "200"),
            (name, value)
        );

        var ex = await Assert.ThrowsAsync<Http3Exception>(() => RunAsync(
            new Http3HeadersFrame(headerBlock)
        ));

        Assert.Contains("forbidden", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-4.2-30S-003: TE header allowed with value trailers")]
    public async Task Should_AllowTeTrailers_When_TeHeaderHasValueTrailers()
    {
        var headerBlock = EncodeHeaders(
            (":status", "200"),
            ("te", "trailers")
        );

        var responses = await RunAsync(
            new Http3HeadersFrame(headerBlock)
        );

        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.OK, responses[0].Response.StatusCode);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-4.2-30S-004: TE header rejected with non-trailers value")]
    public async Task Should_FailStage_When_TeHeaderHasNonTrailersValue()
    {
        var headerBlock = EncodeHeaders(
            (":status", "200"),
            ("te", "gzip")
        );

        var ex = await Assert.ThrowsAsync<Http3Exception>(() => RunAsync(
            new Http3HeadersFrame(headerBlock)
        ));

        Assert.Contains("TE", ex.Message);
    }

    [Theory(Timeout = 10_000, DisplayName = "RFC9114-4.2-30S-005: NUL, CR, LF in header values rejected")]
    [InlineData("x-bad", "val\0ue", "NUL")]
    [InlineData("x-bad", "val\rue", "CR")]
    [InlineData("x-bad", "val\nue", "LF")]
    public async Task Should_FailStage_When_HeaderValueContainsForbiddenCharacter(string name, string value, string charName)
    {
        var headerBlock = EncodeHeaders(
            (":status", "200"),
            (name, value)
        );

        var ex = await Assert.ThrowsAsync<Http3Exception>(() => RunAsync(
            new Http3HeadersFrame(headerBlock)
        ));

        Assert.Contains(charName, ex.Message);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-4.2-30S-006: Valid lowercase headers pass validation")]
    public async Task Should_PassValidation_When_AllHeadersAreLowercaseAndValid()
    {
        var headerBlock = EncodeHeaders(
            (":status", "200"),
            ("content-type", "text/plain"),
            ("x-request-id", "abc-123"),
            ("cache-control", "no-cache")
        );

        var responses = await RunAsync(
            new Http3HeadersFrame(headerBlock)
        );

        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.OK, responses[0].Response.StatusCode);
    }
}
