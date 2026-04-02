using System.Net;
using Akka.Streams.Dsl;
using TurboHttp.Protocol.Http3;
using TurboHttp.Protocol.Http3.Qpack;
using TurboHttp.Streams.Stages.Decoding;

namespace TurboHttp.StreamTests.Http3;

/// <summary>
/// Tests the HTTP/3 stream stage per RFC 9114 §4.1.
/// Verifies that HEADERS and DATA frames are assembled into complete HttpResponseMessage objects
/// with QPACK decoding. Content-Encoding is preserved for the feature layer.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="Http30StreamStage"/>.
/// RFC 9114 §4.1: HTTP/3 request streams carry a sequence of HEADERS and DATA frames.
/// Unlike HTTP/2, there are no CONTINUATION frames and no EndStream flags —
/// stream completion is signaled by QUIC FIN (upstream completion).
/// </remarks>
public sealed class Http30StreamSpec : StreamTestBase
{
    private readonly QpackEncoder _qpack = new(maxTableCapacity: 0);

    private async Task<IReadOnlyList<HttpResponseMessage>> RunAsync(params Http3Frame[] frames)
    {
        return await Source.From(frames)
            .Via(Flow.FromGraph(new Http30StreamStage()))
            .RunWith(Sink.Seq<HttpResponseMessage>(), Materializer);
    }

    private ReadOnlyMemory<byte> EncodeHeaders(params (string Name, string Value)[] headers)
    {
        return _qpack.Encode(headers);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task Http30Stream_should_produce_response_without_body_when_only_headers_frame_present()
    {
        var headerBlock = EncodeHeaders(
            (":status", "200")
        );

        var responses = await RunAsync(
            new Http3HeadersFrame(headerBlock)
        );

        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
        // HEADERS-only (no DATA frames) — no body content set by stage
        if (responses[0].Content is not null)
        {
            var body = await responses[0].Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
            Assert.Empty(body);
        }
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task Http30Stream_should_produce_response_with_body_when_headers_plus_data_present()
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
        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
        var responseBody = await responses[0].Content!.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(body, responseBody);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task Http30Stream_should_concatenate_data_frames_when_multiple_data_frames_arrive()
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
        var responseBody = await responses[0].Content!.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Hello, World!"u8.ToArray(), responseBody);
    }

    [Theory(Timeout = 10_000)]
    [InlineData("200", HttpStatusCode.OK)]
    [InlineData("204", HttpStatusCode.NoContent)]
    [InlineData("301", HttpStatusCode.MovedPermanently)]
    [InlineData("404", HttpStatusCode.NotFound)]
    [InlineData("500", HttpStatusCode.InternalServerError)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task Http30Stream_should_map_to_correct_http_status_code_when_status_pseudo_header_present(string statusValue, HttpStatusCode expected)
    {
        var encoder = new QpackEncoder(maxTableCapacity: 0);
        var headerBlock = encoder.Encode([(":status", statusValue)]);

        var responses = await Source.From<Http3Frame>([new Http3HeadersFrame(headerBlock)])
            .Via(Flow.FromGraph(new Http30StreamStage()))
            .RunWith(Sink.Seq<HttpResponseMessage>(), Materializer);

        Assert.Single(responses);
        Assert.Equal(expected, responses[0].StatusCode);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task Http30Stream_should_include_regular_headers_in_response_when_response_headers_present()
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
        Assert.Equal("abc-123", responses[0].Headers.GetValues("x-request-id").Single());
        Assert.Equal("custom-value", responses[0].Headers.GetValues("x-custom").Single());
        Assert.Equal("TurboHttp", responses[0].Headers.GetValues("server").Single());
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task Http30Stream_should_exclude_pseudo_headers_from_response_headers_when_status_pseudo_header_present()
    {
        var headerBlock = EncodeHeaders(
            (":status", "200"),
            ("x-visible", "yes")
        );

        var responses = await RunAsync(
            new Http3HeadersFrame(headerBlock)
        );

        Assert.Single(responses);
        Assert.False(responses[0].Headers.Contains(":status"));
        Assert.Equal("yes", responses[0].Headers.GetValues("x-visible").Single());
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task Http30Stream_should_preserve_raw_body_when_content_encoding_is_gzip()
    {
        var headerBlock = EncodeHeaders(
            (":status", "200"),
            ("content-encoding", "gzip")
        );

        var compressedBody = new byte[] { 0x1f, 0x8b, 0x08, 0x00, 0x01, 0x02, 0x03, 0x04 };

        var responses = await RunAsync(
            new Http3HeadersFrame(headerBlock),
            new Http3DataFrame(compressedBody)
        );

        Assert.Single(responses);
        var responseBody = await responses[0].Content!.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        // Stage preserves raw compressed bytes — decompression is handled by ContentEncodingBidiStage
        Assert.Equal(compressedBody, responseBody);
        // Content-Encoding header is preserved on the content headers
        Assert.True(responses[0].Content.Headers.Contains("Content-Encoding"));
        Assert.Equal("gzip", responses[0].Content.Headers.GetValues("Content-Encoding").Single());
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task Http30Stream_should_leave_body_unchanged_when_no_content_encoding_header()
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
        var responseBody = await responses[0].Content!.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(body, responseBody);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task Http30Stream_should_drop_data_frame_when_arrives_before_headers()
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
        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
        var body = await responses[0].Content!.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal("ok"u8.ToArray(), body);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task Http30Stream_should_ignore_empty_data_frame_when_empty_data_frame_arrives()
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
        var body = await responses[0].Content!.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal("data"u8.ToArray(), body);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task Http30Stream_should_produce_no_content_response_when_status_is_204()
    {
        var headerBlock = EncodeHeaders(
            (":status", "204")
        );

        var responses = await RunAsync(
            new Http3HeadersFrame(headerBlock)
        );

        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.NoContent, responses[0].StatusCode);
        // HEADERS-only (no DATA frames) — no body content set by stage
        if (responses[0].Content is not null)
        {
            var body = await responses[0].Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
            Assert.Empty(body);
        }
    }

    // --- RFC 9114 §4.2 Field Validation Integration Tests ---

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-4.2")]
    public async Task Http30Stream_should_fail_stage_when_uppercase_header_name_present()
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

    [Theory(Timeout = 10_000)]
    [InlineData("connection", "close")]
    [InlineData("transfer-encoding", "chunked")]
    [InlineData("upgrade", "websocket")]
    [InlineData("keep-alive", "timeout=5")]
    [Trait("RFC", "RFC9114-4.2")]
    public async Task Http30Stream_should_fail_stage_when_connection_specific_header_present(string name, string value)
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

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-4.2")]
    public async Task Http30Stream_should_allow_te_trailers_when_te_header_has_value_trailers()
    {
        var headerBlock = EncodeHeaders(
            (":status", "200"),
            ("te", "trailers")
        );

        var responses = await RunAsync(
            new Http3HeadersFrame(headerBlock)
        );

        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-4.2")]
    public async Task Http30Stream_should_fail_stage_when_te_header_has_non_trailers_value()
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

    [Theory(Timeout = 10_000)]
    [InlineData("x-bad", "val\0ue", "NUL")]
    [InlineData("x-bad", "val\rue", "CR")]
    [InlineData("x-bad", "val\nue", "LF")]
    [Trait("RFC", "RFC9114-4.2")]
    public async Task Http30Stream_should_fail_stage_when_header_value_contains_forbidden_character(string name, string value, string charName)
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

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-4.2")]
    public async Task Http30Stream_should_pass_validation_when_all_headers_are_lowercase_and_valid()
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
        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
    }
}
