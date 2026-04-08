using System.Net;
using Akka.Streams.Dsl;
using TurboHTTP.Protocol.Http3;
using TurboHTTP.Protocol.Http3.Qpack;
using TurboHTTP.Streams.Stages.Decoding;

namespace TurboHTTP.StreamTests.Http3;

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
public sealed class Http30FieldValidationSpec : StreamTestBase
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

    // Uppercase Rejection (RFC 9114 §4.2)

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-4.2")]
    public async Task Http30FieldValidation_should_fail_stage_when_uppercase_header_name_present()
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

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-4.2")]
    public async Task Http30FieldValidation_should_fail_stage_when_mixed_case_header_name_present()
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

    // Connection-Specific Header Rejection (RFC 9114 §4.2)

    [Theory(Timeout = 10_000)]
    [InlineData("connection", "close")]
    [InlineData("transfer-encoding", "chunked")]
    [InlineData("upgrade", "websocket")]
    [InlineData("keep-alive", "timeout=5")]
    [Trait("RFC", "RFC9114-4.2")]
    public async Task Http30FieldValidation_should_fail_stage_when_connection_specific_header_present(string name, string value)
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

    // TE Header Validation (RFC 9114 §4.2)

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-4.2")]
    public async Task Http30FieldValidation_should_allow_te_trailers_when_te_header_has_trailers_value()
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
    public async Task Http30FieldValidation_should_fail_stage_when_te_header_has_non_trailers_value()
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

    // Forbidden Characters (RFC 9114 §4.2)

    [Theory(Timeout = 10_000)]
    [InlineData("x-bad", "val\0ue", "NUL")]
    [InlineData("x-bad", "val\rue", "CR")]
    [InlineData("x-bad", "val\nue", "LF")]
    [Trait("RFC", "RFC9114-4.2")]
    public async Task Http30FieldValidation_should_fail_stage_when_header_value_contains_forbidden_character(string name, string value, string charName)
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

    // Valid Headers Pass Through (RFC 9114 §4.2)

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-4.2")]
    public async Task Http30FieldValidation_should_produce_response_when_all_headers_valid()
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

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-4.2")]
    public async Task Http30FieldValidation_should_produce_complete_response_when_headers_followed_by_data()
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
        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
    }
}
