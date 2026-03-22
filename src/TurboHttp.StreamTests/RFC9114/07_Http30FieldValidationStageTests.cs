using System.Net;
using Akka.Streams.Dsl;
using TurboHttp.Protocol.RFC9114;
using TurboHttp.Protocol.RFC9204;
using TurboHttp.Streams.Stages.Decoding;

namespace TurboHttp.StreamTests.RFC9114;

/// <summary>
/// Tests HTTP/3 field validation integration in the stream stage per RFC 9114 §4.2.
/// Verifies that <see cref="Http3FieldValidator"/> is invoked inside <see cref="Http30StreamStage"/>
/// after QPACK decode, rejecting malformed or forbidden headers before response assembly.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="Http30StreamStage"/>.
/// RFC 9114 §4.2: Field name requirements — lowercase only, no connection-specific headers,
/// no NUL/CR/LF in values, TE allowed only with "trailers".
/// </remarks>
public sealed class Http30FieldValidationStageTests : StreamTestBase
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

    // ──────────────────────────────────────────────────────────────────────
    // Uppercase Rejection (RFC 9114 §4.2)
    // ──────────────────────────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-4.2-FV-001: Uppercase header name causes stage failure")]
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

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-4.2-FV-002: Mixed-case header name rejected")]
    public async Task Should_FailStage_When_MixedCaseHeaderNamePresent()
    {
        var headerBlock = EncodeHeaders(
            (":status", "200"),
            ("X-Request-Id", "abc")
        );

        var ex = await Assert.ThrowsAsync<Http3Exception>(() => RunAsync(
            new Http3HeadersFrame(headerBlock)
        ));

        Assert.Contains("uppercase", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Connection-Specific Header Rejection (RFC 9114 §4.2)
    // ──────────────────────────────────────────────────────────────────────

    [Theory(Timeout = 10_000, DisplayName = "RFC9114-4.2-FV-003: Connection-specific headers rejected in stream stage")]
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

    // ──────────────────────────────────────────────────────────────────────
    // TE Header Validation (RFC 9114 §4.2)
    // ──────────────────────────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-4.2-FV-004: TE header with value 'trailers' is allowed")]
    public async Task Should_AllowTeTrailers_When_TeHeaderHasTrailersValue()
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

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-4.2-FV-005: TE header with non-trailers value rejected")]
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

    // ──────────────────────────────────────────────────────────────────────
    // Forbidden Characters (RFC 9114 §4.2)
    // ──────────────────────────────────────────────────────────────────────

    [Theory(Timeout = 10_000, DisplayName = "RFC9114-4.2-FV-006: NUL, CR, LF in header values rejected")]
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

    // ──────────────────────────────────────────────────────────────────────
    // Valid Headers Pass Through (RFC 9114 §4.2)
    // ──────────────────────────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-4.2-FV-007: Valid lowercase headers pass validation in stream stage")]
    public async Task Should_ProduceResponse_When_AllHeadersValid()
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

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-4.2-FV-008: HEADERS + DATA frames produce complete response")]
    public async Task Should_ProduceCompleteResponse_When_HeadersFollowedByData()
    {
        var headerBlock = EncodeHeaders(
            (":status", "200"),
            ("content-type", "text/plain")
        );

        var responses = await RunAsync(
            new Http3HeadersFrame(headerBlock),
            new Http3DataFrame(new byte[] { 0x48, 0x69 })
        );

        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.OK, responses[0].Response.StatusCode);
    }
}
